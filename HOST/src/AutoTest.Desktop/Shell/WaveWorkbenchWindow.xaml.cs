using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using AutoTest.Specialists.Wave;

namespace AutoTest.Desktop.Shell;

public partial class WaveWorkbenchWindow : Window
{
    private readonly WaveWorkbenchViewModel _model;
    private readonly Action _openConfiguration;
    private double? _cursorA;
    private double? _cursorB;

    public WaveWorkbenchWindow(WaveWorkbenchViewModel model, Action openConfiguration)
    {
        _model = model;
        _openConfiguration = openConfiguration;
        DataContext = model;
        InitializeComponent();
        var work = SystemParameters.WorkArea;
        Width = Math.Min(1300, Math.Max(700, work.Width - 30));
        Height = Math.Min(800, Math.Max(500, work.Height - 30));
        MinWidth = Math.Min(850, Width);
        MinHeight = Math.Min(570, Height);
        _model.ChartChanged += OnChartChanged;
        Closed += (_, _) =>
        {
            _model.ChartChanged -= OnChartChanged;
            _model.Close();
        };
        Loaded += (_, _) => RenderWave();
    }

    private void OnChartChanged()
    {
        _cursorA = null;
        _cursorB = null;
        RenderWave();
    }

    private void OpenConfiguration_Click(object sender, RoutedEventArgs e) =>
        _openConfiguration();

    private void WavePlot_SizeChanged(object sender, SizeChangedEventArgs e) => RenderWave();

    private (double TMin, double TMax, double VMin, double VMax)? Bounds()
    {
        var points = _model.Points;
        if (points.Count == 0) return null;
        double xMin = points[0].Seconds, xMax = points[^1].Seconds;
        double min = points.Min(p => p.Value), max = points.Max(p => p.Value);
        if (!double.IsFinite(min) || !double.IsFinite(max) || !double.IsFinite(xMin) ||
            !double.IsFinite(xMax)) return null;
        if (xMax == xMin) xMax = xMin + 1;
        if (max == min) { min -= 0.5; max += 0.5; }
        double pad = (max - min) * 0.10;
        return (xMin, xMax, min - pad, max + pad);
    }

    private void RenderWave()
    {
        if (WavePlot is null) return;
        WavePlot.Children.Clear();
        double w = WavePlot.ActualWidth, h = WavePlot.ActualHeight;
        if (w < 60 || h < 60 || Bounds() is not { } bounds) return;
        double plotLeft = 48, plotTop = 12, plotWidth = w - 61, plotHeight = h - 37;
        if (plotWidth <= 10 || plotHeight <= 10) return;
        var xSpan = bounds.TMax - bounds.TMin;
        var ySpan = bounds.VMax - bounds.VMin;
        double X(double seconds) => plotLeft +
            (seconds - bounds.TMin) / xSpan * plotWidth;
        double Y(double value) => plotTop +
            (bounds.VMax - value) / ySpan * plotHeight;
        for (var i = 0; i <= 4; i++)
        {
            var v = bounds.VMax - (bounds.VMax - bounds.VMin) * i / 4;
            var y = Y(v);
            WavePlot.Children.Add(new Line { X1 = plotLeft, X2 = plotLeft + plotWidth,
                Y1 = y, Y2 = y, Stroke = new SolidColorBrush(Color.FromRgb(227,235,246)),
                StrokeThickness = 1 });
            var label = new TextBlock { Text = v.ToString("G4"), FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(120,143,166)) };
            Canvas.SetLeft(label, 1); Canvas.SetTop(label, y - 7);
            WavePlot.Children.Add(label);
        }
        try
        {
            var samples = _model.Points;
            var plot = samples.Count <= 2000 ? samples :
                WaveChartAnalysis.InWindow(samples, bounds.TMin, bounds.TMax, 2000);
            var poly = new Polyline {
                Stroke = new SolidColorBrush(Color.FromRgb(39,113,192)),
                StrokeThickness = 1.8, StrokeLineJoin = PenLineJoin.Round
            };
            foreach (var p in plot) poly.Points.Add(new Point(X(p.Seconds), Y(p.Value)));
            WavePlot.Children.Add(poly);
        }
        catch (Exception)
        {
            // The verified source remains unchanged; chart refuses malformed projection.
            return;
        }
        foreach (var marker in new[] { _cursorA, _cursorB })
        {
            if (!marker.HasValue) continue;
            var x = X(marker.Value);
            WavePlot.Children.Add(new Line { X1 = x, X2 = x, Y1 = plotTop,
                Y2 = plotTop + plotHeight,
                Stroke = new SolidColorBrush(Color.FromRgb(200,110,60)),
                StrokeThickness = 1.2, StrokeDashArray = new DoubleCollection { 4, 3 } });
        }
    }

    private void WavePlot_Click(object sender, MouseButtonEventArgs e)
    {
        if (Bounds() is not { } b || WavePlot.ActualWidth < 70) return;
        var x = e.GetPosition(WavePlot).X;
        var fraction = Math.Clamp((x - 48) / (WavePlot.ActualWidth - 61), 0, 1);
        var seconds = b.TMin + fraction * (b.TMax - b.TMin);
        if (_cursorA is null || _cursorB is not null)
        {
            _cursorA = seconds;
            _cursorB = null;
        }
        else
        {
            _cursorB = seconds;
            _model.SetCursorMeasure(_cursorA.Value, seconds);
        }
        RenderWave();
    }
}
