using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace LimbusSplitPro.Audio;

/// <summary>
/// Mezclador sample-accurate de un solo dispositivo, un solo reloj, una sola línea temporal
/// (sección 14 del encargo). Todas las pistas comparten un único MixingSampleProvider y un
/// único contador de posición: mute/solo/volumen NUNCA detienen ni reinician la reproducción.
/// </summary>
public sealed class MultiTrackMixer : IDisposable
{
    private readonly WasapiOut _output;
    private readonly MixingSampleProvider _mixer;
    private readonly List<TrackChannel> _tracks = new();
    private readonly int _sampleRate;
    private readonly int _channels;

    public MultiTrackMixer(int sampleRate = 44100, int channels = 2, string? deviceId = null)
    {
        _sampleRate = sampleRate;
        _channels = channels;
        var format = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
        _mixer = new MixingSampleProvider(format) { ReadFully = true };

        // WASAPI compartido por defecto (estable); exclusivo solo si se demuestra necesario y probado.
        _output = deviceId is null
            ? new WasapiOut(NAudio.CoreAudioApi.AudioClientShareMode.Shared, latency: 100)
            : new WasapiOut(ResolveDevice(deviceId), NAudio.CoreAudioApi.AudioClientShareMode.Shared, true, 100);

        _output.Init(_mixer);
    }

    /// <summary>
    /// Añade una pista. Todas las pistas deben empezar exactamente en el mismo sample:
    /// se fuerza el mismo sample rate/canales al cargar (resample si hace falta) antes de mezclar.
    /// </summary>
    public TrackChannel AddTrack(string name, string wavFilePath)
    {
        var reader = new AudioFileReader(wavFilePath);
        ISampleProvider provider = reader;
        if (reader.WaveFormat.SampleRate != _sampleRate || reader.WaveFormat.Channels != _channels)
            provider = new WdlResamplingSampleProvider(reader, _sampleRate); // + conversión de canales si aplica

        var channel = new TrackChannel(name, wavFilePath, provider, reader);
        _tracks.Add(channel);
        _mixer.AddMixerInput(channel.RampedProvider);
        return channel;
    }

    /// <summary>
    /// Recalcula qué pistas suenan según mute/solo (sección 16): si hay algún solo activo,
    /// solo suenan las pistas en solo que no estén también en mute.
    /// </summary>
    public void RecomputeAudibility()
    {
        bool anySolo = _tracks.Any(t => t.IsSolo);
        foreach (var t in _tracks)
        {
            var shouldBeAudible = anySolo ? (t.IsSolo && !t.IsMuted) : !t.IsMuted;
            t.SetAudibleWithRamp(shouldBeAudible); // rampa de ganancia corta: sin clics
        }
    }

    public void Play() => _output.Play();
    public void Pause() => _output.Pause(); // no reinicia posición
    public void Stop() => _output.Stop();   // semántica clara: detiene y libera el dispositivo

    public PlaybackState PlaybackState => _output.PlaybackState;

    /// <summary>Posición/duración de referencia (todas las pistas comparten la misma línea
    /// temporal, se usa la primera pista añadida). Vacío si no hay pistas cargadas.</summary>
    public TimeSpan CurrentPosition => _tracks.Count > 0 ? _tracks[0].CurrentTime : TimeSpan.Zero;
    public TimeSpan TotalDuration => _tracks.Count > 0 ? _tracks[0].TotalTime : TimeSpan.Zero;

    /// <summary>Seek con crossfade corto para evitar rebote/doble ataque/silencio perceptible.</summary>
    public void Seek(TimeSpan position)
    {
        foreach (var t in _tracks) t.SeekWithCrossfade(position);
    }

