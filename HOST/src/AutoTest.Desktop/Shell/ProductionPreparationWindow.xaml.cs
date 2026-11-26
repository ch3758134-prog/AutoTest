using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AutoTest.Desktop.Projects;
using AutoTest.FlashLab.Programming;
using AutoTest.Projects.Project;
using Microsoft.Win32;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// Single V2 workcell editor: one ProjectId, optional product-to-project routing,
/// one real scanner observer, and one station fixture. No model registry, no
/// legacy schema adapter, and no alternate TEST/FLASH configuration objects.
/// </summary>
public partial class ProductionPreparationWindow : Window
{
    private readonly ProjectWorkspaceService _workspace;
    private readonly Func<bool> _mayEdit;
    private readonly SerialScannerWorkbenchViewModel _scanner;
    private readonly Action _onPublished;
    private readonly Func<Task<bool>> _publishProject;
    private readonly Func<Task> _openTestEditor;
    private readonly Action _openFlashEditor;
    private readonly Func<Task> _openProductionMonitor;
    private readonly Func<Task> _openEngineeringVerification;
    private readonly Action _openScannerSettings;
    private readonly Func<string> _hardwareStatus;
    private readonly DispatcherTimer _autosave = new()
        { Interval = TimeSpan.FromMilliseconds(850) };
    private readonly ObservableCollection<ProductionProductBinding> _products = new();
    private readonly List<ProductionIdentityRow> _registry = new();
    private StationProductionConfiguration _draft = StationProductionConfiguration.New();
    private bool _ready, _busy, _dirty, _allowClose;
    private long _editGeneration;
    private string? _loadError;

