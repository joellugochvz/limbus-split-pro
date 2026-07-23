using System.Diagnostics;
using System.Text.Json;

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
    private readonly string _enginePath;
    private readonly string _pythonHome;
    private Process? _process;

    public EngineProcessClient(string enginePath, string pythonHome)
    {
        _enginePath = enginePath;
        _pythonHome = pythonHome;
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
        psi.ArgumentList.Add("-I"); // aislado: ignora variables de entorno de Python del usuario
        psi.ArgumentList.Add("-m");
        psi.ArgumentList.Add("limbus_engine");

        psi.EnvironmentVariables["PYTHONHOME"] = _pythonHome;
        psi.EnvironmentVariables["PYTHONNOUSERSITE"] = "1";
        psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.Start();

        // Enviamos la solicitud como una única línea JSON por stdin.
        await _process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request));
        await _process.StandardInput.FlushAsync();
        _process.StandardInput.Close();

        // stderr técnico se drena a un logger de archivo, nunca a la UI directamente.
        _ = Task.Run(async () =>
        {
            string? line;
            while ((line = await _process.StandardError.ReadLineAsync()) is not null)
                EngineTechnicalLog.Write(line);
        }, ct);

        while (!ct.IsCancellationRequested)
        {
            var line = await _process.StandardOutput.ReadLineAsync(ct);
            if (line is null) break; // proceso terminó
            EngineEvent? evt;
            try
            {
                evt = JsonSerializer.Deserialize<EngineEvent>(line);
            }
            catch (JsonException)
            {
                // Una línea de stdout que no es JSON válido es un fallo del contrato IPC:
                // se registra como error técnico, nunca se muestra la traza cruda al usuario.
                EngineTechnicalLog.Write($"[IPC] Línea no-JSON inesperada en stdout: {line}");
                continue;
            }
            if (evt is not null) yield return evt;
            if (evt?.Event is "result" or "error" or "cancelled") break;
        }

        if (ct.IsCancellationRequested)
            await CancelAndCleanupAsync();
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
