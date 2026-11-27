using System.Windows;
using AutoTest.Projects.Dbc;

namespace AutoTest.Desktop.Dbc;

/// <summary>Native modal field editors; their result goes to DbcWorkbenchDocument for validation.</summary>
public sealed class DbcEditorDialogs
{
    private static Window? ActiveOwner =>
        global::System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
        ?? global::System.Windows.Application.Current.MainWindow;

    public DbcMessage? EditMessage(DbcMessage existing, bool allowIdChange = false)
    {
        var dialog = new DbcMessageEditDialog(existing, allowIdChange);
        if (ActiveOwner is { IsLoaded: true } owner) dialog.Owner = owner;
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    public DbcSignal? EditSignal(DbcSignal existing, bool protectName = false)
    {
        var dialog = new DbcSignalEditDialog(existing, protectName);
        if (ActiveOwner is { IsLoaded: true } owner) dialog.Owner = owner;
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    public string? AskDocumentName(string current)
    {
        var dialog = new AutoTest.Desktop.Projects.ResourceNameDialog("重命名 DBC", current);
        if (ActiveOwner is { IsLoaded: true } owner) dialog.Owner = owner;
        return dialog.ShowDialog() == true ? dialog.ResourceName : null;
    }

    public bool ConfirmDiscard(string message)
    {
        var owner = ActiveOwner;
        var answer = owner is null
            ? MessageBox.Show(message, "未保存修改", MessageBoxButton.YesNo, MessageBoxImage.Warning)
            : MessageBox.Show(owner, message, "未保存修改", MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
        return answer == MessageBoxResult.Yes;
    }

    public void ShowError(string message)
    {
        var owner = ActiveOwner;
        if (owner is null)
            MessageBox.Show(message, "DBC 操作未完成", MessageBoxButton.OK, MessageBoxImage.Warning);
        else
            MessageBox.Show(owner, message, "DBC 操作未完成",
                MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
