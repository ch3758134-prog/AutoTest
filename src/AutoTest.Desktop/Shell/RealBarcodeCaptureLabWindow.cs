using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// Captures actual keyboard-wedge scanner input in memory. This window does not
/// invent a DUT match, a Wave verdict or a production release decision.
/// </summary>
public sealed class RealBarcodeCaptureLabWindow : Window
{
    private readonly ObservableCollection<string> _captures = new();
    private readonly TextBox _input = new()
    {
        MinWidth = 480,
        FontSize = 18,
        Padding = new Thickness(8),
        Margin = new Thickness(10),
        ToolTip = "扫描枪作为键盘输入，配置为发送 Enter 结束符"
    };
    private readonly TextBlock _summary = new()
    {
        Text = "扫码输入就绪；匹配和出厂放行须等待真实波形判定后接线。",
        Margin = new Thickness(12), TextWrapping = TextWrapping.Wrap
    };

    public RealBarcodeCaptureLabWindow()
    {
        Title = "AutoTest · 扫码枪输入检查 · 真实键盘事件";
        WindowState = WindowState.Maximized;
        MinWidth = 840; MinHeight = 500;
        Background = Brushes.White;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 13;

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var top = new StackPanel { Orientation = Orientation.Horizontal };
        top.Children.Add(new TextBlock
        {
            Text = "扫码枪 / 条码输入（回车完成）",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12)
        });
        _input.KeyDown += OnScanKeyDown;
        top.Children.Add(_input);
        var clear = new Button
        {
            Content = "清空本次捕获",
            Margin = new Thickness(8),
            Padding = new Thickness(10, 5)
        };
        clear.Click += (_, _) =>
        {
            _captures.Clear();
            _summary.Text = "捕获已清空。本窗口没有改变任何生产记录。";
            _input.Focus();
        };
        top.Children.Add(clear);
        Grid.SetRow(top, 0);
        grid.Children.Add(top);

        Grid.SetRow(_summary, 1);
        grid.Children.Add(_summary);

        var rows = new ListBox
        {
            ItemsSource = _captures,
            Margin = new Thickness(12)
        };
        Grid.SetRow(rows, 2);
        grid.Children.Add(rows);
        Content = grid;
        Loaded += (_, _) => _input.Focus();
    }

    private void OnScanKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Enter) return;
        args.Handled = true;
        var barcode = _input.Text.Trim();
        if (barcode.Length == 0) return;
        if (barcode.Length > 512)
        {
            _summary.Text = "拒绝：输入长度超过 512 字符。";
            return;
        }
        var alreadySeen = _captures.Any(x => x.EndsWith(" | " + barcode, StringComparison.Ordinal));
        _captures.Insert(0, DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") +
            " | " + barcode);
        if (_captures.Count > 1000) _captures.RemoveAt(_captures.Count - 1);
        _summary.Text = $"收到实际输入 · 本次 { _captures.Count } 次 · 当前 {barcode.Length} 字符" +
            (alreadySeen ? " · 本次已出现相同条码（未作为新 ECU 计数）" : "") +
            "。尚未执行 Wave Verdict→身份匹配，不表示生产 PASS。";
        _input.Clear();
        _input.Focus();
    }
}
