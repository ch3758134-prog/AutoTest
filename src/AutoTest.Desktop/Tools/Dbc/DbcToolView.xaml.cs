using System.Windows.Controls;

namespace AutoTest.Desktop.Tools.Dbc;

public partial class DbcToolView : UserControl
{
    public const string NavigationKey = "DbcTool";
    public DbcToolView(DbcToolViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