    public ProductionPreparationWindow(ProjectWorkspaceService workspace,
        Func<bool> mayEdit, SerialScannerWorkbenchViewModel scanner,
        Action onPublished, Func<Task<bool>> publishProject,
        Func<Task> openTestEditor, Action openFlashEditor,
        Func<Task> openProductionMonitor, Func<Task> openEngineeringVerification,
        Action openScannerSettings, Func<string> hardwareStatus)
    {
        _workspace = workspace;
        _mayEdit = mayEdit;
        _scanner = scanner;
        _onPublished = onPublished;
        _publishProject = publishProject;
        _openTestEditor = openTestEditor;
        _openFlashEditor = openFlashEditor;
        _openProductionMonitor = openProductionMonitor;
        _openEngineeringVerification = openEngineeringVerification;
        _openScannerSettings = openScannerSettings;
        _hardwareStatus = hardwareStatus;
        InitializeComponent();
        ProjectMappings.ItemsSource = _products;
        Loaded += async (_, _) => await LoadAsync();
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _autosave.Stop();
            _scanner.RawBarcodeObserved -= OnRawBarcode;
            _scanner.IncompleteBarcodeObserved -= OnIncompleteBarcode;
            _scanner.PropertyChanged -= OnScannerPropertyChanged;
            _workspace.CurrentChanged -= OnProjectChanged;
        };
        _scanner.RawBarcodeObserved += OnRawBarcode;
        _scanner.IncompleteBarcodeObserved += OnIncompleteBarcode;
        _scanner.PropertyChanged += OnScannerPropertyChanged;
        _workspace.CurrentChanged += OnProjectChanged;
        _autosave.Tick += async (_, _) =>
        {
            _autosave.Stop();
            if (_dirty && !_busy) await SaveAsync();
        };
        foreach (var field in new[]
        {
            StartInput, StopInput, InterlockInput, ClosedFeedback,
            OpenFeedback, SettleDelayInput, ProductKeyInput, HardwareInput,
            PositiveSample, NegativeSample
        })
            field.TextChanged += ConfigChanged;
    }

    private void Say(string message) => StatusText.Text = message;

    private void OnRawBarcode(string raw) =>
        Dispatcher.BeginInvoke(new Action(() =>
        {
            ScanRaw.Text = raw;
            PreviewStatus.Text = "已收到真实扫码原文；未因此获得生产 START 许可。";
        }));

    private void OnIncompleteBarcode(string raw) =>
        Dispatcher.BeginInvoke(new Action(() =>
        {
            ScanRaw.Text = raw;
            PreviewStatus.Text = "串口收到内容但没有形成完整条码；检查结束符。";
        }));

    private void OnScannerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SerialScannerWorkbenchViewModel.IsConnected) or
            nameof(SerialScannerWorkbenchViewModel.ConnectedPort) or
            nameof(SerialScannerWorkbenchViewModel.DeviceStatus))
            Dispatcher.BeginInvoke(new Action(RefreshDevices));
    }

    private void OnProjectChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(new Action(RefreshProject));

    private void RefreshDevices()
    {
        ScannerState.Text = (_scanner.IsConnected
            ? "扫码枪已连接 · " + _scanner.ConnectedPort
            : "扫码枪未连接") + "   |   " + _hardwareStatus();
        if (!string.IsNullOrEmpty(_scanner.LastRaw)) ScanRaw.Text = _scanner.LastRaw;
    }

    private void RefreshProject()
    {
        var current = _workspace.Current;
        ProjectSummary.Text = current is null
            ? "未打开 ECU 工程；扫码测试和手动台架仍可独立使用。"
            : current.Definition.Name + " · " +
              current.Definition.Id.Value.ToString("D") +
              "\nTEST、FLASH、DBC 和波形都来自此工程。";
    }

    private async Task LoadAsync()
    {
        if (_ready) return;
        RefreshProject();
        RefreshDevices();
        try
        {
            _draft = await StationProductionConfigurationStore.LoadDraftAsync();
            ApplyToControls(_draft);
            _ready = true;
            Say("工位设置已加载，修改自动保存。");
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            _loadError = error.Message;
            ResetOldConfig.Visibility = Visibility.Visible;
            _ready = false;
            Say("旧工位配置不能用于新版本：" + error.Message +
                "。点击“重新建立工位配置”，不会自动猜测旧型号绑定。");
        }
        await RefreshPublicationAsync();
    }

    private void ApplyToControls(StationProductionConfiguration config)
    {
        _ready = false;
        _products.Clear();
        foreach (var item in config.Products) _products.Add(item);
        _registry.Clear();
        _registry.AddRange(config.IdentityRows);
        MatchMode.SelectedIndex = config.Mode switch
        {
            StationProductionConfiguration.FixedStation => 0,
            StationProductionConfiguration.BarcodeModel => 1,
            StationProductionConfiguration.RegistryModel => 2,
            _ => throw new InvalidDataException("未知的工位身份模式。")
        };
        MixedOptions.IsExpanded = MatchMode.SelectedIndex != 0;
        RegistrySummary.Text = "已登记 " + _registry.Count + " 个 ECU 编号";
        var rule = config.Rules.Single();
        SeparatorInput.Text = rule.Separator;
        SerialIndexInput.Text = rule.SerialField.ToString(CultureInfo.InvariantCulture);
        ProductIndexInput.Text = rule.ModelField.ToString(CultureInfo.InvariantCulture);
        PositiveSample.Text = rule.Samples.FirstOrDefault(x => x.ShouldAccept)?.Raw ?? "";
        NegativeSample.Text = rule.Samples.FirstOrDefault(x => !x.ShouldAccept)?.Raw ?? "";
        var fixture = config.Fixture;
        AutoClamp.IsChecked = fixture.AutoClampEnabled;
        Clamp1.IsChecked = fixture.UseClamp1;
        Clamp2.IsChecked = fixture.UseClamp2;
        FirstClampChoice.SelectedIndex = fixture.FirstClamp == 2 ? 1 : 0;
        StartInput.Text = fixture.StartInput;
        StopInput.Text = fixture.StopInput;
        InterlockInput.Text = fixture.InterlockInput;
        ClosedFeedback.Text = fixture.ClampClosedFeedback;
        OpenFeedback.Text = fixture.ClampOpenFeedback;
        SettleDelayInput.Text = fixture.SettleDelayMs.ToString(CultureInfo.InvariantCulture);
        _ready = true;
        _dirty = false;
    }

    private void ConfigChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready || _busy) return;
        _dirty = true;
        _editGeneration++;
        _autosave.Stop();
        _autosave.Start();
    }

    private ProductionBarcodeRule BuildRule(bool mixed, bool registry)
    {
        var rule = _draft.Rules.Single();
        if (!mixed || registry)
            return ProductionBarcodeRule.NewSerial() with
            {
                RuleId = rule.RuleId, Revision = checked(rule.Revision + 1),
                Samples = Samples()
            };
        if (!int.TryParse(SerialIndexInput.Text, out var serialIndex) ||
            !int.TryParse(ProductIndexInput.Text, out var productIndex) ||
            SeparatorInput.Text.Length != 1)
            throw new FormatException("请指定单字符分隔符、产品字段和 ECU 编号字段序号。");
        return rule with
        {
            Revision = checked(rule.Revision + 1),
            Format = ProductionBarcodeRule.Delimited,
            Name = "产品字段与 ECU 唯一编号",
            Prefix = "", ExactLength = 0,
            AllowedCharacters = @"[A-Za-z0-9_.|-]+",
            Separator = SeparatorInput.Text,
            SerialField = serialIndex,
            ModelField = productIndex,
            HardwareField = 0, BatchField = 0,
            Samples = Samples()
        };
    }

    private IReadOnlyList<ProductionBarcodeSample> Samples()
    {
        var list = new List<ProductionBarcodeSample>();
        if (!string.IsNullOrWhiteSpace(PositiveSample.Text))
            list.Add(new(PositiveSample.Text.Trim(), true));
        if (!string.IsNullOrWhiteSpace(NegativeSample.Text))
            list.Add(new(NegativeSample.Text.Trim(), false));
        return list;
    }

    private StationProductionConfiguration BuildDraft()
    {
        var mode = MatchMode.SelectedIndex switch
        {
            0 => StationProductionConfiguration.FixedStation,
            1 => StationProductionConfiguration.BarcodeModel,
            2 => StationProductionConfiguration.RegistryModel,
            _ => throw new InvalidDataException("请先选择扫码与工程匹配方式。")
        };
        var fixedMode = mode == StationProductionConfiguration.FixedStation;
        var currentId = _products.Count == 1 ? _products[0].ProjectId : Guid.Empty;
        if (fixedMode && _products.Count == 0 && _workspace.Current is { } current)
        {
            currentId = current.Definition.Id.Value;
            _products.Add(new("", "", current.ManifestPath, currentId));
        }
        if (!fixedMode && _products.Any(x => string.IsNullOrWhiteSpace(x.ProductKey)))
            throw new InvalidDataException("混线工程必须填写可信产品识别码。");
        if (!int.TryParse(SettleDelayInput.Text, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var delay))
            throw new FormatException("夹具稳定时间必须为毫秒整数。");
        var fixture = _draft.Fixture with
        {
            AutoClampEnabled = AutoClamp.IsChecked == true,
            UseClamp1 = Clamp1.IsChecked == true,
            UseClamp2 = Clamp2.IsChecked == true,
            FirstClamp = Clamp1.IsChecked == true && Clamp2.IsChecked == true
                ? FirstClampChoice.SelectedIndex + 1 : 0,
            StartInput = StartInput.Text.Trim(),
            StopInput = StopInput.Text.Trim(),
            InterlockInput = InterlockInput.Text.Trim(),
            ClampClosedFeedback = ClosedFeedback.Text.Trim(),
            ClampOpenFeedback = OpenFeedback.Text.Trim(),
            SettleDelayMs = delay
        };
        return new(StationProductionConfiguration.CurrentSchema,
            checked(_draft.Revision + 1), mode,
            fixedMode ? currentId : Guid.Empty,
            [BuildRule(!fixedMode, mode == StationProductionConfiguration.RegistryModel)],
            _registry.ToArray(), _products.ToArray(), fixture);
    }

    // Compare VALUE, not draft revision or the parser's internal revision.
    // Reopening and re-applying the same station must not manufacture a
    // different publication simply because the UI serialized again.
    private static string ContentKey(StationProductionConfiguration document)
    {
        var normalized = document with
        {
            Revision = 0,
            Rules = document.Rules.Select(x => x with { Revision = 0 }).ToArray()
        };
        return System.Text.Json.JsonSerializer.Serialize(normalized);
    }

    private static string RuleKey(ProductionBarcodeRule rule) =>
        System.Text.Json.JsonSerializer.Serialize(rule with { Revision = 0 });

    private async Task<bool> SaveAsync()
    {
        if (!_ready || !_mayEdit())
        {
            Say("工位正在执行实体任务或尚未加载配置。");
            return false;
        }
        try
        {
            var generation = _editGeneration;
            var next = BuildDraft();
            var previousRule = _draft.Rules.Single();
            var nextRule = next.Rules.Single();

            // Changing clamp settings or project binding does not rewrite the
            // barcode rule revision. Only a real change in its contents does.
            if (RuleKey(previousRule) == RuleKey(nextRule))
                next = next with
                {
                    Rules = [nextRule with { Revision = previousRule.Revision }]
                };

            // Nothing to save: do not increment the revision or invalidate a
            // working station release. This is the normal repeated-Apply case.
            if (ContentKey(next) != ContentKey(_draft))
            {
                await StationProductionConfigurationStore.SaveDraftAsync(next);
                _draft = next;
            }

            _dirty = generation != _editGeneration;
            if (_dirty) _autosave.Start();
            Say(_dirty ? "还有更改正在保存" : "配置已保存");
            return true;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Say("当前修改未保存：" + error.Message);
            return false;
        }
    }

    private void BindCurrent_Click(object sender, RoutedEventArgs e)
    {
        var current = _workspace.Current;
        if (current is null) { Say("请先打开 ECU 工程。"); return; }
        if (MatchMode.SelectedIndex != 0)
        {
            Say("混线工位请在下方填写产品识别码后绑定。");
            MixedOptions.IsExpanded = true;
            return;
        }
        _products.Clear();
        _products.Add(new("", "", current.ManifestPath, current.Definition.Id.Value));
        ConfigChanged(sender, e);
        Say("已绑定当前工程：" + current.Definition.Name);
    }

    private async void SelectProject_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog
        { Filter = "AutoTest 工程 (*.json)|*.json", Title = "选择 ECU 工程清单" };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            var file = Path.GetFullPath(picker.FileName);
            var project = await ProjectRepository.OpenDefinitionAsync(file);
            if (MatchMode.SelectedIndex != 0)
            {
                Say("混线请打开目标工程，并填写产品识别码再绑定。");
                return;
            }
            _products.Clear();
            _products.Add(new("", "", file, project.Id.Value));
            ConfigChanged(sender, e);
            Say("已选择工程：" + project.Name);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { Say("所选工程无效：" + error.Message); }
    }

    private void AddMixedProject_Click(object sender, RoutedEventArgs e)
    {
        var current = _workspace.Current;
        if (current is null) { Say("请先打开需要绑定的工程。"); return; }
        var key = ProductKeyInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(key))
        { Say("混线必须填写条码中真实存在的产品识别码。"); return; }
        if (_products.Any(x => x.ProjectId == current.Definition.Id.Value ||
            x.ProductKey == key && x.HardwareCode == HardwareInput.Text.Trim()))
        { Say("同一工程或产品识别码已经存在，不能重复绑定。"); return; }
        _products.Add(new(key, HardwareInput.Text.Trim(),
            current.ManifestPath, current.Definition.Id.Value));
        ConfigChanged(sender, e);
        Say("产品识别码 " + key + " 已绑定工程：" + current.Definition.Name);
    }

    private void RemoveMixedProject_Click(object sender, RoutedEventArgs e)
    {
        if (ProjectMappings.SelectedItem is ProductionProductBinding selected)
        {
            _products.Remove(selected);
            ConfigChanged(sender, e);
        }
    }

    private async void RetryScanner_Click(object sender, RoutedEventArgs e)
    {
        await _scanner.RetryAutoConnectAsync();
        RefreshDevices();
    }
    private void ScannerSettings_Click(object sender, RoutedEventArgs e) =>
        _openScannerSettings();

    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        var raw = ScanTrial.Text.Trim();
        if (string.IsNullOrEmpty(raw)) raw = _scanner.LastRaw;
        ScanRaw.Text = raw;
        if (string.IsNullOrWhiteSpace(raw))
        { PreviewStatus.Text = "请扫码或输入一条编号。"; return; }
        try
        {
            var rule = BuildRule(MatchMode.SelectedIndex != 0,
                MatchMode.SelectedIndex == 2);
            var parsed = rule.Parse(raw);
            PreviewStatus.Text = parsed.Accepted
                ? "解析到 ECU 编号：" + parsed.Serial +
                  (MatchMode.SelectedIndex == 1 ? " · 产品识别码：" + parsed.Model : "") +
                  "（仅试算，未授权正式生产）"
                : "未匹配：" + parsed.Explanation;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { PreviewStatus.Text = "解析失败：" + error.Message; }
    }

    private void ImportRegistry_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog
        { Filter = "CSV 编号表 (*.csv)|*.csv", Title = "选择受控编号归属表" };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            var file = new FileInfo(picker.FileName);
            if (file.Length > 8_000_000)
                throw new InvalidDataException("编号表超过 8MB 限制。");
            var lines = File.ReadAllLines(picker.FileName, new UTF8Encoding(false, true));
            if (lines.Length > 100001) throw new InvalidDataException("编号表超过十万行。");
            var rows = new List<ProductionIdentityRow>();
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var columns = line.Split(',');
                if (columns.Length is < 2 or > 5)
                    throw new InvalidDataException("格式应为：编号,产品识别码,硬件版本,截止日期,批次。");
                if (columns[0].Trim().Equals("Serial", StringComparison.OrdinalIgnoreCase) ||
                    columns[0].Trim() == "唯一编号") continue;
                DateTimeOffset? expiration = null;
                if (columns.Length > 3 && !string.IsNullOrWhiteSpace(columns[3]))
                    expiration = DateTimeOffset.Parse(columns[3], CultureInfo.InvariantCulture)
                        .ToUniversalTime();
                rows.Add(new(columns[0].Trim(), columns[1].Trim(),
                    columns.Length > 2 ? columns[2].Trim() : "",
                    expiration, columns.Length > 4 ? columns[4].Trim() : ""));
            }
            if (rows.Select(x => x.Serial).Distinct(StringComparer.Ordinal).Count() != rows.Count)
                throw new InvalidDataException("编号表出现重复 ECU 编号。");
            _registry.Clear();
            _registry.AddRange(rows);
            RegistrySummary.Text = "已导入 " + rows.Count + " 条编号归属";
            ConfigChanged(sender, e);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { Say("编号表导入失败：" + error.Message); }
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            WindowState = WindowState.Minimized;
            await _openTestEditor();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { Say("TEST 编辑器无法打开：" + error.Message); }
    }
    private void Flash_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
        _openFlashEditor();
    }

    private sealed record PointerSnapshot(string Path, byte[]? Bytes);
    private async Task<PointerSnapshot[]> CapturePointersAsync(string projectRoot)
    {
        var locations = new[]
        {
            NativeFlashProfileStore.InsideRoot(projectRoot,
                NativeFlashPublicationStore.CurrentRelativePath),
            StationDeploymentStore.PathName,
            StationProductionConfigurationStore.PublishedPath
        };
        var results = new List<PointerSnapshot>();
        foreach (var path in locations)
            results.Add(new(path, File.Exists(path)
                ? await File.ReadAllBytesAsync(path) : null));
        return results.ToArray();
    }
    private static async Task RestorePointersAsync(IEnumerable<PointerSnapshot> snapshots)
    {
        foreach (var snapshot in snapshots.Reverse())
        {
            if (snapshot.Bytes is null)
            {
                if (File.Exists(snapshot.Path)) File.Delete(snapshot.Path);
                continue;
            }
            var tmp = snapshot.Path + ".rollback-" + Guid.NewGuid().ToString("N");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(snapshot.Path)!);
                await File.WriteAllBytesAsync(tmp, snapshot.Bytes);
                File.Move(tmp, snapshot.Path, true);
            }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
        }
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !_ready || !_mayEdit())
        { Say("工位正在运行或配置未准备好。"); return; }
        var project = _workspace.Current;
        if (project is null)
        { Say("请先打开准备投入工位的 ECU 工程。"); return; }
        PointerSnapshot[]? previous = null;
        try
        {
            if (!await SaveAsync()) return;
            var candidate = await StationProductionConfigurationStore.LoadDraftAsync();
            candidate.Validate();
            var issues = StationProductionConfigurationStore.InspectRules(candidate);
            if (issues.Count > 0)
                throw new InvalidDataException(string.Join("；", issues.Take(8)));
            var flash = await NativeFlashProfileStore.LoadAsync(project.RootDirectory)
                ?? throw new InvalidDataException("尚未配置原生 FLASH；请打开 FLASH 编辑器。");
            _ = NativeFlashJobBuilder.Freeze(flash.Profile, project.RootDirectory);
            var test = await AutomaticTestResourceContract.ResolveAsync(project);
            test.RequireExecutable();
            // The current project's native FLASH is published by the single
            // Apply action below. Inspect only OTHER already frozen projects
            // before changing any publication pointer.
            foreach (var binding in candidate.Products.Where(x =>
                x.ProjectId != project.Definition.Id.Value))
                await StationProductionConfigurationStore.InspectProjectAsync(binding);
            _busy = true;
            ApplyButton.IsEnabled = false;
            previous = await CapturePointersAsync(project.RootDirectory);
            if (!await _publishProject())
            {
                await RestorePointersAsync(previous);
                previous = null;
                Say("已取消，原工位版本未改变。");
                return;
            }
            var deployed = await StationDeploymentStore.RestoreAsync()
                ?? throw new InvalidDataException("应用后无法读取真实工程部署。");
            if (deployed.Project.Definition.Id != project.Definition.Id ||
                deployed.Project.ManifestSha256 != project.ManifestSha256)
                throw new InvalidDataException("应用期间工程版本已变化。");
            var published = await StationProductionConfigurationStore.PublishAsync(candidate);
            previous = null;
            _onPublished();
            Say("已应用完整工程 " + project.Definition.Name +
                " · " + published.PublicationId.ToString("N")[..10]);
            await RefreshPublicationAsync();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (previous is not null)
            {
                try
                {
                    await RestorePointersAsync(previous);
                    Say("应用失败，旧版本已恢复：" + error.Message);
                }
                catch (Exception rollback) when (rollback is not OutOfMemoryException)
                {
                    Say("应用失败且旧版本未完全恢复！禁止正式生产：" +
                        error.Message + "；恢复异常：" + rollback.Message);
                }
            }
            else Say("无法应用：" + error.Message);
        }
        finally
        {
            _busy = false;
            ApplyButton.IsEnabled = true;
        }
    }

    private async Task RefreshPublicationAsync()
    {
        try
        {
            var published = await StationProductionConfigurationStore.LoadPublishedAsync();
            if (published is null)
                PublicationState.Text = "尚无已应用的工程。";
            else
                PublicationState.Text = "版本 " +
                    published.PublicationId.ToString("N")[..10] + " · " +
                    string.Join(" / ", published.Projects.Select(x =>
                        x.ProjectId.ToString("N")[..10] + " · TEST " +
                        x.OrderedCaseNames.Count + " 用例"));
        }
        catch (Exception error)
        {
            PublicationState.Text = "旧版或损坏的工位配置不能使用：" +
                error.Message;
        }
        BlockerState.Text = "正式生产：实体 START 和持久化 Inspection 接线尚未完成，不能直接放行。" +
            "\n工程实测：按现有明确授权机制执行真实 FLASH/TEST。";
    }

    private async void ResetOld_Click(object sender, RoutedEventArgs e)
    {
        if (_mayEdit() == false) return;
        if (MessageBox.Show(this,
            "旧版型号配置与新 ProjectId 绑定不能自动对应。" +
            "\n重新建立会覆盖旧草稿（不改变已应用生产版本）。是否继续？",
            "确认重建工位草稿", MessageBoxButton.YesNo,
            MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        try
        {
            var fresh = StationProductionConfiguration.New();
            await StationProductionConfigurationStore.SaveDraftAsync(fresh);
            _draft = fresh;
            ApplyToControls(fresh);
            ResetOldConfig.Visibility = Visibility.Collapsed;
            _loadError = null;
            Say("已建立新版工位草稿，请选择需要使用的工程。");
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { Say("无法建立新版工位草稿：" + error.Message); }
    }

    private async Task NavigateAsync(Func<Task> navigate)
    {
        if (_busy) { Say("请先结束工位配置操作。"); return; }
        if (_dirty && !await SaveAsync()) return;
        _allowClose = true;
        Close();
        try { await navigate(); }
        catch (Exception error) when (error is not OutOfMemoryException)
        { MessageBox.Show("切换工作区失败：" + error.Message, "AutoTest"); }
    }
    private async void Engineering_Click(object sender, RoutedEventArgs e) =>
        await NavigateAsync(_openEngineeringVerification);
    private async void Production_Click(object sender, RoutedEventArgs e) =>
        await NavigateAsync(_openProductionMonitor);
    private async void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_dirty && !await SaveAsync()) return;
        _allowClose = true;
        Close();
    }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose || !_dirty) return;
        e.Cancel = true;
        if (await SaveAsync())
        {
            _allowClose = true;
            Close();
        }
    }
}
