using System.Windows;

namespace LimbusSplitPro.App;

public partial class App : Application
{
    protected override void OnExit(ExitEventArgs e)
    {
        // Requisito crítico (sección 8/22): al cerrar, ningún proceso hijo (python.exe,
        // procesos de inferencia) debe quedar huérfano. El EngineProcessClient activo
        // se cierra aquí mediante DisposeAsync antes de salir.
        base.OnExit(e);
    }
}
