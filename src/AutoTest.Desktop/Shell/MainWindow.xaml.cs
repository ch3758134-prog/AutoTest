using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AutoTest.Desktop.Shell;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();
}

// Temporary V0 region content. UI direction owns its later replacement.
public sealed class V0RegionPlaceholder : UserControl
{
    public V0RegionPlaceholder()
    {
        Content = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = "INTEGRATION · V0", FontWeight = FontWeights.Bold,
                    Foreground = Brushes.DarkSlateGray, Margin = new Thickness(0, 0, 0, 12) },
                new TextBlock { Text = "Project / DBC: NOT_IMPLEMENTED", TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 12) },
                new TextBlock { Text = "Test editor: NOT_IMPLEMENTED", TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 12) },
                new TextBlock { Text = "CAN / UDS / Wave / Flash: NOT_IMPLEMENTED",
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) },
                new TextBlock { Text = "No USB or real station adapter is loaded.",
                    TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DarkRed }
            }
        };
    }
}
