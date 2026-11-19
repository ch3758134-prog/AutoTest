using System.Windows;
using System.Windows.Controls;

namespace AutoTest.Frontend.Views;

public partial class RunWorkspace : UserControl
{
    public event EventHandler? RequestEdit;
    public RunWorkspace() => InitializeComponent();
    private void Edit_Click(object sender, RoutedEventArgs e) =>
        RequestEdit?.Invoke(this, EventArgs.Empty);
}