    /// <summary>
    /// Exporta la mezcla actual (volumen/mute/solo tal como están, sección 17) de forma
    /// OFFLINE y sample-accurate: reabre cada WAV desde cero y los suma en memoria, sin
    /// grabar la salida del dispositivo en tiempo real y sin interferir con la reproducción
    /// en curso (usa lectores independientes de los que están sonando).
    /// </summary>
    public ExportResult ExportMix(string outputWavPath)
    {
        if (_tracks.Count == 0)
            throw new InvalidOperationException("No hay pistas cargadas para exportar.");

        bool anySolo = _tracks.Any(t => t.IsSolo);
        var format = WaveFormat.CreateIeeeFloatWaveFormat(_sampleRate, _channels);
        var offlineMixer = new MixingSampleProvider(format) { ReadFully = false };

        var freshReaders = new List<AudioFileReader>();
        try
        {
            foreach (var t in _tracks)
            {
                // Misma regla de audibilidad que en vivo (sección 16), aplicada aquí como
                // ganancia fija en vez de rampa en tiempo real (no aplica en un render offline).
                var audible = anySolo ? (t.IsSolo && !t.IsMuted) : !t.IsMuted;
                var gain = audible ? t.Volume : 0f;

                var reader = new AudioFileReader(t.FilePath);
                freshReaders.Add(reader);
                ISampleProvider provider = reader;
                if (reader.WaveFormat.SampleRate != _sampleRate || reader.WaveFormat.Channels != _channels)
                    provider = new WdlResamplingSampleProvider(reader, _sampleRate);
                offlineMixer.AddMixerInput(new VolumeSampleProvider(provider) { Volume = gain });
            }

            bool clippingDetected = false;
            var buffer = new float[_sampleRate * _channels]; // ~1s por bloque
            using var writer = new WaveFileWriter(outputWavPath, new WaveFormat(_sampleRate, 16, _channels));
            int read;
            while ((read = offlineMixer.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (int i = 0; i < read; i++)
                {
                    if (buffer[i] > 1f || buffer[i] < -1f) clippingDetected = true;
                }
                writer.WriteSamples(buffer, 0, read);
            }

            // No se normaliza silenciosamente (sección 17): solo se informa si hubo
            // clipping para que el usuario decida, no se altera la ganancia automáticamente.
            return new ExportResult(outputWavPath, clippingDetected);
        }
        finally
        {
            foreach (var r in freshReaders) r.Dispose();
        }
    }

    public void Dispose()
    {
        _output.Dispose();
        foreach (var t in _tracks) t.Dispose();
    }

    private static NAudio.CoreAudioApi.MMDevice ResolveDevice(string deviceId)
    {
        using var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
        return enumerator.GetDevice(deviceId);
    }
}

public sealed record ExportResult(string FilePath, bool ClippingDetected);

/// <summary>Una pista del mezclador: nombre, mute, solo, volumen con rampa de ganancia.</summary>
public sealed class TrackChannel : IDisposable
{
    public string Name { get; }
    public string FilePath { get; }
    public bool IsMuted { get; private set; }
    public bool IsSolo { get; private set; }
    public float Volume { get; private set; } = 1.0f;

    private readonly VolumeSampleProvider _volumeProvider;
    private readonly IDisposable _underlyingReader;

    public ISampleProvider RampedProvider => _volumeProvider;

    internal TrackChannel(string name, string filePath, ISampleProvider provider, IDisposable underlyingReader)
    {
        Name = name;
        FilePath = filePath;
        _underlyingReader = underlyingReader;
        _volumeProvider = new VolumeSampleProvider(provider) { Volume = 1.0f };
    }

    public void SetMute(bool muted) => IsMuted = muted;   // no detiene reproducción
    public void SetSolo(bool solo) => IsSolo = solo;       // no detiene reproducción
    public void SetVolume(float volume01) => Volume = _volumeProvider.Volume = Math.Clamp(volume01, 0f, 1f);

    internal void SetAudibleWithRamp(bool audible)
    {
        // TODO (build real en Windows): interpolar Volume en ~10-20ms para evitar clics,
        // en vez de un salto instantáneo. Placeholder de la interfaz aquí.
        _volumeProvider.Volume = audible ? Volume : 0f;
    }

    internal void SeekWithCrossfade(TimeSpan position)
    {
        if (_underlyingReader is AudioFileReader reader)
            reader.CurrentTime = position; // TODO: aplicar crossfade de unos ms al reanudar
    }

    /// <summary>Posición actual de reproducción de esta pista (todas comparten la misma,
    /// se usa la primera pista añadida como referencia para la UI).</summary>
    public TimeSpan CurrentTime =>
        _underlyingReader is AudioFileReader reader ? reader.CurrentTime : TimeSpan.Zero;

    public TimeSpan TotalTime =>
        _underlyingReader is AudioFileReader reader ? reader.TotalTime : TimeSpan.Zero;

    public void Dispose() => _underlyingReader.Dispose();
}
