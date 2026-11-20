using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using AutoTest.UI.Design;
using AutoTest.UI.Model;

namespace AutoTest.UI.Workspaces;

/// <summary>Graph-centric waveform work area, no generated signal samples.</summary>
public sealed class WaveWorkspace : Grid
{
    public WaveWorkspace(UiSession state, Action changed, Action<string> status)
    {
        Margin = new Thickness(16);
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition());
        Children.Add(UiKit.Header("波形分析", "信号、参考、时间轴、双游标与判定工作空间"));
        var area = UiKit.Columns(288, 0);
        Grid.SetRow(area, 1); Children.Add(area);
        var left = new DockPanel { Margin = new Thickness(0, 0, 10, 0) };
        area.Children.Add(UiKit.Card(left, new Thickness(13)));
        var top = new StackPanel();
        DockPanel.SetDock(top, Dock.Top); left.Children.Add(top);
        top.Children.Add(UiKit.Text("信号与参考", 17, true));
        top.Children.Add(UiKit.Note("可以添加信号、编辑单位与参考名称。真实曲线必须由采集或导入的真实样本提供。"));
        var signals = UiKit.Table(state.WaveSignals,
            ("名称", "Name", 120), ("单位", "Unit", 65), ("参考", "Reference", 0));
        signals.CellEditEnding += (_, _) => changed();
        top.Children.Add(UiKit.Actions(
            UiKit.Button("＋ 添加信号", () =>
            {
                state.WaveSignals.Add(new UiWaveSignal { Name = "新信号" }); changed();
            }, true),
            UiKit.Button("移除", () =>
            {
                if (signals.SelectedItem is UiWaveSignal s) { state.WaveSignals.Remove(s); changed(); }
            })));
        left.Children.Add(signals);
        var right = new Grid();
        Grid.SetColumn(right, 1); area.Children.Add(UiKit.Card(right));
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition());
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var head = new StackPanel();
        right.Children.Add(head);
        head.Children.Add(UiKit.Text("波形画布", 18, true));
        head.Children.Add(UiKit.Note("不生成正弦曲线代替测量数据。可移动游标、调整视窗和编辑观察布局。"));
        var controlRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        head.Children.Add(controlRow);
        controlRow.Children.Add(UiKit.Text("缩放", 12, false, UiKit.Muted));
        var zoom = new Slider { Minimum = 1, Maximum = 10, Value = 1, Width = 150,
            Margin = new Thickness(10, 0, 22, 0), AutoToolTipPlacement = System.Windows.Controls.Primitives.AutoToolTipPlacement.TopLeft };
        controlRow.Children.Add(zoom);
        controlRow.Children.Add(UiKit.Text("游标 A", 12, false, UiKit.Muted));
        var a = new Slider { Minimum = 0, Maximum = 100, Value = 25, Width = 135, Margin = new Thickness(8, 0, 18, 0) };
        controlRow.Children.Add(a);
        controlRow.Children.Add(UiKit.Text("游标 B", 12, false, UiKit.Muted));
        var b = new Slider { Minimum = 0, Maximum = 100, Value = 75, Width = 135, Margin = new Thickness(8, 0, 0, 0) };
        controlRow.Children.Add(b);
        var display = new Border
        {
            Background = UiKit.Brush("#FBFDFF"), BorderBrush = UiKit.Outline,
            BorderThickness = new Thickness(1), MinHeight = 250
        };
        Grid.SetRow(display, 1);
        right.Children.Add(display);
        var canvas = new Canvas { ClipToBounds = true };
        display.Child = canvas;
        var text = UiKit.Text("暂无实际采样数据", 17, true, UiKit.Muted);
        canvas.Children.Add(text);
        var cursorA = new Line { Stroke = UiKit.Brush("#D48A37"), StrokeThickness = 2, StrokeDashArray = [4, 3] };
        var cursorB = new Line { Stroke = UiKit.Accent, StrokeThickness = 2, StrokeDashArray = [4, 3] };
        canvas.Children.Add(cursorA); canvas.Children.Add(cursorB);
        void Draw()
        {
            var w = canvas.ActualWidth; var h = canvas.ActualHeight;
            cursorA.X1 = cursorA.X2 = w * a.Value / 100;
            cursorB.X1 = cursorB.X2 = w * b.Value / 100;
            cursorA.Y1 = cursorB.Y1 = 0;
            cursorA.Y2 = cursorB.Y2 = h;
            Canvas.SetLeft(text, Math.Max(12, (w - 190) / 2));
            Canvas.SetTop(text, Math.Max(15, (h - 30) / 2));
        }
        canvas.SizeChanged += (_, _) => Draw();
        a.ValueChanged += (_, _) => Draw();
        b.ValueChanged += (_, _) => Draw();
        zoom.ValueChanged += (_, _) => status("UI 时间轴缩放比例：" + zoom.Value.ToString("F1") + "x（无真实波形）");
        var foot = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        Grid.SetRow(foot, 2); right.Children.Add(foot);
        var measurement = UiKit.Text("游标 Δ：仅 UI 相对位置；无采样时间单位", 12, false, UiKit.Muted);
        foot.Children.Add(measurement);
        a.ValueChanged += (_, _) => measurement.Text =
            "游标相对间距：" + Math.Abs(a.Value - b.Value).ToString("F1") + "%（无实际时间轴）";
        b.ValueChanged += (_, _) => measurement.Text =
            "游标相对间距：" + Math.Abs(a.Value - b.Value).ToString("F1") + "%（无实际时间轴）";
        var measure = UiKit.Button("真实参考判定", () => { }, true);
        UiKit.Disabled(measure, "尚未接入真实采样、参考资源及波形判定服务");
        foot.Children.Add(UiKit.Actions(measure));
    }
}
