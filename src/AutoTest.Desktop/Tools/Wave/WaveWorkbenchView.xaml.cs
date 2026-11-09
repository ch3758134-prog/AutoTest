using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AutoTest.Desktop.Tools.Wave;

public partial class WaveWorkbenchView : UserControl
{
    public const string NavigationKey = "WaveWorkbench";

    public WaveWorkbenchView()
    {
        InitializeComponent();
        DataContext = new WaveWorkbenchViewModel();
    }

    public WaveWorkbenchViewModel Model => (WaveWorkbenchViewModel)DataContext;

    private void ChooseArchive_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "AutoTest CAN RX trace (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog() == true)
        {
            Model.ArchivePath = dialog.FileName;
            if (Model.OpenArchiveCommand.CanExecute(null))
                Model.OpenArchiveCommand.Execute(null);
        }
    }

    private void ChartCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var x = e.GetPosition(ChartCanvas).X;
        var second = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        Model.SetCursorFraction((x - 48) / 852, second);
        e.Handled = true;
    }
}
