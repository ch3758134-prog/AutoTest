using System.Windows;
using System.Globalization;
using System.IO;
using Microsoft.Win32;
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
        var trace = new Polyline { Stroke = UiKit.Accent, StrokeThickness = 1.6 };
        canvas.Children.Add(trace);
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
            text.Visibility = state.WavePoints.Count >= 2 ? Visibility.Collapsed : Visibility.Visible;
            Canvas.SetLeft(text, Math.Max(12, (w - 190) / 2));
            Canvas.SetTop(text, Math.Max(15, (h - 30) / 2));
            trace.Points.Clear();
            if (state.WavePoints.Count < 2 || w <= 1 || h <= 1) return;
            var timeMin = state.WavePoints.Min(p => p.Time);
            var timeMax = state.WavePoints.Max(p => p.Time);
            var windowSpan = Math.Max((timeMax - timeMin) / zoom.Value, 1e-12);
            var windowStart = (timeMin + timeMax - windowSpan) / 2;
            var windowEnd = windowStart + windowSpan;
            var visible = state.WavePoints.Where(p => p.Time >= windowStart && p.Time <= windowEnd).ToArray();
            if (visible.Length < 2) return;
            var min = visible.Min(p => p.Value);
            var max = visible.Max(p => p.Value);
            var range = Math.Max(max - min, 1e-12);
            foreach (var point in visible)
            {
                var px = (point.Time - windowStart) / windowSpan * w;
                var py = h - ((point.Value - min) / range * (h - 24) + 12);
                trace.Points.Add(new Point(px, py));
            }
        }
        canvas.SizeChanged += (_, _) => Draw();
        a.ValueChanged += (_, _) => Draw();
        b.ValueChanged += (_, _) => Draw();
        zoom.ValueChanged += (_, _) =>
        {
            Draw();
            status("时间轴缩放：" + zoom.Value.ToString("F1") + "x；仅使用已导入 CSV 数据。");
        };
        var foot = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        Grid.SetRow(foot, 2); right.Children.Add(foot);
        var measurement = UiKit.Text("游标 Δ：仅 UI 相对位置；无采样时间单位", 12, false, UiKit.Muted);
        foot.Children.Add(measurement);
        void UpdateCursorDelta()
        {
            if (state.WavePoints.Count < 2)
            {
                measurement.Text = "游标相对间距：" +
                    Math.Abs(a.Value - b.Value).ToString("F1") + "%（无实际时间轴）";
                return;
            }
            var span = state.WavePoints.Max(p => p.Time) - state.WavePoints.Min(p => p.Time);
            measurement.Text = "游标 ΔX：" + (span / zoom.Value *
                Math.Abs(a.Value - b.Value) / 100).ToString("G6", CultureInfo.InvariantCulture) +
                "（单位以导入 CSV 第一列为准）";
        }
        a.ValueChanged += (_, _) => UpdateCursorDelta();
        b.ValueChanged += (_, _) => UpdateCursorDelta();
        zoom.ValueChanged += (_, _) => UpdateCursorDelta();
        var import = UiKit.Button("导入本地 CSV 曲线", () =>
        {
            var dialog = new OpenFileDialog
            {
                Title = "导入两列数值型 CSV（第一列 X，第二列 Y）",
                Filter = "CSV 文件 (*.csv)|*.csv|文本文件 (*.txt)|*.txt"
            };
            if (dialog.ShowDialog() != true) return;
            try
            {
                var rows = new List<UiWavePoint>();
                foreach (var line in File.ReadLines(dialog.FileName).Take(10001))
                {
                    var values = line.Split(new[] { ',', ';', '\t' }, StringSplitOptions.TrimEntries);
                    if (values.Length < 2) continue;
                    if (!double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var time) ||
                        !double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
                        !double.IsFinite(time) || !double.IsFinite(value)) continue;
                    rows.Add(new UiWavePoint { Time = time, Value = value });
                }
                if (rows.Count < 2)
                    throw new InvalidDataException("文件没有至少两条合法的 X,Y 数值行。");
                var ordered = rows.OrderBy(x => x.Time).ToArray();
                state.WavePoints.Clear();
                foreach (var point in ordered) state.WavePoints.Add(point);
                state.WaveSourceFile = dialog.FileName;
                if (state.WaveSignals.Count == 0)
                    state.WaveSignals.Add(new UiWaveSignal { Name = Path.GetFileNameWithoutExtension(dialog.FileName) });
                Draw();
                changed();
                status("已导入 " + ordered.Length + " 个 CSV 点；X 轴单位按源文件定义，不代表真实设备采集。");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                MessageBox.Show("导入失败：" + ex.Message, "CSV 波形", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }, true);
        top.Children.Add(import);
        var measure = UiKit.Button("真实参考判定", () => { }, true);
        UiKit.Disabled(measure, "尚未接入真实采样、参考资源及波形判定服务");
        foot.Children.Add(UiKit.Actions(measure));
    }
}
