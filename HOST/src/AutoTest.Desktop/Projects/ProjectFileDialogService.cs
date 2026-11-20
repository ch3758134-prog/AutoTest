using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using System.Windows;

namespace AutoTest.Desktop.Projects;

/// <summary>Native Windows dialogs only; no project persistence here.</summary>
public sealed class ProjectFileDialogService(RecentProjectStore recent)
{
    private string BrowseDirectory(string? preferred = null) =>
        Directory.Exists(preferred) ? preferred! : recent.GetLastBrowseDirectory() ??
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    private void RememberSelectedFile(string file)
    {
        try { recent.RememberDirectory(Path.GetDirectoryName(file)!); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or ArgumentException)
        { System.Diagnostics.Trace.TraceWarning("Recent browse directory not saved: " + e.Message); }
    }
    private static Window? Owner => global::System.Windows.Application.Current?.MainWindow;

    public string? PickProjectManifest()
    {
        var picker = new OpenFileDialog
        {
            Title = "打开 AutoTest 工程",
            Filter = "AutoTest 工程 (*.autotest-project.json)|*.autotest-project.json|JSON 文件 (*.json)|*.json",
            InitialDirectory = BrowseDirectory(),
            CheckFileExists = true,
            Multiselect = false
        };
        if (picker.ShowDialog(Owner) != true) return null;
        RememberSelectedFile(picker.FileName);
        return picker.FileName;
    }

    public string? PickDbcFile(string? projectDirectory)
    {
        var picker = new OpenFileDialog
        {
            Title = "导入 DBC 文件到当前工程",
            Filter = "DBC 数据库 (*.dbc)|*.dbc",
            InitialDirectory = BrowseDirectory(projectDirectory),
            CheckFileExists = true,
            Multiselect = false
        };
        if (picker.ShowDialog(Owner) != true) return null;
        RememberSelectedFile(picker.FileName);
        return picker.FileName;
    }

    public string? PickFirmwareFile(string? projectDirectory)
    {
        var picker = new OpenFileDialog
        {
            Title = "导入固件资源（仅注册文件，不执行烧录）",
            Filter = "固件文件 (*.hex;*.s19;*.s28;*.s37)|*.hex;*.s19;*.s28;*.s37",
            InitialDirectory = BrowseDirectory(projectDirectory),
            CheckFileExists = true, Multiselect = false
        };
        if (picker.ShowDialog(Owner) != true) return null;
        RememberSelectedFile(picker.FileName);
        return picker.FileName;
    }

    public string? PickWaveEvidenceFile(string? projectDirectory)
    {
        var picker = new OpenFileDialog
        {
            Title = "导入 AutoTest 波形证据",
            Filter = "波形证据 JSON (*.json)|*.json",
            InitialDirectory = BrowseDirectory(projectDirectory),
            CheckFileExists = true, Multiselect = false
        };
        if (picker.ShowDialog(Owner) != true) return null;
        RememberSelectedFile(picker.FileName);
        return picker.FileName;
    }

    public string? AskResourceName(string title, string initialName)
    {
        var dialog = new ResourceNameDialog(title, initialName);
        if (Owner is { IsLoaded: true } owner) dialog.Owner = owner;
        return dialog.ShowDialog() == true ? dialog.ResourceName : null;
    }

    public void ShowError(string message)
    {
        var owner = Owner;
        if (owner is not null)
            MessageBox.Show(owner, message, "工程操作未完成", MessageBoxButton.OK, MessageBoxImage.Warning);
        else
            MessageBox.Show(message, "工程操作未完成", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    public bool ConfirmRemoveReference(string name)
    {
        var result = MessageBox.Show(Owner,
            "要从工程中移除资源“" + name + "”的引用吗？\n\n" +
            "此操作只修改工程清单，资源文件保留在磁盘上，不会被删除。",
            "移除资源引用", MessageBoxButton.YesNo, MessageBoxImage.Question,
            MessageBoxResult.No);
        return result == MessageBoxResult.Yes;
    }
}
