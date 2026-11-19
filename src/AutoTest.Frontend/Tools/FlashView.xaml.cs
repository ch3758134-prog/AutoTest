using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace AutoTest.Frontend.Tools;

public partial class FlashView : UserControl
{
    public FlashView() => InitializeComponent();
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择烧录镜像路径（仅 UI 草稿，未检查镜像）",
            Filter = "镜像文件 (*.hex;*.s19;*.srec;*.bin)|*.hex;*.s19;*.srec;*.bin|所有文件 (*.*)|*.*"
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        ((UiWorkbenchState)DataContext).FlashFilePath = dlg.FileName;
    }
}
