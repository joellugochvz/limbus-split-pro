using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;

namespace LimbusSplitPro.Engine;

/// <summary>
/// Lanza y controla el proceso del motor Python como proceso hijo.
/// Reglas obligatorias del encargo (sección 8):
///  - Nunca cmd.exe con cadenas concatenadas: se usa ArgumentList exclusivamente.
///  - stdout reservado para JSON Lines; stderr para logs técnicos.
///  - Cancelación mediante señal controlada + limpieza de procesos hijos.
///  - Al cerrar la app no debe quedar python.exe huérfano.
/// </summary>
public sealed class EngineProcessClient : IAsyncDisposable
{
    // El motor Python (limbus_engine) usa camelCase en su contrato JSON Lines
    // (inputFilePath, errorCode, outputFiles...). Sin esta política, System.Text.Json
    // serializaría/leería en PascalCase por defecto y la comunicación fallaría en silencio.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _enginePath;
    private readonly string _pythonHome;
    private readonly string _enginePyPath;
    private readonly string _manifestPath;
    private readonly string _modelsDir;
    private readonly string? _ffmpegDir;
    private readonly string? _torchHome;
    private Process? _process;

    /// <param name="enginePath">Ruta a python.exe dentro del runtime embebido.</param>
    /// <param name="pythonHome">Carpeta raíz del runtime Python embebido (PYTHONHOME).</param>
    /// <param name="enginePyPath">Carpeta que contiene el paquete limbus_engine (engine-py/),
    /// pasada como PYTHONPATH ya que el runtime embebido no lo trae instalado.</param>
    /// <param name="manifestPath">Ruta a legal/model-manifest.json (verificación fail-closed).</param>
    /// <param name="modelsDir">Carpeta que contiene los modelos descargados y verificados
    /// (ej. legal/models/, que a su vez contiene spleeter/4stems).</param>
    /// <param name="ffmpegDir">Carpeta que contiene ffmpeg.exe empaquetado, agregada al PATH
    /// del proceso hijo. Spleeter depende del binario ffmpeg para leer/escribir audio; sin
    /// esto puede quedarse colgado indefinidamente sin ningún error visible (comportamiento
    /// real reportado en deezer/spleeter#819) en vez de fallar con un mensaje claro.</param>
    /// <param name="torchHome">Carpeta con los pesos de Demucs pre-descargados (solo build
    /// de desarrollo, uso personal). Si es null, Demucs simplemente no está disponible.</param>
    public EngineProcessClient(string enginePath, string pythonHome, string enginePyPath,
        string manifestPath, string modelsDir, string? ffmpegDir = null, string? torchHome = null)
    {
        _enginePath = enginePath;
        _pythonHome = pythonHome;
        _enginePyPath = enginePyPath;
        _manifestPath = manifestPath;
        _modelsDir = modelsDir;
        _ffmpegDir = ffmpegDir;
        _torchHome = torchHome;
    }

