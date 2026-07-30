using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LimbusSplitPro.App;

/// <summary>
/// Fila del mezclador (sección 16): nombre, mute, solo, volumen, conectados de verdad
/// al LimbusSplitPro.Audio.TrackChannel subyacente. Mute/Solo/Volumen NUNCA detienen ni
/// reinician la reproducción (sección 15) — solo cambian ganancia con rampa.
/// </summary>
public sealed class TrackChannelViewModel : INotifyPropertyChanged
{
    public required string Name { get; init; }
    public required string IconGlyph { get; init; }

    /// <summary>Pista de audio real subyacente. Se asigna después de añadirla al mixer.</summary>
    public LimbusSplitPro.Audio.TrackChannel? AudioChannel { get; set; }

    /// <summary>Llamado tras cambiar Mute/Solo para recalcular audibilidad global
    /// (sección 16: con algún solo activo, solo suenan las pistas en solo sin mute).</summary>
    public Action? RequestRecompute { get; set; }

    private bool _isMuted;
    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            _isMuted = value;
            AudioChannel?.SetMute(value);
            RequestRecompute?.Invoke();
            OnPropertyChanged();
        }
    }

    private bool _isSolo;
    public bool IsSolo
    {
        get => _isSolo;
        set
        {
            _isSolo = value;
            AudioChannel?.SetSolo(value);
            RequestRecompute?.Invoke();
            OnPropertyChanged();
        }
    }

    private double _volumePercent = 100;
    public double VolumePercent
    {
        get => _volumePercent;
        set
        {
            _volumePercent = value;
            AudioChannel?.SetVolume((float)(value / 100.0));
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
