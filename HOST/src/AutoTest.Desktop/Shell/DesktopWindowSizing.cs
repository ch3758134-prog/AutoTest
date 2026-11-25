using System.Windows;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// One-shot size correction for independent desktop tool windows and dialogs.
/// Screen size in pixels is not the same as WPF device-independent pixels.
/// Do not alter the user's subsequent manual resizing or persist geometry.
/// </summary>
internal static class DesktopWindowSizing
{
    public static void FitToVisibleWorkspace(Window dialog)
    {
        if (dialog.WindowState != WindowState.Normal) return;
        var owner = dialog.Owner is { IsLoaded: true } loaded ? loaded : null;
        var work = SystemParameters.WorkArea;
        // The owning WPF window knows the actual DPI/monitor of the station.
        // Standalone windows without an owner use the primary work area.
        var availableWidth = owner is null ? work.Width - 20 : owner.ActualWidth - 20;
        var availableHeight = owner is null ? work.Height - 20 : owner.ActualHeight - 20;
        if (!double.IsFinite(availableWidth) || availableWidth < 530 ||
            !double.IsFinite(availableHeight) || availableHeight < 350)
            return;
        dialog.MinWidth = Math.Min(dialog.MinWidth, availableWidth);
        dialog.MinHeight = Math.Min(dialog.MinHeight, availableHeight);
        dialog.Width = Math.Min(dialog.Width, availableWidth);
        dialog.Height = Math.Min(dialog.Height, availableHeight);
        // Center AFTER size correction; the ordinary CenterOwner calculation
        // could have placed a taller-than-screen dialog outside the work area.
        if (owner is not null)
        {
            dialog.Left = owner.Left + Math.Max(0, (owner.ActualWidth - dialog.Width) / 2);
            dialog.Top = owner.Top + Math.Max(0, (owner.ActualHeight - dialog.Height) / 2);
        }
        else
        {
            dialog.Left = work.Left + Math.Max(0, (work.Width - dialog.Width) / 2);
            dialog.Top = work.Top + Math.Max(0, (work.Height - dialog.Height) / 2);
        }
    }
}
