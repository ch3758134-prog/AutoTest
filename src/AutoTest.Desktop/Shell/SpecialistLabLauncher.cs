using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AutoTest.Desktop.Tools.ControllerConfig;
using AutoTest.Desktop.Tools.Dbc;
using AutoTest.Desktop.Tools.ProjectResource;
using AutoTest.Desktop.Tools.Wave;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// Independent, deliberately plain functional test entry. It never constructs
/// the R1 document host, shares no global Mock session and grants no physical Tx.
/// Example: AutoTest.Desktop.exe --lab=dbc
/// The real DEVICE window is the one exception and retains its own B0 gate.
/// </summary>
public static class SpecialistLabLauncher
{
    public static string? RequestedLab(string[] arguments)
    {
        foreach (var argument in arguments)
        {
            if (argument.StartsWith("--lab=", StringComparison.OrdinalIgnoreCase))
                return argument[6..].Trim().ToLowerInvariant();
        }
        return null;
    }

    public static Window Open(string name)
    {
        if (name == "wave") return new WaveOfflineLabWindow();
        if (name == "device") return new AutoTest.DeviceLab.DeviceLabWindow();
        if (name == "flash") return new RealFirmwareImageLabWindow();
        if (name == "scan") return new RealBarcodeCaptureLabWindow();

        (FrameworkElement view, string caption, string mode) = name switch
        {
            "project" => (new ProjectResourceToolView(), "工程文件与资源", "REAL FILES"),
            "dbc" => (new DbcToolView(new DbcToolViewModel()), "DBC 文件编辑与 CAN 归档解码", "REAL FILES / OFFLINE"),
            "controller" => (new ControllerConfigToolView(new ControllerConfigToolViewModel()), "控制器及按键文件配置", "REAL FILES / NO PHYSICAL OUTPUT"),
            _ => throw new ArgumentException(
                "仅已接通的真实功能可独立打开：--lab=project|dbc|controller|wave|flash|scan|device。UDS 真实发送尚未接通，不能运行假诊断。", nameof(name))
        };

        var window = new Window
        {
            Title = "AutoTest · " + caption + " · 独立功能实验室",
            WindowState = WindowState.Maximized,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            MinWidth = 880,
            MinHeight = 600,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            Background = Brushes.White
        };
        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var banner = new DockPanel
        {
            Background = new SolidColorBrush(Color.FromRgb(31, 47, 66)),
            LastChildFill = true,
            Margin = new Thickness(0),
            MinHeight = 44
        };
        var title = new TextBlock
        {
            Text = caption + "   ·   " + mode,
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 6, 14, 6)
        };
        var guide = new TextBlock
        {
            Text = "使用实际文件或设备；没有执行的动作不会报告成功。UDS 实物发送和 ECU 烧录须先完成安全接线。",
            Foreground = new SolidColorBrush(Color.FromRgb(224, 234, 246)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6)
        };
        DockPanel.SetDock(title, Dock.Left);
        banner.Children.Add(title);
        banner.Children.Add(guide);
        Grid.SetRow(banner, 0);
        body.Children.Add(banner);

        var scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = view
        };
        Grid.SetRow(scroller, 1);
        body.Children.Add(scroller);
        window.Content = body;
        return window;
    }
}
