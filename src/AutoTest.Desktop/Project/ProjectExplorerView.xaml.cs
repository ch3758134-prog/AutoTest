using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AutoTest.Desktop.Shell;

namespace AutoTest.Desktop.Project;

/// <summary>
/// IDE-only asset navigator. Never edits DBC or TEST through side-bar controls;
/// every double-click opens the existing project-backed editor and its VM.
/// </summary>
public partial class ProjectExplorerView : UserControl
{
    public ProjectExplorerView(MainWindowViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    // Type-safe document requests: the explorer no longer depends on
    // a magic numbered TabControl or a particular layout tree.
    public event Action<string>? OpenRequested;

    private void GoTo(string key) => OpenRequested?.Invoke(key);

    private bool OpenResource(WorkbenchResourceNode node)
    {
        string? key = node.Kind switch
        {
            "test" => "program",
            "dbc" => "dbc",
            "controller" => "controller",
            "wave" => "wave",
            "flash" => "flash",
            "scan" => "scan",
            "records" => "records",
            _ => null
        };
        if (key is null) return false;
        GoTo(key);
        return true;
    }

    private void ResourceTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ResourceTree.SelectedItem is WorkbenchResourceNode node && OpenResource(node))
            e.Handled = true;
    }

    private void ResourceTree_RightClick(object sender, MouseButtonEventArgs e)
    {
        // Nested resources must be selected by hit target before the context menu opens.
        for (DependencyObject? visual = e.OriginalSource as DependencyObject;
             visual is not null;
             visual = VisualTreeHelper.GetParent(visual))
        {
            if (visual is not TreeViewItem node) continue;
            node.IsSelected = true;
            node.Focus();
            break;
        }
    }

    private void OpenSelectedResource_Click(object sender, RoutedEventArgs e)
    {
        if (ResourceTree.SelectedItem is WorkbenchResourceNode node)
            OpenResource(node);
    }

    private void OpenTest_Click(object sender, RoutedEventArgs e) => GoTo("program");
    private void OpenDbc_Click(object sender, RoutedEventArgs e) => GoTo("dbc");
    private void OpenRules_Click(object sender, RoutedEventArgs e) => GoTo("scan");

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm ||
            string.IsNullOrWhiteSpace(vm.Workspace.ProjectPath))
        {
            MessageBox.Show(Window.GetWindow(this),
                "当前工程尚未保存到磁盘。请先保存工程，再打开所在目录。",
                "工程文件", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var folder = Path.GetDirectoryName(vm.Workspace.ProjectPath);
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            MessageBox.Show(Window.GetWindow(this), "工程目录不存在：" + folder,
                "工程文件", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = "\"" + folder + "\"",
            UseShellExecute = true
        });
    }
}
