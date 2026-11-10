using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace AutoTest.Desktop.Tools.Uds;

/// <summary>R1 P08 document; the Shell injects a shared physical controller
/// provider, never opens another USB device/session.</summary>
public partial class UdsToolView : UserControl
{
    public UdsToolView()
    {
        InitializeComponent();
        DataContext=new UdsToolViewModel();
    }
    private UdsToolViewModel? Model => DataContext as UdsToolViewModel;
    private void ImportCanTool_Click(object sender,RoutedEventArgs e)
    {
        var picker=new OpenFileDialog
        {
            Filter="CanTool XML (*.xml)|*.xml|All files (*.*)|*.*",
            Title="导入现有 CanTool Configuration.xml（只读解析，不发送诊断指令）"
        };
        if (picker.ShowDialog() != true) return;
        try { Model?.ImportCanToolConfiguration(picker.FileName); }
        catch(Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this),
                "导入 CanTool 诊断端点失败：\n"+ex.Message,
                "UDS Profile",MessageBoxButton.OK,MessageBoxImage.Warning);
        }
    }
    private void UseCanToolApp_Click(object sender,RoutedEventArgs e) =>
        Model?.UseCanToolEndpoint(false);
    private void UseCanToolBoot_Click(object sender,RoutedEventArgs e) =>
        Model?.UseCanToolEndpoint(true);
}
