using System.Windows;

namespace AutoTest.FlashLab.Shell;

/// <summary>One-shot WPF DIP fit for the standalone FlashLab assembly.</summary>
internal static class FlashWindowSizing
{
    public static void Fit(Window tool)
    {
        if (tool.WindowState != WindowState.Normal) return;
        var owner = tool.Owner is { IsLoaded: true } loaded ? loaded : null;
        var work = SystemParameters.WorkArea;
        var width = (owner is null ? work.Width : owner.ActualWidth) - 20;
        var height = (owner is null ? work.Height : owner.ActualHeight) - 20;
        if (!double.IsFinite(width) || !double.IsFinite(height) ||
            width < 530 || height < 350) return;
        tool.MinWidth = Math.Min(tool.MinWidth, width);
        tool.MinHeight = Math.Min(tool.MinHeight, height);
        tool.Width = Math.Min(tool.Width, width);
        tool.Height = Math.Min(tool.Height, height);
        if (owner is not null)
        {
            tool.Left = owner.Left + Math.Max(0, (owner.ActualWidth - tool.Width) / 2);
            tool.Top = owner.Top + Math.Max(0, (owner.ActualHeight - tool.Height) / 2);
        }
        else
        {
            tool.Left = work.Left + Math.Max(0, (work.Width - tool.Width) / 2);
            tool.Top = work.Top + Math.Max(0, (work.Height - tool.Height) / 2);
        }
    }
}
