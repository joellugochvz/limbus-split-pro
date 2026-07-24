using NAudio.Wave;

namespace LimbusSplitPro.Audio;

/// <summary>
/// Lee metadatos reales de un archivo de audio (sección 4, punto 2 del encargo):
/// nombre, formato, duración, frecuencia de muestreo y canales. No reproduce nada,
/// solo abre el header/stream lo justo para leer el formato y lo cierra.
/// </summary>
public static class AudioFileInspector
{
    public static AudioFileInfo Inspect(string filePath)
    {
        if (!File.Exists(filePath))
            throw new AudioInspectionException(AudioInspectionErrorCode.FileNotFound,
                "El archivo ya no existe en la ruta indicada.");

        var extension = Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant();

        try
        {
            // AudioFileReader detecta WAV nativamente y delega a Media Foundation de Windows
            // para MP3/AIFF/FLAC (sección 12: no se asume FFmpeg en PATH del usuario;
            // en la build real conviene verificar en Windows qué formatos resuelve
            // Media Foundation sin dependencias adicionales, especialmente FLAC).
            using var reader = new AudioFileReader(filePath);

            return new AudioFileInfo(
                FileName: Path.GetFileName(filePath),
                Format: extension,
                Duration: reader.TotalTime,
                SampleRate: reader.WaveFormat.SampleRate,
                Channels: reader.WaveFormat.Channels
            );
        }
        catch (IOException ex) when (IsFileLocked(ex))
        {
            throw new AudioInspectionException(AudioInspectionErrorCode.FileLocked,
                "El archivo está siendo usado por otra aplicación.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new AudioInspectionException(AudioInspectionErrorCode.PermissionDenied,
                "Permiso denegado para leer este archivo.", ex);
        }
        catch (Exception ex)
        {
            // Cualquier otro fallo de decodificación se traduce a "formato no compatible"
            // en vez de mostrar una traza técnica cruda al usuario (sección 18/22).
            throw new AudioInspectionException(AudioInspectionErrorCode.UnsupportedFormat,
                $"No se pudo leer este archivo como audio ({extension}).", ex);
        }
    }

    private static bool IsFileLocked(IOException ex) => ex.HResult is -2147024864 or 32;
}

public sealed record AudioFileInfo(
    string FileName,
    string Format,
    TimeSpan Duration,
    int SampleRate,
    int Channels
);

public enum AudioInspectionErrorCode
{
    FileNotFound,
    FileLocked,
    PermissionDenied,
    UnsupportedFormat,
}

public sealed class AudioInspectionException : Exception
{
    public AudioInspectionErrorCode ErrorCode { get; }

    public AudioInspectionException(AudioInspectionErrorCode code, string message, Exception? inner = null)
        : base(message, inner) => ErrorCode = code;
}
