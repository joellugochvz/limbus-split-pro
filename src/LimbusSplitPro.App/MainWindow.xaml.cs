using System.Windows;

namespace LimbusSplitPro.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closed += (_, _) => (DataContext as IDisposable)?.Dispose();
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

    /// <summary>
    /// Seek arrastrando la barra (sección 15: "Clic y arrastre para hacer seek").
    /// Se marca IsSeeking=true al presionar para que el timer de posición del
    /// ViewModel no pelee con el arrastre del usuario; el seek real ocurre recién
    /// al soltar, con el valor final del Slider (IsMoveToPointEnabled ya lo mueve
    /// al punto del clic antes de que se dispare este evento).
    /// </summary>
    private void SeekSlider_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.IsSeeking = true;
    }

    private void SeekSlider_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm && sender is System.Windows.Controls.Slider slider)
        {
            vm.SeekTo(slider.Value);
            vm.IsSeeking = false;
        }
    }
}
