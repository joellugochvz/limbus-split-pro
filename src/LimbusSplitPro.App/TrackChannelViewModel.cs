using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LimbusSplitPro.App;

/// <summary>
/// Fila del mezclador (sección 16): nombre, mute, solo, volumen.
/// El binding aquí solo actualiza estado visual; la conexión real con
/// LimbusSplitPro.Audio.MultiTrackMixer se hace en el siguiente paso de
/// implementación (todavía no wireada en este commit).
/// </summary>
public sealed class TrackChannelViewModel : INotifyPropertyChanged
{
    public required string Name { get; init; }
    public required string IconGlyph { get; init; }

    private bool _isMuted;
    public bool IsMuted
    {
        get => _isMuted;
        set { _isMuted = value; OnPropertyChanged(); }
    }

    private bool _isSolo;
    public bool IsSolo
    {
        get => _isSolo;
        set { _isSolo = value; OnPropertyChanged(); }
    }

    private double _volumePercent = 100;
    public double VolumePercent
    {
        get => _volumePercent;
        set { _volumePercent = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
