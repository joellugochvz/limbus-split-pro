using System.Windows;

namespace LimbusSplitPro.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Arrastrar-y-soltar sobre la zona de mezcla (sección 4, punto 1: "Arrastrar una
    /// canción o seleccionarla con un diálogo nativo"). Solo acepta un único archivo;
    /// el resto de la validación real (formato, lectura de metadatos) la hace
    /// MainViewModel.LoadFile, igual que el diálogo nativo, para no duplicar lógica.
    /// </summary>
    private void DropZoneBorder_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void DropZoneBorder_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths) return;
        if (DataContext is MainViewModel vm)
            vm.LoadFile(paths[0]); // admite rutas con espacios/Unicode: se pasa el string tal cual, sin concatenar
    }
}
