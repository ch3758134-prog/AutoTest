using System.Windows;
using AutoTest.Testing.Authoring;

namespace AutoTest.Desktop.Testing;

public readonly record struct StepParametersResult(
    string Name, IReadOnlyDictionary<string, string> Properties);

public sealed class TestEditorDialogs
{
    private static Window? ActiveOwner =>
        Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
        ?? Application.Current.MainWindow;

    public StepParametersResult? EditParameters(StepNode step)
    {
        var dialog = new StepParametersDialog(step);
        if (ActiveOwner is { IsLoaded: true } owner) dialog.Owner = owner;
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    public bool Confirm(string message)
    {
        var owner = ActiveOwner;
        var result = owner is null
            ? MessageBox.Show(message, "请确认", MessageBoxButton.YesNo, MessageBoxImage.Question)
            : MessageBox.Show(owner, message, "请确认", MessageBoxButton.YesNo,
                MessageBoxImage.Question);
        return result == MessageBoxResult.Yes;
    }

    public void ShowError(string message)
    {
        var owner = ActiveOwner;
        if (owner is null)
            MessageBox.Show(message, "测试程序操作未完成", MessageBoxButton.OK, MessageBoxImage.Warning);
        else
            MessageBox.Show(owner, message, "测试程序操作未完成",
                MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
