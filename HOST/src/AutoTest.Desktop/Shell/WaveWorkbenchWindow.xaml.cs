using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using AutoTest.Specialists.Wave;

namespace AutoTest.Desktop.Shell;

/// <summary>Light industrial multi-lane renderer. It only receives real signal
/// previews or verified evidence; all waveform judgement remains in Specialists.</summary>
public partial class WaveWorkbenchWindow : Window
{
    private readonly WaveWorkbenchViewModel _model;
    private readonly Action _openConfiguration;
    private double? _cursorA;
    private double? _cursorB;
    private int _cursorLane;
    private bool _lastLiveMode = true;
    private bool _isRendering;
    private const double LeftMargin = 110;
    private const double RightMargin = 22;
    private const double LaneHeight = 174;
    private const double TopMargin = 22;
    private const double BottomMargin = 38;
    private readonly DispatcherTimer _previewTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(250)
    };

    public WaveWorkbenchWindow(WaveWorkbenchViewModel model, Action openConfiguration)
    {
        _model = model;
        _openConfiguration = openConfiguration;
        DataContext = model;
        InitializeComponent();
        _model.ChartChanged += OnChartChanged;
        _previewTimer.Tick += (_, _) => _model.PollCapturePreview();
        Closed += (_, _) =>
        {
            _previewTimer.Stop();
            _model.ChartChanged -= OnChartChanged;
            _model.Close();
        };
        Loaded += (_, _) =>
        {
            RenderWave();
            _previewTimer.Start();
        };
    }

    private void OpenConfiguration_Click(object sender, RoutedEventArgs e) => _openConfiguration();
    private void ClearCursors_Click(object sender, RoutedEventArgs e)
    {
        _cursorA = null;
        _cursorB = null;
        _model.CursorInfo = "点击同一信号轨道的两个位置测量 Δt、Δy 与区间极值。";
        RenderWave();
    }
    private void OnChartChanged()
    {
        if (_lastLiveMode != _model.IsLiveView)
        {
            _lastLiveMode = _model.IsLiveView;
            _cursorA = null;
            _cursorB = null;
        }
        RenderWave();
    }
    private void WavePlot_SizeChanged(object sender, SizeChangedEventArgs e) => RenderWave();

    private static SolidColorBrush Brush(string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex)!;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static TextBlock Text(string content, double size, string hex, bool bold = false)
    {
        return new TextBlock
        {
            Text = content,
            FontSize = size,
            Foreground = Brush(hex),
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            IsHitTestVisible = false,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
    }
    private void AtText(string content, double x, double y, double size,
        string hex, bool bold = false, double? width = null)
    {
        var label = Text(content, size, hex, bold);
        if (width.HasValue) label.Width = Math.Max(10, width.Value);
        Canvas.SetLeft(label, x);
        Canvas.SetTop(label, y);
        WavePlot.Children.Add(label);
    }
    private void Line(double x1, double y1, double x2, double y2,
        string hex, double stroke = 1, bool dashed = false)
    {
        WavePlot.Children.Add(new Line
        {
            X1 = x1, X2 = x2, Y1 = y1, Y2 = y2,
            Stroke = Brush(hex), StrokeThickness = stroke,
            StrokeDashArray = dashed ? new DoubleCollection { 4, 4 } : null,
            IsHitTestVisible = false
        });
    }

    private void RenderWave()
    {
        if (_isRendering || WavePlot is null) return;
        _isRendering = true;
        try
        {
            var lanes = _model.GetPlotLanes();
            var count = Math.Max(1, lanes.Count);
            var desiredHeight = Math.Max(470, TopMargin + count * LaneHeight + BottomMargin);
            if (Math.Abs(WavePlot.Height - desiredHeight) > 0.5)
                WavePlot.Height = desiredHeight;
            WavePlot.Children.Clear();
            var width = WavePlot.ActualWidth;
            if (width < LeftMargin + RightMargin + 80) return;
            var chartWidth = width - LeftMargin - RightMargin;
            var fullHeight = TopMargin + count * LaneHeight;
            var bounds = _model.VisibleTimeBounds(lanes);
            if (bounds is not { } range)
            {
                AtText("请先在左侧添加 DBC 信号并开始观察；或打开历史真实证据。",
                    26, 50, 15, "#60758B");
                return;
            }
            double X(double time) => LeftMargin + (time - range.Min) /
                (range.Max - range.Min) * chartWidth;

            // All lanes share the same observation time axis.
            for (var tick = 0; tick <= 6; tick++)
            {
                var time = range.Min + (range.Max - range.Min) * tick / 6d;
                var x = X(time);
                Line(x, TopMargin, x, fullHeight, "#DFE7EF", 1, true);
                AtText(time.ToString("F2") + " s", x - 16, fullHeight + 6, 11, "#52677D");
            }
            AtText("相对时间（秒）", LeftMargin, 3, 11, "#60758B");

            for (var i = 0; i < lanes.Count; i++)
            {
                var lane = lanes[i];
                var sectionTop = TopMargin + i * LaneHeight;
                var upper = sectionTop + 24;
                var lower = sectionTop + LaneHeight - 28;
                var trackHeight = lower - upper;
                var visibleActual = lane.Actual.Where(p =>
                    p.Seconds >= range.Min && p.Seconds <= range.Max).ToArray();
                var visibleReference = (lane.Reference ?? Array.Empty<WaveChartPoint>()).Where(p =>
                    p.Seconds >= range.Min && p.Seconds <= range.Max).ToArray();
                var allValues = visibleActual.Select(p => p.Value).Concat(
                    visibleReference.Select(p => p.Value)).ToArray();
                var minValue = allValues.Length == 0 ? 0 : allValues.Min();
                var maxValue = allValues.Length == 0 ? 1 : allValues.Max();
                if (lane.Tolerance is { } tolerance && visibleReference.Length > 0)
                {
                    minValue -= tolerance;
                    maxValue += tolerance;
                }
                if (maxValue - minValue < 1e-10)
                {
                    minValue -= 0.5;
                    maxValue += 0.5;
                }
                var pad = (maxValue - minValue) * 0.10;
                minValue -= pad;
                maxValue += pad;
                double Y(double val) => upper + (maxValue - val) /
                    (maxValue - minValue) * trackHeight;

                WavePlot.Children.Add(new Rectangle
                {
                    Width = width - 2, Height = LaneHeight - 2,
                    Stroke = Brush("#DFE6EE"),
                    Fill = Brush(i % 2 == 0 ? "#FBFCFE" : "#F7FAFD"),
                    StrokeThickness = 1,
                    IsHitTestVisible = false
                });
                var panel = WavePlot.Children[WavePlot.Children.Count - 1];
                Canvas.SetLeft(panel, 1);
                Canvas.SetTop(panel, sectionTop);
                for (var grid = 0; grid <= 6; grid++)
                {
                    var time = range.Min + (range.Max - range.Min) * grid / 6d;
                    var gridX = X(time);
                    Line(gridX, sectionTop + 3, gridX,
                        sectionTop + LaneHeight - 13, "#E4EBF2", 1, true);
                }
                AtText(lane.Name, 9, sectionTop + 13, 14, "#213F5B", true, 95);
                AtText(lane.Unit, 9, sectionTop + 34, 12, "#52677D", false, 95);
                AtText(lane.Detail, 9, sectionTop + LaneHeight - 35, 10,
                    "#60758B", false, 96);
                Line(LeftMargin, lower, width - RightMargin, lower, "#B4C7D8");
                foreach (var fraction in new[] { 0d, 0.5d, 1d })
                {
                    var value = minValue + (maxValue - minValue) * fraction;
                    var y = Y(value);
                    Line(LeftMargin, y, width - RightMargin, y, "#E4EBF2");
                    AtText(value.ToString("G4"), 56, y - 7, 11, "#52677D", false, 50);
                }

                // Reference and its tolerance envelope share exactly this lane's unit.
                if (visibleReference.Length > 0)
                {
                    if (lane.Tolerance is { } band && band >= 0 && visibleReference.Length >= 2)
                    {
                        var points = new PointCollection();
                        foreach (var point in visibleReference)
                            points.Add(new Point(X(point.Seconds), Y(point.Value + band)));
                        foreach (var point in visibleReference.Reverse())
                            points.Add(new Point(X(point.Seconds), Y(point.Value - band)));
                        WavePlot.Children.Add(new Polygon
                        {
                            Points = points,
                            Fill = new SolidColorBrush(Color.FromArgb(28, 74, 128, 170)),
                            StrokeThickness = 0,
                            IsHitTestVisible = false
                        });
                    }
                    DrawTrace(visibleReference, X, Y, "#718AA8", 1.7, true, lane.StepStyle);
                    AtText("参考", width - 73, sectionTop + 8, 11, "#60758B");
                }
                if (visibleActual.Length > 0)
                    DrawTrace(visibleActual, X, Y, lane.Color, 2.3, false, lane.StepStyle);
                else
                    AtText("无真实采样（当前信号尚未更新）",
                        LeftMargin + 15, sectionTop + 71, 13, "#60758B");
                Line(LeftMargin, sectionTop + LaneHeight - 1,
                    width - RightMargin, sectionTop + LaneHeight - 1, "#CBD6E3");
            }

            foreach (var mark in new[] { _cursorA, _cursorB })
            {
                if (!mark.HasValue || mark.Value < range.Min || mark.Value > range.Max) continue;
                var x = X(mark.Value);
                Line(x, TopMargin, x, fullHeight, "#C35B32", 1.4, true);
            }
        }
        catch (Exception error)
        {
            WavePlot.Children.Clear();
            AtText("绘图无法显示真实采样：" + error.Message, 18, 20, 13, "#A12A26");
        }
        finally { _isRendering = false; }
    }

    private void DrawTrace(IReadOnlyList<WaveChartPoint> points,
        Func<double, double> x, Func<double, double> y,
        string color, double thickness, bool dashed, bool stepped)
    {
        if (points.Count == 0) return;
        IReadOnlyList<WaveChartPoint> plot = points;
        if (points.Count > 2400)
            plot = WaveChartAnalysis.InWindow(points, points[0].Seconds,
                points[^1].Seconds, 2200);
        // A long gap must not be drawn as a continuous measured signal.
        // Compare/TEST always use unmodified evidence, not this visual projection.
        var intervals = plot.Zip(plot.Skip(1), (a, b) => b.Seconds - a.Seconds)
            .Where(d => d > 0 && double.IsFinite(d))
            .OrderBy(d => d).ToArray();
        var median = intervals.Length == 0 ? 0 :
            intervals[intervals.Length / 2];
        var gapLimit = Math.Max(0.2, median * 8d);
        Polyline NewSegment() => new()
        {
            Stroke = Brush(color),
            StrokeThickness = thickness,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeDashArray = dashed ? new DoubleCollection { 6, 4 } : null,
            IsHitTestVisible = false
        };
        var previous = plot[0];
        var segment = NewSegment();
        segment.Points.Add(new Point(x(previous.Seconds), y(previous.Value)));
        foreach (var current in plot.Skip(1))
        {
            if (current.Seconds - previous.Seconds > gapLimit)
            {
                if (segment.Points.Count > 1) WavePlot.Children.Add(segment);
                segment = NewSegment();
                segment.Points.Add(new Point(x(current.Seconds), y(current.Value)));
                previous = current;
                continue;
            }
            if (stepped)
                segment.Points.Add(new Point(x(current.Seconds), y(previous.Value)));
            segment.Points.Add(new Point(x(current.Seconds), y(current.Value)));
            previous = current;
        }
        if (segment.Points.Count > 1) WavePlot.Children.Add(segment);
        else if (plot.Count == 1)
        {
            var dot = new Ellipse
            {
                Width = 5, Height = 5, Fill = Brush(color),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(dot, x(plot[0].Seconds) - 2.5);
            Canvas.SetTop(dot, y(plot[0].Value) - 2.5);
            WavePlot.Children.Add(dot);
        }
    }

    private void WavePlot_Click(object sender, MouseButtonEventArgs e)
    {
        var lanes = _model.GetPlotLanes();
        var bounds = _model.VisibleTimeBounds(lanes);
        if (bounds is not { } b || lanes.Count == 0 ||
            WavePlot.ActualWidth < LeftMargin + RightMargin + 50) return;
        var point = e.GetPosition(WavePlot);
        var index = (int)Math.Floor((point.Y - TopMargin) / LaneHeight);
        if (index < 0 || index >= lanes.Count || lanes[index].Actual.Count == 0) return;
        var ratio = Math.Clamp((point.X - LeftMargin) /
            (WavePlot.ActualWidth - LeftMargin - RightMargin), 0, 1);
        var seconds = b.Min + ratio * (b.Max - b.Min);
        if (_cursorA is null || _cursorB is not null || _cursorLane != index)
        {
            _cursorA = seconds;
            _cursorB = null;
            _cursorLane = index;
            _model.CursorInfo = lanes[index].Name + "：游标 A 已设置；再次点击该轨道以测量区间。";
        }
        else
        {
            _cursorB = seconds;
            try
            {
                var measure = WaveChartAnalysis.Measure(lanes[index].Actual,
                    _cursorA.Value, _cursorB.Value);
                _model.CursorInfo = measure is null ? "选定范围内没有真实样本。" :
                    lanes[index].Name + " · Δt " + measure.DeltaSeconds.ToString("F4") +
                    " s · Δy " + measure.DeltaValue.ToString("G6") + " " +
                    lanes[index].Unit + " · 最小 " + measure.Minimum.ToString("G6") +
                    " · 最大 " + measure.Maximum.ToString("G6");
            }
            catch (Exception ex)
            {
                _model.CursorInfo = "游标测量失败：" + ex.Message;
            }
        }
        RenderWave();
    }
}
