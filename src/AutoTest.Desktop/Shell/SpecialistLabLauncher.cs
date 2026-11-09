using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AutoTest.Desktop.Tools.ControllerConfig;
using AutoTest.Desktop.Tools.Dbc;
using AutoTest.Desktop.Tools.Flash;
using AutoTest.Desktop.Tools.Production;
using AutoTest.Desktop.Tools.ProjectResource;
using AutoTest.Desktop.Tools.Testing;
using AutoTest.Desktop.Tools.Uds;
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

        (FrameworkElement view, string caption, string mode) = name switch
        {
            "project" => (new ProjectResourceToolView(), "工程与文件资源", "LOCAL FILES"),
            "dbc" => (new DbcToolView(new DbcToolViewModel()), "DBC 编辑 / 导入 / 保存 / CAN Replay", "OFFLINE / NO CAN TX"),
            "controller" => (new ControllerConfigToolView(new ControllerConfigToolViewModel()), "控制器及按键配置", "OFFLINE / NO PHYSICAL OUTPUT"),
            "uds" => (new UdsToolView { DataContext = new UdsToolViewModel() }, "UDS / ISO-TP 诊断专项", "MOCK / REPLAY ONLY"),
            "flash" => (new FlashToolView { DataContext = new FlashToolViewModel() }, "烧录镜像 / 预检 / 阶段专项", "MOCK DRY-RUN ONLY"),
            "scan" => (new LabProductionToolView(), "扫码输入 / 规则专项", "KEYBOARD INPUT / NO PRODUCTION RELEASE"),
            "test" => (new LabTestWorkspaceView(), "测试编辑及 Mock 执行专项", "MOCK / NO PHYSICAL OUTPUT"),
            _ => throw new ArgumentException(
                "未知专项：请使用 --lab=project|dbc|controller|wave|uds|flash|scan|test|device", nameof(name))
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
            Text = "独立专项：可使用专业页面已有的文件与 Mock/Replay 命令；物理操作须经独立硬件放行。",
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
