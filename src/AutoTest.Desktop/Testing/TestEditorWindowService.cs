using System.Windows;
using AutoTest.Desktop.Projects;

namespace AutoTest.Desktop.Testing;

public sealed class TestEditorWindowService(ProjectWorkspaceService workspace, TestEditorDialogs dialogs)
{
    private readonly Dictionary<string, TestEditorWindow> _windows = new(StringComparer.OrdinalIgnoreCase);

    public void Show(TestEditorSession session)
    {
        var key = session.ManifestPath + "|" + session.AssetId;
        if (_windows.TryGetValue(key, out var existing))
        {
            existing.Activate();
            return;
        }
        var viewModel = new TestEditorViewModel(workspace, dialogs, session);
        var window = new TestEditorWindow(viewModel);
        if (Application.Current.MainWindow is { IsLoaded: true } owner) window.Owner = owner;
        _windows[key] = window;
        window.Closed += (_, _) => _windows.Remove(key);
        window.Show();
        window.Activate();
    }
}
