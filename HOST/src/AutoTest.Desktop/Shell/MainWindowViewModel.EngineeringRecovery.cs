using System.IO;
using System.Windows;
using AutoTest.Desktop.Projects;
using Prism.Commands;

namespace AutoTest.Desktop.Shell;

public sealed partial class MainWindowViewModel
{
    private bool _testProjectRecoveryAvailable;

    /// <summary>
    /// Invalid project content never turns into a modal crash or an invisible
    /// production gate. The original TEST case files and specialist resources
    /// remain independently usable. Replacing the ONLY damaged invocation
    /// definition is an explicit destructive editor action.
    /// </summary>
    public bool TestProjectRecoveryAvailable
    {
        get => _testProjectRecoveryAvailable;
        private set
        {
            if (SetProperty(ref _testProjectRecoveryAvailable, value))
                RebuildTestProjectCommand?.RaiseCanExecuteChanged();
        }
    }

    public DelegateCommand RebuildTestProjectCommand { get; private set; } = null!;
    public DelegateCommand OpenEngineeringHomeCommand { get; private set; } = null!;

    private void InitializeEngineeringRecovery()
    {
        RebuildTestProjectCommand = new DelegateCommand(
            async () => await RebuildInvalidTestProjectAsync(),
            () => HasProject && TestProjectRecoveryAvailable && !IsBusy);
        OpenEngineeringHomeCommand = new DelegateCommand(ShowOverview,
            () => HasProject && !IsBusy);
        _workspace.CurrentChanged += (_, _) =>
            OpenEngineeringHomeCommand.RaiseCanExecuteChanged();
    }

    private async Task RebuildInvalidTestProjectAsync()
    {
        if (!TestProjectRecoveryAvailable || _workspace.Current is null || IsBusy)
            return;
        if (_realTest.IsRunning)
        {
            StatusMessage = "TEST 正在运行，请结束后再修改工程。";
            return;
        }
        var decision = MessageBox.Show(
            "当前工程的 TEST 执行顺序无法按新格式读取。\n\n" +
            "将新建一份空白有序 TEST 项目；原来独立保存的测试用例、DBC、" +
            "公共流程、波形及 FLASH 文件不会删除。\n\n" +
            "原 TEST 内容会保存在工程的资源修订归档中，但不会作为旧格式继续运行。" +
            "\n\n是否确定重建测试顺序？",
            "重建 TEST 项目", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (decision != MessageBoxResult.Yes) return;
        try
        {
            await _workspace.ResetUnreadableTestProjectAsync();
            TestProjectRecoveryAvailable = false;
            StatusMessage = "已建立当前版本的空白 TEST 项目；从用例库加入需要执行的用例。";
            await OpenFirstAssetAsync(ProjectWorkspaceService.TestProjectKind,
                CreateTestProjectAsync);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or
                                      InvalidOperationException or UnauthorizedAccessException or
                                      AutoTest.Projects.Project.ProjectDataException)
        {
            StatusMessage = "TEST 重建失败，原文件未被主动删除：" + error.Message;
            _fileDialogs.ShowError(StatusMessage);
        }
    }
}
