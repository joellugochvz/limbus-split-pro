using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using LimbusSplitPro.Audio;
using LimbusSplitPro.Engine;
using Microsoft.Win32;

namespace LimbusSplitPro.App;

/// <summary>
/// ViewModel principal. Diálogos nativos (OpenFileDialog / OpenFolderDialog) y lectura
/// real de metadatos de audio ya están conectados. La separación real (motor Python) y
/// la reproducción multipista (MultiTrackMixer) siguen pendientes: no hay todavía un
/// backend de modelos instalado y verificado en esta build (ver docs/01-modelos-licencias.md).
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    public ObservableCollection<StemOption> StemOptions { get; } = new();
    public ObservableCollection<TrackChannelViewModel> Tracks { get; } = new();
    public bool HasTracks => Tracks.Count > 0;
    public bool HasNoTracks => !HasTracks;

    private string? _loadedFilePath;

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

        SelectAllCommand = new RelayCommand(_ =>
        {
            foreach (var s in StemOptions) if (s.IsAvailable) s.IsSelected = true;
        });
        SelectNoneCommand = new RelayCommand(_ =>
        {
            foreach (var s in StemOptions) s.IsSelected = false;
        });

        ChooseFileCommand = new RelayCommand(_ =>
        {
            var dialog = new OpenFileDialog
            {
                Title = "Elegir mezcla",
                Filter = "Audio (*.wav;*.aiff;*.aif;*.mp3;*.flac)|*.wav;*.aiff;*.aif;*.mp3;*.flac|Todos los archivos|*.*",
                CheckFileExists = true,
            };
            if (dialog.ShowDialog() == true)
                LoadFile(dialog.FileName);
        });

        ChooseFolderCommand = new RelayCommand(_ =>
        {
            // OpenFolderDialog es la API nativa de FolderPicker desde .NET 8 en WPF
            // (sección 4, punto 4: "Elegir una carpeta de trabajo... mediante FolderPicker").
            var dialog = new OpenFolderDialog { Title = "Elegir carpeta de trabajo y exportación" };
            if (dialog.ShowDialog() == true)
            {
                WorkingFolderPath = dialog.FolderName;
                HasWorkingFolder = true;
            }
        });

        SeparateCommand = new RelayCommand(async _ => await ExecuteSeparateAsync(),
            _ => HasLoadedFile && StemOptions.Any(s => s.IsSelected) && HasWorkingFolder && !IsProcessing);
    }

    /// <summary>
    /// Invoca el motor Python real como proceso hijo (LimbusSplitPro.Engine). Hoy termina
    /// en un error controlado (MODEL_NOT_AUTHORIZED) porque no hay ningun modelo instalado
    /// y verificado todavia en esta build de desarrollo - eso es el comportamiento correcto
    /// y esperado (fail-closed, seccion 7), no un bug.
    /// </summary>
    private async Task ExecuteSeparateAsync()
    {
        var baseDir = AppContext.BaseDirectory;
        var pythonHome = Path.Combine(baseDir, "runtime", "python-embed", "dist");
        var enginePath = Path.Combine(pythonHome, "python.exe");
        var enginePyPath = Path.Combine(baseDir, "engine-py");

        if (!File.Exists(enginePath))
        {
            StatusMessage = "Runtime Python no encontrado junto al ejecutable (runtime/python-embed/dist/python.exe). " +
                             "Esta build de desarrollo todavia no lo incluye empaquetado.";
            return;
        }

        IsProcessing = true;
        StatusMessage = "Iniciando el motor de separación...";

        try
        {
            var request = new SeparationJobRequest
            {
                InputFilePath = _loadedFilePath ?? "",
                OutputFolderPath = WorkingFolderPath,
                RequestedStems = StemOptions.Where(s => s.IsSelected).Select(s => s.Id).ToList(),
                Device = "auto",
            };

            await using var client = new EngineProcessClient(enginePath, pythonHome, enginePyPath);
            await foreach (var evt in client.RunAsync(request, CancellationToken.None))
            {
                StatusMessage = evt.Event switch
                {
                    "stage" => $"Etapa: {evt.Stage}",
                    "progress" => $"Procesando... {evt.Pct:0}%",
                    "error" => $"No se pudo separar: {evt.Message} (código: {evt.ErrorCode})",
                    "result" => "Separación completada.",
                    "cancelled" => "Separación cancelada.",
                    _ => StatusMessage,
                };
            }
        }
        catch (Exception ex)
        {
            // Frontera del proceso hijo: cualquier fallo de arranque (ej. python.exe corrupto,
            // permiso denegado) se traduce a un mensaje comprensible, nunca a una traza cruda.
            StatusMessage = $"No se pudo iniciar el motor de separación: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    /// <summary>
    /// Carga y valida un archivo de audio real, ya sea desde el diálogo o desde
    /// arrastrar-y-soltar (code-behind de MainWindow). Admite rutas con espacios,
    /// Unicode y unidades distintas a C: porque no se manipulan como texto: se pasan
    /// directo a File.Exists/AudioFileReader (sección 4).
    /// </summary>
    public void LoadFile(string filePath)
    {
        try
        {
            var info = AudioFileInspector.Inspect(filePath);
            _loadedFilePath = filePath;
            LoadedFileName = info.FileName;
            LoadedFileInfo = $"{info.Format} · {info.Duration:m\\:ss} · {info.SampleRate} Hz · " +
                              (info.Channels == 1 ? "Mono" : info.Channels == 2 ? "Estéreo" : $"{info.Channels} canales");
            StatusMessage = "Elige qué quieres extraer";
        }
        catch (AudioInspectionException ex)
        {
            // Mensaje comprensible, sin traza técnica cruda (sección 18/22 del encargo).
            _loadedFilePath = null;
            LoadedFileName = null;
            StatusMessage = ex.ErrorCode switch
            {
                AudioInspectionErrorCode.FileLocked => "No se pudo abrir: el archivo está en uso por otra aplicación.",
                AudioInspectionErrorCode.PermissionDenied => "No se pudo abrir: permiso denegado para este archivo.",
                AudioInspectionErrorCode.UnsupportedFormat => "Formato no compatible o archivo dañado.",
                AudioInspectionErrorCode.FileNotFound => "El archivo ya no existe en esa ruta.",
                _ => "No se pudo abrir el archivo.",
            };
        }
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

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
