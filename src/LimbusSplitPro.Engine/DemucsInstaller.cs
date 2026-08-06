using System.Diagnostics;

namespace LimbusSplitPro.Engine;

/// <summary>
/// Instala Demucs (opcional, solo build de desarrollo, uso estrictamente personal)
/// dentro del propio runtime Python embebido, sin pasar nunca por GitHub/CI/artifacts
/// públicos. Se dispara SOLO por acción explícita del usuario (un clic), nunca en
/// silencio (sección 7 del encargo: "sin descargar modelos silenciosamente").
///
/// Aviso de licencia (ver docs/01-modelos-licencias.md): los pesos de Demucs no tienen
/// licencia explícita publicada. Este instalador solo debe ofrecerse en builds marcadas
/// "buildType": "development" en el manifiesto, nunca en una build pública/distribuible.
/// </summary>
public sealed class DemucsInstaller
{
    private readonly string _pythonExePath;
    private readonly string _pythonHome;
    private readonly string _torchCacheDir;

    public DemucsInstaller(string pythonExePath, string pythonHome, string torchCacheDir)
    {
        _pythonExePath = pythonExePath;
        _pythonHome = pythonHome;
        _torchCacheDir = torchCacheDir;
    }

    /// <summary>Emite líneas de progreso reales (stdout/stderr de pip y de la descarga
    /// del modelo) a medida que avanza, para que la UI nunca se quede sin señal de vida
    /// durante los varios minutos que puede tardar esto en una conexión normal.</summary>
    public async IAsyncEnumerable<string> InstallAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        yield return "Instalando PyTorch (CPU)... esto puede tardar varios minutos.";
        await foreach (var line in RunPythonCommandAsync(
            new[] { "-m", "pip", "install", "--index-url", "https://download.pytorch.org/whl/cpu", "torch" }, ct))
            yield return line;

        yield return "Instalando Demucs...";
        await foreach (var line in RunPythonCommandAsync(new[] { "-m", "pip", "install", "demucs" }, ct))
            yield return line;

        Directory.CreateDirectory(_torchCacheDir);
        yield return "Descargando el modelo htdemucs_6s (pesos reales, ~150-300 MB)...";
        await foreach (var line in RunPythonCommandAsync(
            new[] { "-c", "from demucs.api import Separator; Separator(model='htdemucs_6s'); print('MODEL_READY')" },
            ct, extraEnv: new Dictionary<string, string> { ["TORCH_HOME"] = _torchCacheDir }))
            yield return line;

        yield return "Demucs instalado correctamente.";
    }

    private async IAsyncEnumerable<string> RunPythonCommandAsync(
        string[] args,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct,
        Dictionary<string, string>? extraEnv = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _pythonExePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        psi.EnvironmentVariables["PYTHONHOME"] = _pythonHome;
        psi.EnvironmentVariables["PYTHONNOUSERSITE"] = "1";
        psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
        if (extraEnv is not null)
            foreach (var (k, v) in extraEnv) psi.EnvironmentVariables[k] = v;

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stderrTask = DrainAsync(process.StandardError, ct);

        string? line;
        while ((line = await process.StandardOutput.ReadLineAsync(ct)) is not null)
            yield return line;

        await foreach (var errLine in stderrTask.WithCancellation(ct))
            yield return errLine;

        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"El comando de instalación terminó con código {process.ExitCode} (python {string.Join(' ', args)}).");
    }

    private static async IAsyncEnumerable<string> DrainAsync(
        StreamReader reader, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
            yield return line;
    }
}
