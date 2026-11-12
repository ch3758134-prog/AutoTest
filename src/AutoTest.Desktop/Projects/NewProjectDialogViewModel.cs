using System.IO;
using Prism.Commands;
using Prism.Mvvm;

namespace AutoTest.Desktop.Projects;

/// <summary>Dialog state and validation. Disk writes are delegated to ProjectWorkspaceService.</summary>
public sealed class NewProjectDialogViewModel : BindableBase
{
    private readonly ProjectWorkspaceService _workspace;
    private readonly Func<string?> _chooseDirectory;
    private string _projectName = "新建测试工程";
    private string _parentDirectory =
        AppContext.BaseDirectory;
    private string _validationMessage = "";
    private bool _isCreating;

    public NewProjectDialogViewModel(ProjectWorkspaceService workspace, Func<string?> chooseDirectory)
    {
        _workspace = workspace;
        _chooseDirectory = chooseDirectory;
        CreateCommand = new DelegateCommand(async () => await CreateAsync(),
            () => !IsCreating && string.IsNullOrEmpty(ValidationMessage));
        BrowseCommand = new DelegateCommand(ChooseDirectory, () => !IsCreating);
        CancelCommand = new DelegateCommand(() => CloseRequested?.Invoke(false), () => !IsCreating);
        Revalidate();
    }

    public event Action<bool>? CloseRequested;

    public string ProjectName
    {
        get => _projectName;
        set
        {
            if (SetProperty(ref _projectName, value))
            {
                RaisePropertyChanged(nameof(TargetPath));
                Revalidate();
            }
        }
    }

    public string ParentDirectory
    {
        get => _parentDirectory;
        set
        {
            if (SetProperty(ref _parentDirectory, value))
            {
                RaisePropertyChanged(nameof(TargetPath));
                Revalidate();
            }
        }
    }

    public string TargetPath => ProjectWorkspaceService.PreviewManifestPath(ProjectName, ParentDirectory);

    public string ValidationMessage
    {
        get => _validationMessage;
        private set => SetProperty(ref _validationMessage, value);
    }

    public bool IsCreating
    {
        get => _isCreating;
        private set
        {
            if (SetProperty(ref _isCreating, value))
            {
                CreateCommand.RaiseCanExecuteChanged();
                BrowseCommand.RaiseCanExecuteChanged();
                CancelCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public DelegateCommand CreateCommand { get; }
    public DelegateCommand BrowseCommand { get; }
    public DelegateCommand CancelCommand { get; }

    private void ChooseDirectory()
    {
        var selected = _chooseDirectory();
        if (!string.IsNullOrWhiteSpace(selected)) ParentDirectory = selected;
    }

    private void Revalidate()
    {
        ValidationMessage = ProjectWorkspaceService.ValidateInput(ProjectName, ParentDirectory);
        CreateCommand.RaiseCanExecuteChanged();
    }

    private async Task CreateAsync()
    {
        if (IsCreating || ValidationMessage.Length > 0) return;
        IsCreating = true;
        try
        {
            await _workspace.CreateAsync(ProjectName, ParentDirectory);
            // Allow WPF Window.Closing to complete before setting DialogResult.
            IsCreating = false;
            CloseRequested?.Invoke(true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or
                                     ArgumentException or NotSupportedException or
                                     AutoTest.Projects.Project.ProjectDataException)
        {
            ValidationMessage = e.Message;
        }
        finally
        {
            IsCreating = false;
        }
    }
}
