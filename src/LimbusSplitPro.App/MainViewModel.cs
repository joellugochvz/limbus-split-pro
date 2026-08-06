using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
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
public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
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

    // ===== Reproductor multipista (sección 14-16) =====
    private LimbusSplitPro.Audio.MultiTrackMixer? _mixer;
    private System.Windows.Threading.DispatcherTimer? _positionTimer;

    private bool _isPlaying;
    public bool IsPlaying
    {
        get => _isPlaying;
        set { _isPlaying = value; OnPropertyChanged(); OnPropertyChanged(nameof(PlayPauseLabel)); }
    }

    public string PlayPauseLabel => IsPlaying ? "Pausar" : "Reproducir";

    private string _positionText = "0:00";
    public string PositionText
    {
        get => _positionText;
        set { _positionText = value; OnPropertyChanged(); }
    }

    private string _durationText = "0:00";
    public string DurationText
    {
        get => _durationText;
        set { _durationText = value; OnPropertyChanged(); }
    }

    public RelayCommand PlayPauseCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand ExportMixCommand { get; }

    // ===== Instalacion opcional de Demucs (solo uso personal, ver docs/01-modelos-licencias.md) =====
    private bool _hasDemucsInstalled;
    public bool HasDemucsInstalled
    {
        get => _hasDemucsInstalled;
        set { _hasDemucsInstalled = value; OnPropertyChanged(); OnPropertyChanged(nameof(NeedsDemucsInstall)); }
    }

    public bool NeedsDemucsInstall => !HasDemucsInstalled;

    private bool _isInstallingDemucs;
    public bool IsInstallingDemucs
    {
        get => _isInstallingDemucs;
        set { _isInstallingDemucs = value; OnPropertyChanged(); OnPropertyChanged(nameof(InstallDemucsLabel)); }
    }

    public string InstallDemucsLabel => IsInstallingDemucs ? "Instalando..." : "Instalar Demucs (opcional, uso personal)";

    public RelayCommand InstallDemucsCommand { get; }

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

        PlayPauseCommand = new RelayCommand(_ =>
        {
            if (_mixer is null) return;
            if (IsPlaying) { _mixer.Pause(); IsPlaying = false; }
            else { _mixer.Play(); IsPlaying = true; }
        }, _ => HasTracks);

        StopCommand = new RelayCommand(_ =>
        {
            if (_mixer is null) return;
            _mixer.Stop();
            _mixer.Seek(TimeSpan.Zero);
            IsPlaying = false;
            PositionText = FormatTime(TimeSpan.Zero);
        }, _ => HasTracks);

        ExportMixCommand = new RelayCommand(_ =>
        {
            if (_mixer is null) return;
            try
            {
                var outputPath = Path.Combine(WorkingFolderPath, "Mezcla.wav");
                var result = _mixer.ExportMix(outputPath);
                StatusMessage = result.ClippingDetected
                    ? $"Mezcla exportada en {Path.GetFileName(outputPath)} (aviso: se detectó clipping, considera bajar el volumen de alguna pista)."
                    : $"Mezcla exportada en {Path.GetFileName(outputPath)}.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"No se pudo exportar la mezcla: {ex.Message}";
            }
        }, _ => HasTracks && HasWorkingFolder);

        HasDemucsInstalled = Directory.Exists(Path.Combine(AppContext.BaseDirectory, "runtime", "torch-cache"));

        InstallDemucsCommand = new RelayCommand(async _ => await ExecuteInstallDemucsAsync(),
            _ => !HasDemucsInstalled && !IsInstallingDemucs);

        _positionTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        _positionTimer.Tick += (_, _) =>
        {
            if (_mixer is null) return;
            PositionText = FormatTime(_mixer.CurrentPosition);
            // El WasapiOut pasa a Stopped solo al llegar al final o al llamar Stop();
            // se detecta aquí para reflejar "Reproducir" de nuevo sin que el usuario
            // tenga que pulsar Pausa manualmente cuando la canción termina sola.
            if (IsPlaying && _mixer.PlaybackState == NAudio.Wave.PlaybackState.Stopped)
                IsPlaying = false;
        };
        _positionTimer.Start();
    }

    private static string FormatTime(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    /// <summary>
    /// Carga los WAV generados por la separación real en el mezclador multipista
    /// (sección 14-16). Se llama al recibir el evento "result" del motor.
    /// </summary>
    private void LoadTracksIntoMixer(IReadOnlyList<string> outputFiles)
    {
        _mixer?.Dispose();
        Tracks.Clear();

        _mixer = new LimbusSplitPro.Audio.MultiTrackMixer();
        foreach (var filePath in outputFiles)
        {
            var name = Path.GetFileNameWithoutExtension(filePath);
            var audioChannel = _mixer.AddTrack(name, filePath);

            var vm = new TrackChannelViewModel { Name = name, IconGlyph = "\uE8D6" };
            vm.AudioChannel = audioChannel;
            vm.RequestRecompute = () => _mixer?.RecomputeAudibility();
            Tracks.Add(vm);
        }

        OnPropertyChanged(nameof(HasTracks));
        OnPropertyChanged(nameof(HasNoTracks));
        DurationText = FormatTime(_mixer.TotalDuration);
        PositionText = FormatTime(TimeSpan.Zero);
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
        var manifestPath = Path.Combine(baseDir, "legal", "model-manifest.json");
        var modelsDir = Path.Combine(baseDir, "legal", "models");
        var ffmpegDir = Path.Combine(baseDir, "runtime", "ffmpeg");
        var torchHomeCandidate = Path.Combine(baseDir, "runtime", "torch-cache");
        var torchHome = Directory.Exists(torchHomeCandidate) ? torchHomeCandidate : null;

        if (!File.Exists(enginePath))
        {
            StatusMessage = "Runtime Python no encontrado junto al ejecutable (runtime/python-embed/dist/python.exe). " +
                             "Esta build de desarrollo todavia no lo incluye empaquetado.";
            return;
        }

        IsProcessing = true;
        StatusMessage = "Iniciando el motor de separación...";

        // Límite de tiempo real: un cuelgue silencioso de horas (observado en pruebas reales)
        // ya no es aceptable. 20 minutos es generoso para una canción normal en CPU; si se
        // agota, se cancela con un mensaje claro en vez de quedar "Separando..." para siempre.
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(20));

        try
        {
            var request = new SeparationJobRequest
            {
                InputFilePath = _loadedFilePath ?? "",
                OutputFolderPath = WorkingFolderPath,
                RequestedStems = StemOptions.Where(s => s.IsSelected).Select(s => s.Id).ToList(),
                Device = "auto",
            };

            await using var client = new EngineProcessClient(enginePath, pythonHome, enginePyPath, manifestPath, modelsDir, ffmpegDir, torchHome);
            await foreach (var evt in client.RunAsync(request, timeoutCts.Token))
            {
                if (evt.Event == "result" && evt.OutputFiles is { Count: > 0 })
                    LoadTracksIntoMixer(evt.OutputFiles);

                StatusMessage = evt.Event switch
                {
                    "stage" => $"Etapa: {evt.Stage}",
                    "progress" => $"Procesando... {evt.Pct:0}%",
                    // heartbeat: señal de actividad real del motor (derivada de su log técnico),
                    // para que la UI nunca se quede estática sin ningún indicio de progreso.
                    "heartbeat" => $"Trabajando... ({evt.Message})",
                    "error" => $"No se pudo separar: {evt.Message} (código: {evt.ErrorCode})",
                    "result" => "Separación completada.",
                    "cancelled" => "Separación cancelada.",
                    _ => StatusMessage,
                };
            }
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            StatusMessage = "Se canceló la separación: superó el tiempo máximo esperado (20 min) sin completarse. " +
                             "Puede indicar un problema con el motor (ver logs técnicos).";
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
    /// Instala Demucs (opcional, uso estrictamente personal, ver docs/01-modelos-licencias.md)
    /// dentro del propio runtime Python embebido. Se dispara SOLO por un clic explícito del
    /// usuario (nunca automáticamente), y todo el proceso ocurre en la máquina local del
    /// usuario, sin pasar por GitHub/CI/artifacts públicos en ningún momento.
    /// </summary>
    private async Task ExecuteInstallDemucsAsync()
    {
        var baseDir = AppContext.BaseDirectory;
        var pythonHome = Path.Combine(baseDir, "runtime", "python-embed", "dist");
        var enginePath = Path.Combine(pythonHome, "python.exe");
        var torchCacheDir = Path.Combine(baseDir, "runtime", "torch-cache");

        if (!File.Exists(enginePath))
        {
            StatusMessage = "Runtime Python no encontrado; no se puede instalar Demucs sin él.";
            return;
        }

        IsInstallingDemucs = true;
        StatusMessage = "Instalando Demucs (solo uso personal)...";

        // Sin límite de tiempo corto: la descarga de PyTorch + el modelo puede tardar
        // bastante en una conexión doméstica normal. 60 min es generoso mas no infinito.
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(60));

        try
        {
            var installer = new DemucsInstaller(enginePath, pythonHome, torchCacheDir);
            await foreach (var line in installer.InstallAsync(timeoutCts.Token))
            {
                if (!string.IsNullOrWhiteSpace(line))
                    StatusMessage = line.Length > 140 ? line[..140] + "…" : line;
            }

            HasDemucsInstalled = Directory.Exists(torchCacheDir);
            if (HasDemucsInstalled)
            {
                StatusMessage = "Demucs instalado. Guitarra y piano ya están disponibles.";
                // Se reconstruye la lista de stems para reflejar guitarra/piano habilitados
                // (StemOption.IsAvailable es de solo inicialización, ver StemOption.cs).
                StemOptions.Clear();
                SeedStemOptions();
            }
            else
            {
                StatusMessage = "La instalación terminó pero no se encontró el modelo descargado. Revisa el log técnico.";
            }
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            StatusMessage = "Se canceló la instalación de Demucs: superó el tiempo máximo esperado (60 min).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"No se pudo instalar Demucs: {ex.Message}";
        }
        finally
        {
            IsInstallingDemucs = false;
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
        StemOptions.Add(new StemOption
        {
            Id = "coros",
            Title = "Coros y segundas",
            Subtitle = "Armonías y dobles",
            IconGlyph = "\uE716",
            IsAvailable = false,
            UnavailableReason = "El modelo instalado (Spleeter 4stems) no distingue voz principal de coros."
        });
        StemOptions.Add(new StemOption
        {
            Id = "efectos_vocales",
            Title = "Efectos vocales",
            Subtitle = "Reverb y cola vocal",
            IconGlyph = "\uE71E",
            IsAvailable = false,
            UnavailableReason = "Sin modelo con licencia comercial verificada para esta separación."
        });
        StemOptions.Add(new StemOption
        {
            Id = "ruido",
            Title = "Ruido",
            Subtitle = "Fondo y artefactos",
            IconGlyph = "\uE7F3",
            IsAvailable = false,
            UnavailableReason = "Sin modelo con licencia comercial verificada para esta separación."
        });
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

        // Guitarra y piano solo están disponibles si esta build específica incluye
        // Demucs htdemucs_6s (build de desarrollo, uso personal, repo privado — ver
        // docs/01-modelos-licencias.md). Se detecta en tiempo real, no se asume.
        var hasDemucs = Directory.Exists(Path.Combine(AppContext.BaseDirectory, "runtime", "torch-cache"));

        StemOptions.Add(new StemOption
        {
            Id = "guitarra",
            Title = "Guitarra",
            Subtitle = hasDemucs ? "Acústica y eléctrica (Demucs, uso personal)" : "Acústica y eléctrica",
            IconGlyph = "\uE71E",
            IsAvailable = hasDemucs,
            UnavailableReason = "Solo disponible en htdemucs_6s (build de desarrollo personal), no empaquetado en esta build."
        });
        StemOptions.Add(new StemOption
        {
            Id = "piano",
            Title = "Piano y teclados",
            Subtitle = hasDemucs ? "Piano, órgano y teclas (Demucs, uso personal)" : "Piano, órgano y teclas",
            IconGlyph = "\uE711",
            IsAvailable = hasDemucs,
            UnavailableReason = "Solo disponible en htdemucs_6s (build de desarrollo personal), no empaquetado en esta build."
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Libera el mixer y detiene el timer al cerrar la ventana (sección 8/22:
    /// al cerrar la app no debe quedar el dispositivo de audio ni procesos bloqueados).</summary>
    public void Dispose()
    {
        _positionTimer?.Stop();
        _mixer?.Dispose();
    }
}