    public async IAsyncEnumerable<EngineEvent> RunAsync(
        SeparationJobRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _enginePath, // runtime\python-embed\python.exe (o engine.exe empaquetado)
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // NUNCA concatenar argumentos como una sola cadena: se añaden uno a uno.
        // NOTA: NO se usa el flag "-I" (modo aislado) porque ese flag ignora
        // PYTHONPATH a nivel del propio intérprete, sin importar el ._pth (bug real
        // encontrado en pruebas: causaba "No module named limbus_engine" siempre).
        // PYTHONNOUSERSITE + PYTHONHOME ya bastan para no depender del entorno del
        // usuario ni de un Python instalado por fuera.
        psi.ArgumentList.Add("-m");
        psi.ArgumentList.Add("limbus_engine");

        psi.EnvironmentVariables["PYTHONHOME"] = _pythonHome;
        psi.EnvironmentVariables["PYTHONNOUSERSITE"] = "1";
        psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
        psi.EnvironmentVariables["PYTHONPATH"] = _enginePyPath;
        psi.EnvironmentVariables["LIMBUS_MANIFEST_PATH"] = _manifestPath;
        psi.EnvironmentVariables["LIMBUS_MODELS_DIR"] = _modelsDir;
        if (!string.IsNullOrEmpty(_torchHome))
            psi.EnvironmentVariables["LIMBUS_TORCH_HOME"] = _torchHome;
        if (!string.IsNullOrEmpty(_ffmpegDir))
        {
            var currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
            psi.EnvironmentVariables["PATH"] = _ffmpegDir + ";" + currentPath;
        }

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.Start();

        // Enviamos la solicitud como una única línea JSON por stdin.
        await _process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request, JsonOptions));
        await _process.StandardInput.FlushAsync();
        _process.StandardInput.Close();

        // Canal compartido: unifica los eventos JSON reales de stdout con "latidos" derivados
        // de stderr, para que la UI nunca se quede mostrando un mensaje estático sin ninguna
        // señal de vida mientras el proceso trabaja (motivo real: un cuelgue de una hora sin
        // ningún indicio observado en pruebas reales).
        var channel = Channel.CreateUnbounded<EngineEvent>();

        var stderrTask = Task.Run(async () =>
        {
            string? line;
            while ((line = await _process.StandardError.ReadLineAsync()) is not null)
            {
                EngineTechnicalLog.Write(line);
                if (!string.IsNullOrWhiteSpace(line))
                {
                    await channel.Writer.WriteAsync(new EngineEvent { Event = "heartbeat", Message = line });
                }
            }
        }, ct);

        var stdoutTask = Task.Run(async () =>
        {
            while (true)
            {
                string? line;
                try
                {
                    line = await _process.StandardOutput.ReadLineAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                if (line is null) break; // proceso terminó

                EngineEvent? evt;
                try
                {
                    evt = JsonSerializer.Deserialize<EngineEvent>(line, JsonOptions);
                }
                catch (JsonException)
                {
                    // Una línea de stdout que no es JSON válido es un fallo del contrato IPC:
                    // se registra como error técnico, nunca se muestra la traza cruda al usuario.
                    EngineTechnicalLog.Write($"[IPC] Línea no-JSON inesperada en stdout: {line}");
                    continue;
                }
                if (evt is not null) await channel.Writer.WriteAsync(evt, ct);
                if (evt?.Event is "result" or "error" or "cancelled") break;
            }
            channel.Writer.TryComplete();
        }, ct);

        await foreach (var evt in channel.Reader.ReadAllAsync(ct))
        {
            yield return evt;
            if (evt.Event is "result" or "error" or "cancelled") break;
        }

        if (ct.IsCancellationRequested)
            await CancelAndCleanupAsync();

        _ = stderrTask; // se deja correr hasta que el proceso cierre stderr; no bloquea la salida
        _ = stdoutTask;
    }

    private async Task CancelAndCleanupAsync()
    {
        if (_process is null || _process.HasExited) return;
        try
        {
            // Señal controlada: el motor Python escucha el cierre de stdin / una línea "cancel"
            // y libera modelos/CUDA antes de salir. Si no responde en un tiempo razonable,
            // se termina el árbol de procesos para no dejar huérfanos.
            _process.CloseMainWindow();
            if (!_process.WaitForExit(5000))
                _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { /* ya había terminado */ }
        await Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_process is { HasExited: false })
            _process.Kill(entireProcessTree: true);
        _process?.Dispose();
        await Task.CompletedTask;
    }
}

public sealed record SeparationJobRequest
{
    public required string InputFilePath { get; init; }
    public required string OutputFolderPath { get; init; }
    public required IReadOnlyList<string> RequestedStems { get; init; }
    public required string Device { get; init; } // "auto" | "cpu" | "gpu"
}

internal static class EngineTechnicalLog
{
    public static void Write(string line) => Trace.WriteLine(line); // sustituir por el sink de logs rotativos por usuario
}
