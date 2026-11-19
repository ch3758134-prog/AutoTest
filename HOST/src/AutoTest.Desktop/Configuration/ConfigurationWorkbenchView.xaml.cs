using System.Windows.Controls;
using AutoTest.Desktop.Projects;

namespace AutoTest.Desktop.Configuration;
public partial class ConfigurationWorkbenchView : UserControl
{
    public ConfigurationWorkbenchView(ConfigurationWorkbenchViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
