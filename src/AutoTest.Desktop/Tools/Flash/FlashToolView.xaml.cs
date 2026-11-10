using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
namespace AutoTest.Desktop.Tools.Flash;
public partial class FlashToolView : UserControl
{
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
