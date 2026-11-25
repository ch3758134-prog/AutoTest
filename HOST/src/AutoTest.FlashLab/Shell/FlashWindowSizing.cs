using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace AutoTest.FlashLab.Shell;

/// <summary>
/// Standalone FlashLab work-area fit, in monitor-specific WPF DIPs.
/// Keeps HEX/Job/monitor controls reachable on the actual active monitor.
/// </summary>
internal static class FlashWindowSizing
{
    private const uint NearestMonitor = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref NativeMonitorInfo info);

    public static void Fit(Window tool)
    {
        if (!tool.IsLoaded || tool.WindowState != WindowState.Normal) return;
        var owner = tool.Owner is { IsLoaded: true } loaded ? loaded : null;
        var target = owner ?? tool;
        var handle = new WindowInteropHelper(target).Handle;
        var monitor = handle != nint.Zero
            ? MonitorFromWindow(handle, NearestMonitor) : nint.Zero;
        var info = new NativeMonitorInfo { Size = Marshal.SizeOf<NativeMonitorInfo>() };
        if (monitor == nint.Zero || !GetMonitorInfo(monitor, ref info))
        {
            FitWithin(tool, owner, SystemParameters.WorkArea);
            return;
        }

        var matrix = PresentationSource.FromVisual(target)?
            .CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var a = matrix.Transform(new Point(info.Work.Left, info.Work.Top));
        var b = matrix.Transform(new Point(info.Work.Right, info.Work.Bottom));
        FitWithin(tool, owner, new System.Windows.Rect(a.X, a.Y,
            b.X - a.X, b.Y - a.Y));
    }

    private static void FitWithin(Window tool, Window? owner, System.Windows.Rect area)
    {
        const double margin = 12d;
        var width = area.Width - margin * 2;
        var height = area.Height - margin * 2;
        if (!double.IsFinite(width) || !double.IsFinite(height) ||
            width < 500 || height < 330) return;
        tool.MinWidth = Math.Min(tool.MinWidth, width);
        tool.MinHeight = Math.Min(tool.MinHeight, height);
        tool.Width = Math.Min(double.IsFinite(tool.Width)
            ? tool.Width : Math.Max(tool.ActualWidth, 500), width);
        tool.Height = Math.Min(double.IsFinite(tool.Height)
            ? tool.Height : Math.Max(tool.ActualHeight, 330), height);

        var x = owner is not null
            ? owner.Left + (owner.ActualWidth - tool.Width) / 2
            : area.Left + (area.Width - tool.Width) / 2;
        var y = owner is not null
            ? owner.Top + (owner.ActualHeight - tool.Height) / 2
            : area.Top + (area.Height - tool.Height) / 2;
        if (!double.IsFinite(x)) x = area.Left + (area.Width - tool.Width) / 2;
        if (!double.IsFinite(y)) y = area.Top + (area.Height - tool.Height) / 2;
        tool.Left = Math.Clamp(x, area.Left + margin, area.Right - tool.Width - margin);
        tool.Top = Math.Clamp(y, area.Top + margin, area.Bottom - tool.Height - margin);
    }
}
