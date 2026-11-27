using System.Windows;
using System.Windows.Controls;
using AutoTest.Desktop.Projects;
using Microsoft.Win32;

namespace AutoTest.Desktop.Shell;

/// <summary>Admin workbench only. Preview never actuates the station.</summary>
public partial class ProductionProjectLibraryWindow : Window
{
    private readonly ProductionProjectCatalog _catalog;
    private readonly ProjectWorkspaceService _workspace;
    private readonly Func<bool> _canModify;
    private readonly Func<bool> _canExport;
    private readonly Action _configurationChanged;
    private bool _busy;
    private bool _loading;
    private IReadOnlyList<InstalledProductionPackage> _loadedPackages =
        Array.Empty<InstalledProductionPackage>();

    public sealed record BindingDisplay(
        string Marker, string ProjectName, int ProjectRevision, int Revision);

    public ProductionProjectLibraryWindow(ProductionProjectCatalog catalog,
        ProjectWorkspaceService workspace, Func<bool> canModify,
        Func<bool> canExport, Action configurationChanged)
    {
        _catalog = catalog;
        _workspace = workspace;
        _canModify = canModify;
        _canExport = canExport;
        _configurationChanged = configurationChanged;
        InitializeComponent();
        ExportButton.Visibility = _canExport() ? Visibility.Visible : Visibility.Collapsed;
        Loaded += async (_, _) => await ReloadAsync();
    }

    private InstalledProductionPackage? Selection =>
        CatalogTable.SelectedItem as InstalledProductionPackage;

    private void Say(string message) => Status.Text = message;

    private async Task ReloadAsync()
    {
        if (_loading) return;
        _loading = true;
        try
        {
            var packages = await _catalog.ListAsync();
            var bindings = await _catalog.ListBindingsAsync();
            _loadedPackages = packages;
            ApplySearchFilter();
            EmptyCatalogHint.Visibility = packages.Count == 0
                ? Visibility.Visible : Visibility.Collapsed;
            BindingsTable.ItemsSource = bindings.Select(binding =>
            {
                var package = packages.FirstOrDefault(item =>
                    item.PackageSha256 == binding.PackageSha256);
                return new BindingDisplay(binding.Marker,
                    package?.ProjectName ?? "已失效的工程",
                    package?.ProjectRevision ?? 0, binding.Revision);
            }).ToArray();
            Say("已导入 " + packages.Count + " 份工程版本，" +
                bindings.Count + " 个固定扫码标记。");
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Say("读取生产工程库失败：" + error.Message);
        }
        finally { _loading = false; }
    }

    private void SearchChanged(object sender, TextChangedEventArgs e) =>
        ApplySearchFilter();

    private void ApplySearchFilter()
    {
        if (SearchBox is null || CatalogTable is null) return;
        var text = SearchBox.Text.Trim();
        CatalogTable.ItemsSource = string.IsNullOrEmpty(text)
            ? _loadedPackages
            : _loadedPackages.Where(p =>
                p.ProjectName.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                p.PackageSha256.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                p.ProjectRevision.ToString().Contains(text,
                    StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    private void CatalogSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedSummary is null) return;
        SelectedSummary.Text = Selection is { } p
            ? "已选择 " + p.ProjectName + " · 修订 " + p.ProjectRevision +
              "。输入一个固定标记，将它绑定到此工程版本。"
            : "选择一份已导入的工程版本。";
    }

    private async Task ModifyAsync(Func<Task> operation)
    {
        if (_busy) return;
        if (!_canModify())
        {
            Say("正在测试或使用设备，不能修改生产工程配置。");
            return;
        }
        _busy = true;
        try
        {
            await operation();
            _configurationChanged();
            await ReloadAsync();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Say("操作未完成：" + error.Message);
        }
        finally { _busy = false; }
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入完整检测工程",
            Filter = "AutoTest 工程包 (*.atpkg)|*.atpkg",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true) return;
        await ModifyAsync(async () =>
        {
            foreach (var file in dialog.FileNames)
                await _catalog.ImportAsync(file);
        });
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (!_canExport() || _workspace.Current is not { } project)
        {
            Say("请在工程工作区打开并检查要导出的检测工程。");
            return;
        }
        var dialog = new SaveFileDialog
        {
            Title = "导出供生产使用的完整工程",
            Filter = "AutoTest 工程包 (*.atpkg)|*.atpkg",
            DefaultExt = ".atpkg",
            AddExtension = true,
            FileName = project.Definition.Name + ".atpkg"
        };
        if (dialog.ShowDialog(this) != true) return;
        await ModifyAsync(async () =>
        {
            await ProductionProjectPackageService.ExportAsync(
                project.ManifestPath, dialog.FileName);
        });
    }

    private async void Bind_Click(object sender, RoutedEventArgs e)
    {
        if (Selection is not { } package)
        {
            Say("请先选中要关联的工程版本。");
            return;
        }
        var marker = MarkerInput.Text;
        await ModifyAsync(() => _catalog.BindMarkerAsync(
            marker, package.PackageSha256));
    }

    private async void RemoveBinding_Click(object sender, RoutedEventArgs e)
    {
        if (BindingsTable.SelectedItem is not BindingDisplay selected)
        {
            Say("请先选中要删除的识别标记。");
            return;
        }
        await ModifyAsync(() => _catalog.RemoveMarkerAsync(selected.Marker));
    }

    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var route = await _catalog.ResolveAsync(SampleBarcodeInput.Text);
            if (!route.Accepted || route.Package is null)
            {
                PreviewResult.Text = "未匹配：" + route.Message;
                return;
            }
            await _catalog.VerifySelectionAsync(route.Package);
            PreviewResult.Text = "完整 ECU 编号：" + route.FullBarcode +
                " · 标记：" + route.Marker +
                " · 工程：" + route.Package.ProjectName +
                "（修订 " + route.Package.ProjectRevision + "）。" +
                "这里只检查配置，不会启动硬件。";
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            PreviewResult.Text = "匹配检查失败：" + error.Message;
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) =>
        await ReloadAsync();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}