using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using AutoTest.Devices.Diagnostics;
namespace AutoTest.Desktop.Tools.Flash;
public partial class FlashToolView : UserControl
{
    private Func<HardwareBenchController?>? _physicalProvider;
    public void AttachPhysicalLink(Func<HardwareBenchController?> provider) =>
        _physicalProvider=provider ?? throw new ArgumentNullException(nameof(provider));
    private void ImportCanTool_Click(object sender,RoutedEventArgs e)
    {
        var picker=new OpenFileDialog
        {
            Filter="CanTool XML (*.xml)|*.xml|All files (*.*)|*.*",
            CheckFileExists=true,
            Title="导入 CanTool Configuration.xml（只读配置解析）"
        };
        if(picker.ShowDialog()!=true) return;
        try { if(DataContext is FlashToolViewModel vm) vm.ImportCanToolConfiguration(picker.FileName); }
        catch(Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this),"CanTool 配置解析失败：\n"+ex.Message,
                "ECU Flash",MessageBoxButton.OK,MessageBoxImage.Warning);
        }
    }
    private void PhysicalPreflight_Click(object sender,RoutedEventArgs e)
    {
        try { if(DataContext is FlashToolViewModel vm) vm.EvaluatePhysicalPreflight(_physicalProvider?.Invoke()); }
        catch(Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this),"烧录预检失败：\n"+ex.Message,
                "ECU Flash",MessageBoxButton.OK,MessageBoxImage.Warning);
        }
    }
    public FlashToolView()
    {
        InitializeComponent();
        DataContext=new FlashToolViewModel();
    }
    private void BrowseImage_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog
        {
            Filter = "Firmware image|*.hex;*.ihex;*.s19;*.s28;*.s37;*.srec;*.mot;*.bin|All files|*.*",
            CheckFileExists = true,
            Multiselect = false,
            Title = "选择固件镜像（仅用于离线解析 / Mock Dry-run）"
        };
        if (picker.ShowDialog() != true || DataContext is not FlashToolViewModel vm) return;
        vm.ImagePath = picker.FileName;
        _ = vm.LoadFromPathAsync();
    }
}
