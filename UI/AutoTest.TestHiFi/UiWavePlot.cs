using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace AutoTest.TestHiFi;

/// <summary>UI-only plot of measured CSV values read from disk; never fabricates samples.</summary>
public sealed class UiWavePlot : FrameworkElement
{
    private readonly List<(double Time, double Value)> _samples = [];
    private double _zoom = 1;
    private double? _cursorA;
    private double? _cursorB;
    private bool _moveSecond;
    public event Action? CursorChanged;
    public int Count => _samples.Count;
    public string CursorInfo => (_cursorA is null ? "游标 A：未设置" : "A：" + _cursorA.Value.ToString("0.###", CultureInfo.InvariantCulture) + " ms") +
        "   " + (_cursorB is null ? "游标 B：未设置" : "B：" + _cursorB.Value.ToString("0.###", CultureInfo.InvariantCulture) + " ms") +
        (_cursorA is not null && _cursorB is not null
            ? "   Δt：" + Math.Abs(_cursorA.Value - _cursorB.Value).ToString("0.###", CultureInfo.InvariantCulture) + " ms"
            : "");
    public UiWavePlot()
    {
        MinHeight = 260;
        Focusable = true;
        MouseLeftButtonDown += (_, e) =>
        {
            if (_samples.Count < 2) return;
            var (start, end) = ViewRange();
            var width = Math.Max(1, ActualWidth - 48);
            var fraction = Math.Clamp((e.GetPosition(this).X - 36) / width, 0, 1);
            var x = start + fraction * (end - start);
            var nearest = _samples.MinBy(p => Math.Abs(p.Time - x));
            if (_moveSecond) _cursorB = nearest.Time;
            else _cursorA = nearest.Time;
            _moveSecond = !_moveSecond;
            CursorChanged?.Invoke();
            InvalidateVisual();
        };
    }
    public void LoadCsv(string path)
    {
        var parsed = new List<(double Time, double Value)>();
        foreach (var line in File.ReadLines(path))
        {
            var fields = line.Split(',', ';', '\t');
            if (fields.Length < 2) continue;
            if (!double.TryParse(fields[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var t) ||
                !double.TryParse(fields[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ||
                !double.IsFinite(t) || !double.IsFinite(v)) continue;
            parsed.Add((t, v));
            if (parsed.Count > 250000)
                throw new InvalidDataException("波形文件超过 25 万样本，当前轻量预览器拒绝加载以避免界面卡顿。");
        }
        if (parsed.Count < 2) throw new InvalidDataException("没有可解析的两列数值数据（time_ms,value）。");
        parsed.Sort((a, b) => a.Time.CompareTo(b.Time));
        if (parsed[0].Time == parsed[^1].Time)
            throw new InvalidDataException("时间列不存在有效跨度。");
        _samples.Clear(); _samples.AddRange(parsed);
        _cursorA = null; _cursorB = null; _zoom = 1; _moveSecond = false;
        CursorChanged?.Invoke();
        InvalidateVisual();
    }
    public void ZoomIn() { _zoom = Math.Min(64, _zoom * 2); InvalidateVisual(); }
    public void ZoomOut() { _zoom = Math.Max(1, _zoom / 2); InvalidateVisual(); }
    public void ResetZoom() { _zoom = 1; InvalidateVisual(); }
    private (double Start, double End) ViewRange()
    {
        if (_samples.Count == 0) return (0, 1);
        var first = _samples[0].Time; var last = _samples[^1].Time;
        var center = _cursorA is null ? (first + last) / 2 : _cursorA.Value;
        var half = (last - first) / (2 * _zoom);
        center = Math.Clamp(center, first + half, last - half);
        return (center - half, center + half);
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var w = Math.Max(120, ActualWidth), h = Math.Max(140, ActualHeight);
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(249, 251, 254)), null, new Rect(0, 0, w, h));
        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(224, 232, 242)), 1);
        var left = 36d; var right = w - 12; var top = 14d; var bottom = h - 22;
        for (int i = 0; i <= 5; i++)
        {
            var x = left + (right - left) * i / 5;
            var y = top + (bottom - top) * i / 5;
            dc.DrawLine(gridPen, new Point(x, top), new Point(x, bottom));
            dc.DrawLine(gridPen, new Point(left, y), new Point(right, y));
        }
        if (_samples.Count < 2) return;
        var (start, end) = ViewRange();
        var visible = _samples.Where(p => p.Time >= start && p.Time <= end).ToArray();
        if (visible.Length < 2) return;
        var min = visible.Min(p => p.Value); var max = visible.Max(p => p.Value);
        if (max <= min) { min -= 0.5; max += 0.5; }
        var path = new StreamGeometry();
        using (var context = path.Open())
        {
            var limit = Math.Max(1, visible.Length / 3000);
            bool firstPoint = true;
            for (int i = 0; i < visible.Length; i += limit)
            {
                var p = visible[i];
                var point = new Point(left + (p.Time - start) / (end - start) * (right - left),
                    bottom - (p.Value - min) / (max - min) * (bottom - top));
                if (firstPoint) { context.BeginFigure(point, false, false); firstPoint = false; }
                else context.LineTo(point, true, false);
            }
        }
        path.Freeze();
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromRgb(36, 111, 195)), 1.8), path);
        void Cursor(double? t, Color color)
        {
            if (t is null || t < start || t > end) return;
            var x = left + (t.Value - start) / (end - start) * (right - left);
            dc.DrawLine(new Pen(new SolidColorBrush(color), 1.6), new Point(x, top), new Point(x, bottom));
        }
        Cursor(_cursorA, Color.FromRgb(211, 109, 31));
        Cursor(_cursorB, Color.FromRgb(31, 137, 117));
    }
}
