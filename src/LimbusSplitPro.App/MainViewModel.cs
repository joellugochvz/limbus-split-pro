using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LimbusSplitPro.App;

/// <summary>
/// ViewModel principal. En este commit gestiona el estado de la UI (selección de stems,
/// carpeta de trabajo, lista de pistas del mezclador) con datos de ejemplo/placeholder.
/// La conexión real con LimbusSplitPro.Engine (proceso Python) y LimbusSplitPro.Audio
/// (mezclador WASAPI) es el siguiente paso: hoy los botones "Separar" y "Exportar" no
/// ejecutan separación real todavía, para no simular una función que no existe (ver
/// docs/01-modelos-licencias.md y el propio encargo, sección "no simules capacidades").
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    public ObservableCollection<StemOption> StemOptions { get; } = new();
    public ObservableCollection<TrackChannelViewModel> Tracks { get; } = new();
    public bool HasTracks => Tracks.Count > 0;
    public bool HasNoTracks => !HasTracks;

    private string? _loadedFileName;
    public string? LoadedFileName
    {
        get => _loadedFileName;
        set { _loadedFileName = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasLoadedFile)); OnPropertyChanged(nameof(HasNoLoadedFile)); }
    }

    public bool HasLoadedFile => !string.IsNullOrEmpty(LoadedFileName);
    public bool HasNoLoadedFile => !HasLoadedFile;

    private string _loadedFileInfo = "";
    public string LoadedFileInfo
    {
        get => _loadedFileInfo;
        set { _loadedFileInfo = value; OnPropertyChanged(); }
    }

    private string _workingFolderPath = "Sin seleccionar";
    public string WorkingFolderPath
    {
        get => _workingFolderPath;
        set { _workingFolderPath = value; OnPropertyChanged(); }
    }

    /// <summary>true solo cuando el usuario eligió explícitamente una carpeta real
    /// (sección 4: "Nunca guardes resultados en una ubicación desconocida").</summary>
    private bool _hasWorkingFolder;
    public bool HasWorkingFolder
    {
        get => _hasWorkingFolder;
        set { _hasWorkingFolder = value; OnPropertyChanged(); }
    }

    private string _statusMessage = "Elige qué quieres extraer";
    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    private bool _isProcessing;
    public bool IsProcessing
    {
        get => _isProcessing;
        set { _isProcessing = value; OnPropertyChanged(); OnPropertyChanged(nameof(SeparateButtonLabel)); }
    }

    public string SeparateButtonLabel => IsProcessing ? "Separando..." : "Separar y exportar";

    public RelayCommand SelectAllCommand { get; }
    public RelayCommand SelectNoneCommand { get; }
    public RelayCommand ChooseFileCommand { get; }
    public RelayCommand ChooseFolderCommand { get; }
    public RelayCommand SeparateCommand { get; }

    public MainViewModel()
    {
        SeedStemOptions();
        SeedTracksPlaceholder();

        SelectAllCommand = new RelayCommand(_ =>
        {
            foreach (var s in StemOptions) if (s.IsAvailable) s.IsSelected = true;
        });
        SelectNoneCommand = new RelayCommand(_ =>
        {
            foreach (var s in StemOptions) s.IsSelected = false;
        });

        // TODO (siguiente commit): conectar con un OpenFileDialog nativo real y
        // con LimbusSplitPro.Audio para leer duración/sample rate/canales reales.
        ChooseFileCommand = new RelayCommand(_ =>
        {
            StatusMessage = "Selección de archivo: pendiente de conectar diálogo nativo real.";
        });

        // TODO (siguiente commit): FolderPicker nativo real (sección 4, punto 4).
        ChooseFolderCommand = new RelayCommand(_ =>
        {
            StatusMessage = "Selección de carpeta: pendiente de conectar FolderPicker nativo real.";
        });

        // TODO (siguiente commit): invocar LimbusSplitPro.Engine.EngineProcessClient
        // contra un backend autorizado del manifiesto (hoy no hay ninguno instalado).
        SeparateCommand = new RelayCommand(_ =>
        {
            StatusMessage = "Separación real pendiente: no hay todavía un backend de modelos instalado y verificado en esta build.";
        }, _ => HasLoadedFile && StemOptions.Any(s => s.IsSelected) && HasWorkingFolder);
    }

    private void SeedStemOptions()
    {
        StemOptions.Add(new StemOption { Id = "voces", Title = "Voces", Subtitle = "Base para voz detallada", IconGlyph = "\uE720" });
        StemOptions.Add(new StemOption { Id = "voz_principal", Title = "Voz principal", Subtitle = "Voz al frente", IconGlyph = "\uE720" });
        StemOptions.Add(new StemOption { Id = "coros", Title = "Coros y segundas", Subtitle = "Armonías y dobles", IconGlyph = "\uE716" });
        StemOptions.Add(new StemOption { Id = "efectos_vocales", Title = "Efectos vocales", Subtitle = "Reverb y cola vocal", IconGlyph = "\uE71E" });
        StemOptions.Add(new StemOption { Id = "ruido", Title = "Ruido", Subtitle = "Fondo y artefactos", IconGlyph = "\uE7F3" });
        StemOptions.Add(new StemOption { Id = "bateria", Title = "Batería", Subtitle = "Bombo, caja, toms y platos", IconGlyph = "\uE7C4" });
        StemOptions.Add(new StemOption
        {
            Id = "bateria_detallada",
            Title = "Batería detallada",
            Subtitle = "Bombo / caja / toms / platos por separado",
            IconGlyph = "\uE7C4",
            IsAvailable = false,
            UnavailableReason = "Sin modelo con licencia comercial verificada (ver docs/01-modelos-licencias.md)."
        });
        StemOptions.Add(new StemOption { Id = "bajo", Title = "Bajo", Subtitle = "Bajo eléctrico y sintético", IconGlyph = "\uE71E" });
        StemOptions.Add(new StemOption
        {
            Id = "guitarra",
            Title = "Guitarra",
            Subtitle = "Acústica y eléctrica",
            IconGlyph = "\uE71E",
            IsAvailable = false,
            UnavailableReason = "Solo disponible en htdemucs_6s, bloqueado por licencia de pesos no confirmada."
        });
        StemOptions.Add(new StemOption { Id = "piano", Title = "Piano y teclados", Subtitle = "Piano, órgano y teclas", IconGlyph = "\uE711" });
    }

    private void SeedTracksPlaceholder()
    {
        // Placeholder visual: se reemplaza por las pistas reales generadas por el motor
        // cuando exista un backend instalado (LimbusSplitPro.Engine).
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
