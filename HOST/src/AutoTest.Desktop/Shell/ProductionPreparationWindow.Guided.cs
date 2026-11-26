using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using AutoTest.Desktop.Projects;
using AutoTest.FlashLab.Programming;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// A task-oriented, fixed-model station commissioning surface. Existing
/// multi-model and barcode rule editors are retained as explicit advanced
/// tools. No UI operation here may authorize CAN, FLASH, START or a PASS.
/// </summary>
public partial class ProductionPreparationWindow
{
    private TextBlock? _guidedProject;
    private TextBlock? _guidedDevice;
    private TextBlock? _guidedScan;
    private TextBlock? _guidedPublication;
    private TextBlock? _guidedBlocker;
    private TextBlock? _guidedFixturePlan;
    private CheckBox? _guidedFixtureEnabled;
    private bool _syncingGuidedFixture;

    // A successful barcode read belongs to one concrete scanner/COM session.
    // A disconnect or COM rebind invalidates the proof even if the port name
    // is later reused by another USB serial device.
    private void OnGuidedScannerPropertyChanged(object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(SerialScannerWorkbenchViewModel.IsConnected) or
            nameof(SerialScannerWorkbenchViewModel.ConnectedPort) or
            nameof(SerialScannerWorkbenchViewModel.DeviceStatus))) return;
        Dispatcher.BeginInvoke(new Action(async () =>
        {
            if (!IsVisible || _scanner is null) return;
            // A COM disconnect must never erase the last real barcode.
            ConnectionHint = _scanner.IsConnected
                ? "扫码枪：" + _scanner.ConnectedPort : "扫码枪：未连接";
            await RefreshGuidedOverviewAsync();
        }));
    }

    private static readonly Brush GuidedInk =
        new SolidColorBrush(Color.FromRgb(38, 60, 79));
    private static readonly Brush GuidedMuted =
        new SolidColorBrush(Color.FromRgb(94, 115, 133));

    private static TextBlock GuidedText(string value, bool prominent = false)
        => new()
        {
            Text = value, Foreground = prominent ? GuidedInk : GuidedMuted,
            FontSize = prominent ? 14 : 12.5,
            FontWeight = prominent ? FontWeights.SemiBold : FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 1, 0, 4)
        };

    private static Border GuidedCard(string title, out StackPanel content)
    {
        content = new StackPanel();
        content.Children.Add(GuidedText(title, prominent: true));
        return new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(222, 230, 237)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, 10),
            Child = content
        };
    }

    private static Button GuidedButton(string name, bool primary = false)
    {
        var button = new Button
        {
            Content = name,
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 5, 7, 0),
            MinHeight = 30,
            FontSize = 12.5,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        if (primary)
        {
            button.Background = new SolidColorBrush(Color.FromRgb(38, 105, 179));
            button.Foreground = Brushes.White;
            button.BorderBrush = button.Background;
        }
        return button;
    }

    // One glance: active product, scanner observation, production sequence,
    // and whether this station version was applied. All original specialist
    // editors remain available below and edit the canonical same documents.
    internal UIElement CreateGuidedOverview()
    {
        var root = new StackPanel
        {
            Margin = new Thickness(14, 12, 14, 10),
            MaxWidth = 1060
        };

        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var project = GuidedCard("当前 ECU 工程", out var projectBody);
        project.Margin = new Thickness(0, 0, 7, 12);
        _guidedProject = GuidedText("检查工程中…", prominent: true);
        projectBody.Children.Add(_guidedProject);
        var projectActions = new WrapPanel();
        var flash = GuidedButton("编辑 FLASH");
        flash.Click += (_, _) =>
        {
            if (_openFlashEditor is null) { Status = "FLASH 编辑器未连接。"; return; }
            WindowState = WindowState.Minimized;
            _openFlashEditor();
        };
        projectActions.Children.Add(flash);
        var test = GuidedButton("编辑 TEST");
        test.Click += async (_, _) =>
        {
            if (_openTestEditor is null) { Status = "TEST 编辑器未连接。"; return; }
            try
            {
                WindowState = WindowState.Minimized;
                await _openTestEditor();
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            { Status = "打开 TEST 失败：" + error.Message; }
        };
        projectActions.Children.Add(test);
        var binding = GuidedButton("工程绑定");
        binding.Click += (_, _) => OpenStationSection(0);
        projectActions.Children.Add(binding);
        var diagnostics = GuidedButton("资源问题");
        diagnostics.Click += (_, _) => ShowGuidedProjectDiagnostics();
        projectActions.Children.Add(diagnostics);
        projectBody.Children.Add(projectActions);
        Grid.SetColumn(project, 0);
        top.Children.Add(project);

        var scanner = GuidedCard("扫码与产品识别", out var scannerBody);
        scanner.Margin = new Thickness(7, 0, 0, 12);
        _guidedDevice = GuidedText("检查扫码设备中…");
        scannerBody.Children.Add(_guidedDevice);
        _guidedScan = GuidedText(string.IsNullOrWhiteSpace(_scanner?.LastRaw)
            ? "等待扫码原文…" : "扫码原文：" + _scanner.LastRaw, prominent: true);
        scannerBody.Children.Add(_guidedScan);
        var scanActions = new WrapPanel();
        var retry = GuidedButton("重新检测扫码枪");
        retry.Click += RetryScanner_Click;
        scanActions.Children.Add(retry);
        var device = GuidedButton("设备设置");
        device.Click += (_, _) => OpenStationSection(3);
        scanActions.Children.Add(device);
        var codes = GuidedButton("编号规则");
        codes.Click += (_, _) => OpenStationSection(1);
        scanActions.Children.Add(codes);
        scannerBody.Children.Add(scanActions);
        Grid.SetColumn(scanner, 1);
        top.Children.Add(scanner);
        root.Children.Add(top);

        root.Children.Add(GuidedCard("生产检测顺序（配置预览）", out var fixtureBody));
        var process = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        foreach (var item in new[]
        {
            "扫码", "→", "实体启动 / 夹紧", "→", "FLASH", "→",
            "完整 TEST", "→", "记录", "→", "释放"
        })
            process.Children.Add(new TextBlock
            {
                Text = item,
                FontSize = 13,
                FontWeight = item == "→" ? FontWeights.Normal : FontWeights.Medium,
                Foreground = item == "→" ? GuidedMuted : GuidedInk,
                Margin = new Thickness(0, 2, item == "→" ? 10 : 8, 4)
            });
        fixtureBody.Children.Add(process);
        var fixtureActions = new WrapPanel();
        _guidedFixtureEnabled = new CheckBox
        {
            Content = "自动压紧 / 释放",
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Foreground = GuidedInk,
            Margin = new Thickness(0, 10, 20, 4),
            MinHeight = 26
        };
        _guidedFixtureEnabled.Checked += (_, _) => OnGuidedFixtureEnabledChanged();
        _guidedFixtureEnabled.Unchecked += (_, _) => OnGuidedFixtureEnabledChanged();
        fixtureActions.Children.Add(_guidedFixtureEnabled);
        var hardware = GuidedButton("夹具与实体按钮");
        hardware.Click += (_, _) => OpenStationSection(3);
        fixtureActions.Children.Add(hardware);
        fixtureBody.Children.Add(fixtureActions);
        _guidedFixturePlan = GuidedText("读取夹具配置中…");
        fixtureBody.Children.Add(_guidedFixturePlan);

        root.Children.Add(GuidedCard("工位应用状态", out var publishedBody));
        _guidedPublication = GuidedText("检查当前应用版本中…", prominent: true);
        publishedBody.Children.Add(_guidedPublication);
        _guidedBlocker = GuidedText("检查运行条件中…");
        publishedBody.Children.Add(_guidedBlocker);
        var blocked = GuidedText(
            "正式生产尚未接通实体启动与检测归档；下方可进入真实整组实测（不计入生产）。");
        publishedBody.Children.Add(blocked);
        var actions = new WrapPanel();
        var engineering = GuidedButton("进入真实整组实测");
        engineering.Click += StartEngineering_Click;
        actions.Children.Add(engineering);
        var details = GuidedButton("查看待处理问题");
        details.Click += (_, _) => OpenStationSection(4);
        actions.Children.Add(details);
        publishedBody.Children.Add(actions);

        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
            Content = root
        };
    }

    private void OnGuidedFixtureEnabledChanged()
    {
        if (_syncingGuidedFixture || !_ready) return;
        if (!_mayEdit() || _busy)
        {
            UpdateGuidedFixtureText();
            Status = "工位运行中或配置锁定，不能修改自动夹具设置。";
            return;
        }
        if (_guidedFixtureEnabled?.IsChecked is not bool enabled) return;
        var current = Fixture.AutoClampEnabled &&
            (Fixture.UseClamp1 || Fixture.UseClamp2);
        if (enabled == current) return;
        if (enabled)
        {
            // First installation defaults to output 1. Re-enabling a station
            // preserves its explicitly installed output 2/dual routing.
            if (!Fixture.UseClamp1 && !Fixture.UseClamp2)
                Fixture.UseClamp1 = true;
            Fixture.AutoClampEnabled = true;
        }
        else Fixture.AutoClampEnabled = false;
        MarkDirty();
        UpdateGuidedFixtureText();
    }

    private void UpdateGuidedFixtureText()
    {
        var active = Fixture.AutoClampEnabled &&
            (Fixture.UseClamp1 || Fixture.UseClamp2);
        if (_guidedFixtureEnabled is not null)
        {
            _syncingGuidedFixture = true;
            try { _guidedFixtureEnabled.IsChecked = active; }
            finally { _syncingGuidedFixture = false; }
        }
        if (_guidedFixturePlan is null) return;
        try
        {
            var plan = StationFixtureExecutionPlan.From(Fixture.ToModel());
            _guidedFixturePlan.Text = plan.Press.Count switch
            {
                0 => "自动夹具未启用",
                1 => "1 路夹具 · 检测前压紧，符合释放条件后松开",
                _ => "2 路夹具 · 按配置顺序压紧、逆序释放"
            };
        }
        catch (Exception)
        {
            _guidedFixturePlan.Text = "夹具配置待核对 · 点击“夹具与实体按钮”";
        }
    }

    private string _guidedProjectDiagnostics = "";

    private void ShowGuidedProjectDiagnostics()
    {
        var message = string.IsNullOrWhiteSpace(_guidedProjectDiagnostics)
            ? "当前没有可用的工程检查详情；请打开 ECU 工程并刷新生产准备。"
            : _guidedProjectDiagnostics;
        MessageBox.Show(this, message.Length > 10000 ? message[..10000] : message,
            "ECU 工程资源核查（仅诊断）", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private Task PreviewGuidedBarcodeAsync(string raw, bool actualDevice)
    {
        if (_guidedScan is null) return Task.CompletedTask;
        var origin = actualDevice ? "真实扫码" : "规则试算";
        _guidedScan.Text = origin + "：" + raw;
        if (!_ready || string.IsNullOrWhiteSpace(raw))
            return Task.CompletedTask;
        try
        {
            // Parse only; no scanner observation here can authorize production.
            var config = BuildDraft();
            var matches = config.Rules.Where(x => x.Enabled)
                .Select(x => x.Parse(raw))
                .Where(x => x.Accepted).ToArray();
            if (matches.Length == 0)
            {
                _guidedScan.Text += "\n未匹配编码规则";
                return Task.CompletedTask;
            }
            var unique = matches.Select(x =>
                x.Serial + "|" + x.Model + "|" + x.Hardware + "|" + x.Batch)
                .Distinct(StringComparer.Ordinal).Count();
            if (unique != 1)
            {
                _guidedScan.Text += "\n编号解析有冲突 · 检查规则";
                return Task.CompletedTask;
            }
            _guidedScan.Text += "\nECU 编号：" + matches[0].Serial +
                (config.Mode == StationProductionConfiguration.FixedStation
                    ? " · 当前固定工程" : " · 型号待核对");
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            _guidedScan.Text += "\n解析失败：" + error.Message;
        }
        return Task.CompletedTask;
    }

    // The current R1 uses three legacy publication pointers (FLASH, project,
    // station). Snapshot them before applying from this single user action.
    // If a later stage fails, restore the former committed version rather
    // than leaving a partially applied workcell. No physical USB operations.
    private sealed record StationPointerSnapshot(string Path, byte[]? Bytes);

    private static async Task<StationPointerSnapshot[]> CaptureStationPointersAsync(
        string projectRoot)
    {
        var paths = new[]
        {
            NativeFlashProfileStore.InsideRoot(projectRoot,
                NativeFlashPublicationStore.CurrentRelativePath),
            StationDeploymentStore.PathName,
            StationProductionConfigurationStore.PublishedPath
        };
        var snapshots = new List<StationPointerSnapshot>();
        foreach (var path in paths)
            snapshots.Add(new(path, File.Exists(path)
                ? await File.ReadAllBytesAsync(path) : null));
        return snapshots.ToArray();
    }

    private static async Task RestoreStationPointersAsync(
        IReadOnlyList<StationPointerSnapshot> snapshots)
    {
        // The last publication is restored first. A reader seeing a
        // mismatched release during this operation fails closed.
        foreach (var snapshot in snapshots.Reverse())
        {
            var current = File.Exists(snapshot.Path)
                ? await File.ReadAllBytesAsync(snapshot.Path) : null;
            if (snapshot.Bytes is null && current is null ||
                snapshot.Bytes is not null && current is not null &&
                snapshot.Bytes.AsSpan().SequenceEqual(current))
                continue;
            if (snapshot.Bytes is null)
            {
                File.Delete(snapshot.Path);
                continue;
            }
            var temporary = snapshot.Path + ".rollback-" +
                Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(snapshot.Path)!);
                await File.WriteAllBytesAsync(temporary, snapshot.Bytes);
                File.Move(temporary, snapshot.Path, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    private async Task GuidedPublishAsync()
    {
        if (!_ready || _busy || !_mayEdit())
        {
            Status = "工位正忙或配置未加载，暂不能应用。";
            return;
        }
        var project = _workspace.Current;
        if (project is null)
        {
            Status = "请先打开需要应用到工位的 ECU 工程。";
            return;
        }
        if (IdentityMode == StationProductionConfiguration.FixedStation)
        {
            if (FixedProjectId != Guid.Empty &&
                FixedProjectId != project.Definition.Id.Value)
            {
                Status = "固定工位已绑定另一份工程，请先明确选择工程。";
                OpenStationSection(0);
                return;
            }
            FixedProjectId = project.Definition.Id.Value;
            if (Products.Count == 0)
            {
                Products.Add(new ProductRow
                {
                    ProjectId = project.Definition.Id.Value,
                    ManifestPath = project.ManifestPath
                });
                MarkDirty();
            }
            else if (Products.Count != 1 ||
                Products[0].ProjectId != project.Definition.Id.Value ||
                !string.Equals(Products[0].ManifestPath, project.ManifestPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                Status = "工程绑定不一致，请核对唯一 TEST 工程。";
                OpenStationSection(0);
                return;
            }
        }
        StationPointerSnapshot[]? snapshots = null;
        try
        {
            if (!await SaveAsync()) return;
            var config = await StationProductionConfigurationStore.LoadDraftAsync();
            var issues = StationProductionConfigurationStore.InspectRules(config);
            if (issues.Count != 0)
                throw new InvalidDataException(string.Join("；", issues.Take(8)));
            // Read and verify the real FLASH/TEST closure before any publication.
            var native = await NativeFlashProfileStore.LoadAsync(project.RootDirectory)
                ?? throw new InvalidDataException("工程未配置原生 FLASH。请点击“编辑 FLASH”。");
            _ = NativeFlashJobBuilder.Freeze(native.Profile, project.RootDirectory);
            var test = await AutomaticTestResourceContract.ResolveAsync(project);
            test.RequireExecutable();
            foreach (var other in config.Products.Where(x =>
                x.ProjectId != project.Definition.Id.Value))
                await StationProductionConfigurationStore.InspectProjectAsync(other);

            var old = await StationProductionConfigurationStore.LoadPublishedAsync();
            if (old is not null &&
                old.ConfigurationSha256 == StationProductionConfigurationStore.ConfigurationSha(config))
            {
                var pin = old.Projects.SingleOrDefault(x =>
                    x.ProjectId == project.Definition.Id.Value);
                var deployment = await StationDeploymentStore.RestoreAsync();
                if (pin is not null && deployment?.Project.Definition.Id == project.Definition.Id &&
                    deployment.Project.ManifestSha256 == project.ManifestSha256)
                {
                    await StationProductionConfigurationStore.VerifyProductAsync(pin);
                    Status = "当前工程及工位配置已应用，无需重复发布。";
                    await RefreshOverviewAsync();
                    return;
                }
            }

            if (_publishProject is null)
                throw new InvalidOperationException("工位真实工程发布服务未连接。");
            var editGeneration = _editGeneration;
            _busy = true;
            snapshots = await CaptureStationPointersAsync(project.RootDirectory);
            if (!await _publishProject())
            {
                await RestoreStationPointersAsync(snapshots);
                snapshots = null;
                Status = "已取消应用，原生产版本保持不变。";
                return;
            }
            if (editGeneration != _editGeneration ||
                _workspace.Current?.Definition.Id != project.Definition.Id ||
                _workspace.Current?.ManifestSha256 != project.ManifestSha256)
                throw new IOException("发布期间工程或工位配置发生变更。");
            var release = await StationProductionConfigurationStore.PublishAsync(config);
            snapshots = null;
            _onPublished?.Invoke();
            Status = "工位已应用工程 " + project.Definition.Name + " · " +
                release.PublicationId.ToString("N")[..10] +
                "。正式生产仍需真实 START、机械反馈和正式 Inspection。";
            await RefreshOverviewAsync();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (snapshots is not null)
            {
                try
                {
                    await RestoreStationPointersAsync(snapshots);
                    Status = "应用失败，已恢复原工位版本：" + error.Message;
                }
                catch (Exception rollback) when (rollback is not OutOfMemoryException)
                {
                    Status = "工位版本恢复失败，必须停止正式生产：" +
                        error.Message + "；恢复错误：" + rollback.Message;
                }
            }
            else Status = "无法应用工程：" + error.Message;
        }
        finally { _busy = false; }
    }

    private async Task RefreshGuidedOverviewAsync()
    {
        if (_guidedProject is null || !_ready) return;
        UpdateGuidedFixtureText();
        var current = _workspace.Current;
        if (current is null)
        {
            _guidedProject.Text = "未打开 ECU 工程";
            _guidedProjectDiagnostics = "请先打开并保存 ECU 工程。";
        }
        else
        {
            _guidedProject.Text = current.Definition.Name;
            _guidedProjectDiagnostics = "当前工程：" + current.ManifestPath;
            try
            {
                var test = await AutomaticTestResourceContract.ResolveAsync(current);
                var flash = await NativeFlashProfileStore.LoadAsync(current.RootDirectory);
                if (_workspace.Current?.ManifestSha256 != current.ManifestSha256) return;
                var issues = test.Issues.ToArray();
                _guidedProject.Text += "\nTEST " + test.Cases.Count + " 用例 · " +
                    (test.CanExecute ? "资源正常" : issues.Length + " 项问题") +
                    "   |   FLASH " + (flash is null ? "未配置" : "已保存");
                _guidedProjectDiagnostics = test.CanExecute
                    ? "TEST 资源闭包检查通过。\n工程清单：" + current.ManifestPath +
                      "\nFLASH 与固件在应用工位时再执行完整验证。"
                    : "TEST 资源问题：\n" + string.Join("\n", issues.Take(40));
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                _guidedProject.Text += "\n资源检查异常 · 点击“资源问题”";
                _guidedProjectDiagnostics = error.ToString();
            }
        }

        if (_guidedDevice is not null)
        {
            var controller = _stationDeviceStatus?.Invoke() ?? "主控：状态未知";
            controller = controller.Replace("主控 USB：", "主控：")
                .Replace("（不影响工程发布）", "");
            _guidedDevice.Text = (_scanner?.IsConnected == true
                ? "扫码枪：已连接 " + _scanner.ConnectedPort
                : "扫码枪：未连接") + "   ·   " + controller;
        }

        try
        {
            var deployed = await StationDeploymentStore.RestoreAsync();
            var published = await StationProductionConfigurationStore.LoadPublishedAsync();
            var projectApplied = deployed is not null && current is not null &&
                deployed.Project.Definition.Id == current.Definition.Id &&
                deployed.Project.ManifestSha256 == current.ManifestSha256;
            var matching = published?.Projects.FirstOrDefault(x => current is not null &&
                x.ProjectId == current.Definition.Id.Value &&
                x.ManifestSha256 == current.ManifestSha256);
            if (_guidedPublication is not null)
                _guidedPublication.Text = projectApplied && matching is not null
                    ? "当前工程已应用 · " + published!.PublicationId.ToString("N")[..10]
                    : "当前工程尚未应用到工位";
        }
        catch (Exception error)
        {
            if (_guidedPublication is not null)
                _guidedPublication.Text = "应用版本无法读取 · " + error.Message;
        }

        try
        {
            var probe = BuildDraft();
            if (current is not null &&
                probe.Mode == StationProductionConfiguration.FixedStation &&
                probe.Products.Count == 0)
                probe = probe with
                {
                    FixedProjectId = current.Definition.Id.Value,
                    Products = [new ProductionProductBinding(
                        "", "", current.ManifestPath, current.Definition.Id.Value)]
                };
            var issues = StationProductionConfigurationStore.InspectRules(probe);
            if (_guidedBlocker is not null)
                _guidedBlocker.Text = (issues.Count == 0
                    ? "配置规则正常" : "配置有 " + issues.Count + " 项问题") +
                    " · 正式生产尚未接通实体启动按钮和正式检测记录";
        }
        catch (Exception error)
        {
            if (_guidedBlocker is not null)
                _guidedBlocker.Text = "配置检查异常：" + error.Message;
        }
    }

}
