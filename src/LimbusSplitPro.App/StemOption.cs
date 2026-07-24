using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LimbusSplitPro.App;

/// <summary>
/// Una categoría del selector "Qué extraer" (sección 5 del encargo).
/// IsAvailable=false representa un modelo sin licencia comercial verificada:
/// se muestra deshabilitada en la UI, con un motivo, nunca oculta ni falsa.
/// </summary>
public sealed class StemOption : INotifyPropertyChanged
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Subtitle { get; init; }
    public required string IconGlyph { get; init; } // carácter de icono (Segoe Fluent Icons)
    public bool IsAvailable { get; init; } = true;
    public string? UnavailableReason { get; init; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
