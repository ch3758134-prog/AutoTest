using System.Windows;

namespace AutoTest.Desktop.Shell;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
