using System.Windows;

namespace AutoTest.Desktop.Testing;

/// <summary>Only destructive/discard confirmations and errors; all step parameters live in Inspector.</summary>
public sealed class TestEditorDialogs
{
    private static Window? ActiveOwner =>
        Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
        ?? Application.Current.MainWindow;

    public bool Confirm(string message)
    {
        var owner = ActiveOwner;
        return (owner is null
            ? MessageBox.Show(message, "请确认", MessageBoxButton.YesNo, MessageBoxImage.Question)
            : MessageBox.Show(owner, message, "请确认",
                MessageBoxButton.YesNo, MessageBoxImage.Question)) == MessageBoxResult.Yes;
    }

    public void ShowError(string message)
    {
        var owner = ActiveOwner;
        if (owner is null) MessageBox.Show(message, "编辑操作未完成",
            MessageBoxButton.OK, MessageBoxImage.Warning);
        else MessageBox.Show(owner, message, "编辑操作未完成",
            MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
