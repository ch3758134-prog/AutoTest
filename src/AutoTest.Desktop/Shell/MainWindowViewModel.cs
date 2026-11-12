using AutoTest.Desktop.Projects;
using Prism.Commands;
using Prism.Mvvm;

namespace AutoTest.Desktop.Shell;

/// <summary>Presentation state only. ProjectWorkspaceService owns the real project.</summary>
public sealed class MainWindowViewModel : BindableBase
{
    private readonly ProjectWorkspaceService _workspace;
    private readonly NewProjectDialogService _dialog;
    private string _statusMessage = "就绪 · 尚未打开工程";
    private bool _isBusy;

    public MainWindowViewModel(ProjectWorkspaceService workspace, NewProjectDialogService dialog)
    {
        _workspace = workspace;
        _dialog = dialog;
        NewProjectCommand = new DelegateCommand(OpenNewProject, () => !IsBusy);
        _workspace.CurrentChanged += (_, _) =>
        {
            RaisePropertyChanged(nameof(HasProject));
            RaisePropertyChanged(nameof(CurrentProjectName));
            RaisePropertyChanged(nameof(CurrentProjectPath));
            StatusMessage = _workspace.Current is { } current
                ? "已创建工程：" + current.ManifestPath
                : "就绪 · 尚未打开工程";
        };
    }

    public bool HasProject => _workspace.Current is not null;
    public string CurrentProjectName => _workspace.Current?.Definition.Name ?? "未打开工程";
    public string CurrentProjectPath => _workspace.Current?.ManifestPath ?? "";
    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value)) NewProjectCommand.RaiseCanExecuteChanged();
        }
    }

    public DelegateCommand NewProjectCommand { get; }

    private void OpenNewProject()
    {
        if (IsBusy) return;
        IsBusy = true;
        try { _dialog.ShowDialog(); }
        catch (Exception error) { StatusMessage = "无法打开新建工程窗口：" + error.Message; }
        finally { IsBusy = false; }
    }
}
