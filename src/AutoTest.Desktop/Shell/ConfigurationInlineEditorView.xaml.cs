using System.Windows.Controls;
using AutoTest.Desktop.Configuration;

namespace AutoTest.Desktop.Shell;
public partial class ConfigurationInlineEditorView : UserControl
{
    public ConfigurationInlineEditorView(ConfigurationWorkbenchViewModel model)
    {
        InitializeComponent();
        DataContext = model;
    }
}
