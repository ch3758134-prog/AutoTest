using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// One-shot first-show fit for independent WPF editor windows.
/// The actual monitor work area comes from the window HWND in device pixels,
/// then is converted to its current WPF DPI coordinate system.
/// Never infer secondary-monitor bounds from SystemParameters.WorkArea, which
/// describes the PRIMARY screen even when the engineering workbench is elsewhere.
/// </summary>
internal static class DesktopWindowSizing
{
    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    public static void FitToVisibleWorkspace(Window dialog)
    {
        if (dialog.WindowState != WindowState.Normal || !dialog.IsLoaded) return;

        var owner = dialog.Owner is { IsLoaded: true } loaded ? loaded : null;
        var target = owner ?? dialog;
        var hwnd = new WindowInteropHelper(target).Handle;
        var source = PresentationSource.FromVisual(target);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        var monitor = hwnd != nint.Zero
            ? MonitorFromWindow(hwnd, MonitorDefaultToNearest)
            : nint.Zero;

        Rect work;
        if (monitor != nint.Zero && GetMonitorInfo(monitor, ref info))
        {
            work = info.Work;
        }
        else
        {
            // Fallback for an HWND that has not yet attached to a monitor.
            // SystemParameters.WorkArea is already in primary monitor DIPs.
            FitWithinRectangle(dialog, owner, SystemParameters.WorkArea);
            return;
        }

        var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var first = transform.Transform(new Point(work.Left, work.Top));
        var last = transform.Transform(new Point(work.Right, work.Bottom));
        FitWithinRectangle(dialog, owner, new RectD(first.X, first.Y,
            last.X - first.X, last.Y - first.Y).ToWindowsRect());
    }

    private readonly record struct RectD(double X, double Y, double Width, double Height)
    {
        public System.Windows.Rect ToWindowsRect() => new(X, Y, Width, Height);
    }

    private static void FitWithinRectangle(Window dialog, Window? owner,
        System.Windows.Rect area)
    {
        const double margin = 12d;
        var widthMax = area.Width - 2 * margin;
        var heightMax = area.Height - 2 * margin;
        if (!double.IsFinite(widthMax) || !double.IsFinite(heightMax) ||
            widthMax < 500 || heightMax < 330) return;

        dialog.MinWidth = Math.Min(dialog.MinWidth, widthMax);
        dialog.MinHeight = Math.Min(dialog.MinHeight, heightMax);
        var preferredWidth = double.IsFinite(dialog.Width)
            ? dialog.Width : Math.Max(dialog.ActualWidth, 500);
        var preferredHeight = double.IsFinite(dialog.Height)
            ? dialog.Height : Math.Max(dialog.ActualHeight, 330);
        dialog.Width = Math.Min(preferredWidth, widthMax);
        dialog.Height = Math.Min(preferredHeight, heightMax);

        var desiredLeft = owner is not null
            ? owner.Left + (owner.ActualWidth - dialog.Width) / 2
            : area.Left + (area.Width - dialog.Width) / 2;
        var desiredTop = owner is not null
            ? owner.Top + (owner.ActualHeight - dialog.Height) / 2
            : area.Top + (area.Height - dialog.Height) / 2;
        if (!double.IsFinite(desiredLeft))
            desiredLeft = area.Left + (area.Width - dialog.Width) / 2;
        if (!double.IsFinite(desiredTop))
            desiredTop = area.Top + (area.Height - dialog.Height) / 2;
        dialog.Left = Math.Clamp(desiredLeft, area.Left + margin,
            area.Right - dialog.Width - margin);
        dialog.Top = Math.Clamp(desiredTop, area.Top + margin,
            area.Bottom - dialog.Height - margin);
    }
}
