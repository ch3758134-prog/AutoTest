using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using AutoTest.Devices.Diagnostics;

namespace AutoTest.Desktop.Tools.Flash;

public partial class FlashToolView : UserControl
{
    private Func<HardwareBenchController?>? _physicalProvider;

    // R1 Station is the only process-local owner of the real physical controller.
    public void AttachPhysicalLink(Func<HardwareBenchController?> provider)
    {
        _physicalProvider = provider ?? throw new ArgumentNullException(nameof(provider));
        RefreshStationInfo();
    }

    private void ImportCanTool_Click(object sender, RoutedEventArgs e)
    {
        if (_physicalRunning) return;
        var picker = new OpenFileDialog
        {
            Filter = "CanTool XML (*.xml)|*.xml|All files (*.*)|*.*",
            CheckFileExists = true,
            Title = "导入实际 CanTool Configuration.xml"
        };
        if (picker.ShowDialog(Window.GetWindow(this)) != true) return;
        ResetPhysicalApproval("XML 配置重新导入。");
        _r1ConfigPath = null;
        try
        {
            if (DataContext is not FlashToolViewModel vm) return;
            vm.ImportCanToolConfiguration(picker.FileName);
            _r1ConfigPath = picker.FileName;
            AppendPhysical("XML_IMPORTED", picker.FileName);
        }
        catch (Exception ex)
        {
            AppendPhysical("XML_ERROR", ex.ToString());
            MessageBox.Show(Window.GetWindow(this), "CanTool 配置解析失败：\n" + ex.Message,
                "ECU Flash", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void PhysicalPreflight_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is FlashToolViewModel vm)
                vm.EvaluatePhysicalPreflight(_physicalProvider?.Invoke());
            RefreshStationInfo();
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), "烧录预检失败：\n" + ex.Message,
                "ECU Flash", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public FlashToolView()
    {
        InitializeComponent();
        DataContext = new FlashToolViewModel();
        InitializeCanToolPanel();
    }

    private async void BrowseImage_Click(object sender, RoutedEventArgs e)
    {
        if (_physicalRunning) return;
        var picker = new OpenFileDialog
        {
            Filter = "Firmware image|*.hex;*.ihex;*.h86;*.s19;*.s28;*.s37;*.srec;*.mot;*.sx;*.bin|All files|*.*",
            CheckFileExists = true, Multiselect = false,
            Title = "选择实际 ECU 固件镜像"
        };
        if (picker.ShowDialog(Window.GetWindow(this)) != true ||
            DataContext is not FlashToolViewModel vm) return;
        ResetPhysicalApproval("选择了新的固件镜像。");
        vm.ImagePath = picker.FileName;
        await vm.LoadFromPathAsync();
        AppendPhysical("IMAGE", vm.Status + "; " + vm.Hash);
    }
}
