using System.Windows;
using AutoTest.Projects.Dbc;

namespace AutoTest.Desktop.Dbc;

/// <summary>Native modal field editors; their result goes to DbcWorkbenchDocument for validation.</summary>
public sealed class DbcEditorDialogs
{
    private static Window? ActiveOwner =>
        Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
        ?? Application.Current.MainWindow;

    public DbcMessage? EditMessage(DbcMessage existing)
    {
        var dialog = new DbcMessageEditDialog(existing);
        if (ActiveOwner is { IsLoaded: true } owner) dialog.Owner = owner;
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    public DbcSignal? EditSignal(DbcSignal existing)
    {
        var dialog = new DbcSignalEditDialog(existing);
        if (ActiveOwner is { IsLoaded: true } owner) dialog.Owner = owner;
        return dialog.ShowDialog() == true ? dialog.Result : null;
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
