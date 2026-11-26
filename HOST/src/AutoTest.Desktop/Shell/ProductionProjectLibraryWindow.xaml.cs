using System.IO;
using System.Windows;
using System.Windows.Controls;
using AutoTest.Desktop.Projects;
using Microsoft.Win32;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// Administrative UI only. Preview does not create a production run or send
/// any hardware command. A future Production.exe may host this same service.
/// </summary>
public partial class ProductionProjectLibraryWindow : Window
{
    private readonly ProductionProjectCatalog _catalog;
    private readonly ProjectWorkspaceService _workspace;
    private readonly Func<bool> _canModify;
    private readonly Action _configurationChanged;
    private bool _busy;
    private bool _loading;
    private IReadOnlyList<InstalledProductionPackage> _loadedPackages =
        Array.Empty<InstalledProductionPackage>();

    private sealed record BindingDisplay(string Product, string Hardware, string Version);

    public ProductionProjectLibraryWindow(ProductionProjectCatalog catalog,
        ProjectWorkspaceService workspace, Func<bool> canModify,
        Action configurationChanged)
    {
        _catalog = catalog;
        _workspace = workspace;
        _canModify = canModify;
        _configurationChanged = configurationChanged;
        InitializeComponent();
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
            var settings = await _catalog.GetSettingsAsync();
            var bindings = await _catalog.ListBindingsAsync();
            _loadedPackages = packages;
            ApplySearchFilter();
            ModeBox.SelectedIndex = settings.Mode switch
            {
                ProductionRoutingSettings.Single => 0,
                ProductionRoutingSettings.Fields => 1,
                ProductionRoutingSettings.Registry => 2,
                _ => -1
            };
            SeparatorInput.Text = settings.Separator;
            ProductFieldInput.Text = settings.ProductField.ToString();
            SerialFieldInput.Text = settings.SerialField.ToString();
            HardwareFieldInput.Text = settings.HardwareField.ToString();
            BindingsTable.ItemsSource = bindings.Select(x =>
                new BindingDisplay(x.Product, x.Hardware,
                    packages.FirstOrDefault(p => p.PackageSha256 == x.PackageSha)
                        is { } project
                        ? project.ProjectName + " · 修订 " + project.ProjectRevision
                        : "工程已丢失")).ToArray();
            Say("已安装 " + packages.Count + " 份工程版本 · " +
                bindings.Count + " 条工程关联。");
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Say("无法读取工程库：" + error.Message);
        }
        finally { _loading = false; }
    }

    private void SearchChanged(object sender, TextChangedEventArgs e) =>
        ApplySearchFilter();

    private void ApplySearchFilter()
    {
        if (SearchBox is null) return;
        var query = SearchBox.Text.Trim();
        CatalogTable.ItemsSource = string.IsNullOrEmpty(query)
            ? _loadedPackages
            : _loadedPackages.Where(p =>
                p.ProjectName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                p.PackageSha256.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                p.ProjectRevision.ToString().Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToArray();
    }

    private void CatalogSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectedSummary.Text = Selection is { } project
            ? "已选择：" + project.ProjectName + " · 修订 " +
              project.ProjectRevision + "。工程版本不可直接编辑，更新请导入新工程包。"
            : "选择一份已导入的工程版本。";
    }

    private async Task ModifyAsync(Func<Task> change)
    {
        if (_busy) return;
        if (!_canModify())
        {
            Say("工位正在测试或设备操作中，请结束当前任务后修改工程库。");
            return;
        }
        _busy = true;
        try
        {
            await change();
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
        var picker = new OpenFileDialog
        {
            Title = "导入生产检测工程包",
            Filter = "AutoTest 工程包 (*.atpkg)|*.atpkg",
            Multiselect = true
        };
        if (picker.ShowDialog(this) != true) return;
        await ModifyAsync(async () =>
        {
            var imported = 0;
            foreach (var file in picker.FileNames)
            {
                var project = await _catalog.ImportAsync(file);
                imported++;
                Say("已导入：" + project.ProjectName + " · 修订 " +
                    project.ProjectRevision);
            }
            Say("成功导入 " + imported + " 份工程包。请设置扫码与工程的对应关系。");
        });
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace.Current is not { } project)
        {
            Say("请先在工程工作区打开需要导出的工程。");
            return;
        }
        var picker = new SaveFileDialog
        {
            Title = "导出供生产使用的工程包",
            Filter = "AutoTest 工程包 (*.atpkg)|*.atpkg",
            DefaultExt = ".atpkg",
            AddExtension = true,
            FileName = project.Definition.Name + ".atpkg"
        };
        if (picker.ShowDialog(this) != true) return;
        await ModifyAsync(async () =>
        {
            var exported = await ProductionProjectPackageService.ExportAsync(
                project.ManifestPath, picker.FileName);
            Say("已导出：" + exported.ProjectName + "，包含 " +
                exported.Files.Count + " 个资源文件。");
        });
    }

    private async void Bind_Click(object sender, RoutedEventArgs e)
    {
        if (Selection is not { } project)
        {
            Say("请先选择一份工程版本。");
            return;
        }
        await ModifyAsync(async () =>
        {
            await _catalog.BindAsync(ProductKeyInput.Text, HardwareInput.Text,
                project.PackageSha256);
        });
    }

    private async void RemoveBinding_Click(object sender, RoutedEventArgs e)
    {
        if (BindingsTable.SelectedItem is not BindingDisplay selected)
        {
            Say("请先选中需要删除的工程关联。");
            return;
        }
        await ModifyAsync(async () =>
        {
            await _catalog.RemoveBindingAsync(selected.Product, selected.Hardware);
        });
    }

    private async void UseSingle_Click(object sender, RoutedEventArgs e)
    {
        if (Selection is not { } project)
        {
            Say("请先选择一份工程版本。");
            return;
        }
        await ModifyAsync(async () =>
        {
            await _catalog.BindAsync("SINGLE", "", project.PackageSha256);
            var current = await _catalog.GetSettingsAsync();
            await _catalog.SaveSettingsAsync(current with
            {
                Mode = ProductionRoutingSettings.Single
            });
        });
    }

    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        await ModifyAsync(async () =>
        {
            var mode = ModeBox.SelectedIndex switch
            {
                0 => ProductionRoutingSettings.Single,
                1 => ProductionRoutingSettings.Fields,
                2 => ProductionRoutingSettings.Registry,
                _ => throw new InvalidDataException("请选择一种编号识别方式。")
            };
            if (!int.TryParse(ProductFieldInput.Text, out var product) ||
                !int.TryParse(SerialFieldInput.Text, out var serial) ||
                !int.TryParse(HardwareFieldInput.Text, out var hardware))
                throw new InvalidDataException("条码字段位置必须为数字。");
            await _catalog.SaveSettingsAsync(
                new(mode, SeparatorInput.Text, product, serial, hardware));
        });
    }

    private async void RegisterSerial_Click(object sender, RoutedEventArgs e)
    {
        await ModifyAsync(async () =>
        {
            await _catalog.SetRegistryIdentityAsync(
                SerialRegistryInput.Text, ProductKeyInput.Text, HardwareInput.Text);
        });
    }

    private async void ImportRegistry_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog
        {
            Title = "导入编号归属表",
            Filter = "CSV 编号表 (*.csv)|*.csv"
        };
        if (picker.ShowDialog(this) != true) return;
        await ModifyAsync(async () =>
        {
            var count = await _catalog.ImportRegistryCsvAsync(picker.FileName);
            PreviewResult.Text = "已导入 " + count +
                " 条编号归属。重新扫码时将使用最新登记关系。";
        });
    }

    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var match = await _catalog.ResolveAsync(SampleBarcodeInput.Text);
            if (!match.Accepted || match.Package is null)
            {
                PreviewResult.Text = "未匹配：" + match.Message;
                return;
            }
            await _catalog.VerifySelectionAsync(match.Package);
            PreviewResult.Text = "ECU 编号：" + match.Serial +
                " · 对应工程：" + match.Package.ProjectName +
                "（修订 " + match.Package.ProjectRevision + "）" +
                "。这里只检查匹配，不会启动检测。";
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            PreviewResult.Text = "匹配检查失败：" + error.Message;
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ReloadAsync();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
