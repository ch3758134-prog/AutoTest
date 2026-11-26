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
            if (!_scanner.IsConnected && _guidedScan is not null)
                _guidedScan.Text = "扫码枪未连接；仍然可以离线发布工程。真实生产扫码需重新连接已绑定设备。";
            ConnectionHint = _scanner.IsConnected
                ? "扫码枪：" + _scanner.ConnectedPort + " · 不影响工程发布"
                : "扫码枪未连接 · 可离线发布工程";
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
            Margin = new Thickness(0, 3, 0, 6)
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
            Padding = new Thickness(17, 14, 17, 14),
            Margin = new Thickness(0, 0, 0, 12),
            Child = content
        };
    }

    private static Button GuidedButton(string name, bool primary = false)
    {
        var button = new Button
        {
            Content = name,
            Padding = new Thickness(14, 8, 14, 8),
            Margin = new Thickness(0, 6, 10, 0),
            MinHeight = 34,
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

    internal UIElement CreateGuidedOverview()
    {
        var root = new StackPanel
        {
            Margin = new Thickness(22, 17, 22, 17),
            MaxWidth = 980
        };
        root.Children.Add(new TextBlock
        {
            Text = "工位设置",
            Foreground = GuidedInk,
            FontSize = 21,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 5)
        });
        root.Children.Add(GuidedText(
            "首次安装或更换 ECU 型号时设置一次。平时直接扫码检测，无需进入这里。"));

        root.Children.Add(GuidedCard("当前检测工程", out var projectBody));
        _guidedProject = GuidedText("正在检查 ECU 工程…");
        projectBody.Children.Add(_guidedProject);
        var projectActions = new WrapPanel();
        var projectIssues = GuidedButton("工程检查详情");
        projectIssues.Click += (_, _) => ShowGuidedProjectDiagnostics();
        projectActions.Children.Add(projectIssues);
        projectBody.Children.Add(projectActions);

        root.Children.Add(GuidedCard("扫码枪与设备", out var deviceBody));
        _guidedDevice = GuidedText("正在读取设备状态…");
        deviceBody.Children.Add(_guidedDevice);
        _guidedScan = GuidedText("尚未收到扫码；扫码成功后直接显示原文。");
        deviceBody.Children.Add(_guidedScan);
        var connect = GuidedButton("连接扫码枪");
        connect.Click += (_, _) => OpenStationDetail(0);
        deviceBody.Children.Add(connect);
        // Detailed physical interlocks live under hardware settings; the
        // first-use page focuses on the actual task, not protocol prose.

        root.Children.Add(GuidedCard("自动夹具", out var fixtureBody));
        _guidedFixtureEnabled = new CheckBox
        {
            Content = "自动控制夹具",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = GuidedInk,
            Margin = new Thickness(0, 7, 0, 8),
            MinHeight = 28
        };
        _guidedFixtureEnabled.Checked += (_, _) => OnGuidedFixtureEnabledChanged();
        _guidedFixtureEnabled.Unchecked += (_, _) => OnGuidedFixtureEnabledChanged();
        fixtureBody.Children.Add(_guidedFixtureEnabled);
        var process = new WrapPanel { Margin = new Thickness(0, 2, 0, 7) };
        foreach (var item in new[]
        {
            "检测前压紧", "  →  ", "自动烧录与完整 TEST", "  →  ", "正常完成后释放"
        })
            process.Children.Add(new TextBlock
            {
                Text = item,
                FontSize = 13.5,
                FontWeight = item.Contains("→") ? FontWeights.Normal :
                    FontWeights.SemiBold,
                Foreground = item.Contains("→") ? GuidedMuted : GuidedInk,
                Margin = new Thickness(0, 3, 0, 3)
            });
        fixtureBody.Children.Add(process);
        _guidedFixturePlan = GuidedText("尚未加载工位夹具设置。");
        fixtureBody.Children.Add(_guidedFixturePlan);
        var settings = GuidedButton("夹具硬件设置");
        settings.Click += (_, _) => OpenStationDetail(0);
        fixtureBody.Children.Add(settings);
        fixtureBody.Children.Add(GuidedText(
            "这里不会立即执行夹具动作；异常中断后须现场确认机械状态。"));

        root.Children.Add(GuidedCard("正式生产使用", out var releaseBody));
        _guidedPublication = GuidedText("正在检查发布记录…");
        releaseBody.Children.Add(_guidedPublication);
        _guidedBlocker = GuidedText("正式生产准入还需要实体 START、夹具反馈和 Inspection 记录闭环。");
        releaseBody.Children.Add(_guidedBlocker);
        var publishActions = new WrapPanel();
        var publish = GuidedButton("保存并设为当前生产工程", primary: true);
        publish.Click += async (_, _) => await GuidedPublishAsync();
        publishActions.Children.Add(publish);
        var advanced = GuidedButton("高级型号/编号规则");
        advanced.Click += (_, _) => OpenStationDetail(1);
        publishActions.Children.Add(advanced);
        var details = GuidedButton("核验详情");
        details.Click += (_, _) => OpenStationDetail(2);
        publishActions.Children.Add(details);
        releaseBody.Children.Add(publishActions);
        releaseBody.Children.Add(GuidedText(
            "只需在首次使用或工程变更后确认一次。后台核对 TEST、FLASH、DBC、波形版本；" +
            "不会立即控制夹具或烧录 ECU。"));

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
                0 => "未启用：检测不会自动压紧或释放。",
                1 => "检测前压紧，正常完成并保存结果后释放。" +
                     "通道和电平已保存在硬件设置中。",
                _ => "这套夹具使用两路输出，按硬件设置中的顺序压紧、逆序释放。"
            };
        }
        catch (Exception)
        {
            _guidedFixturePlan.Text = "夹具硬件设置未完成：请在硬件设置中确认两路顺序、" +
                "输出极性及稳定时间，完成前禁止启动。";
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
        if (_guidedScan is null || !_ready) return Task.CompletedTask;
        var source = actualDevice ? "真实 COM 输入" : "手工试算";
        if (string.IsNullOrWhiteSpace(raw))
        {
            _guidedScan.Text = source + "：没有有效编号。";
            return Task.CompletedTask;
        }
        try
        {
            // Preview the barcode parser only. A scanner read must not
            // freeze FLASH/TEST assets, publish an ECU project or claim an
            // authorized production identity/START.
            var config = BuildDraft();
            var matches = config.Rules.Where(x => x.Enabled)
                .Select(x => (Rule: x, Parsed: x.Parse(raw)))
                .Where(x => x.Parsed.Accepted).ToArray();
            if (matches.Length == 0)
            {
                _guidedScan.Text = source + "：" + raw +
                    "\n编号识别失败。请核对编码规则；工位发布与扫码验证相互独立。";
                return Task.CompletedTask;
            }
            var unique = matches.Select(x =>
                x.Parsed.Serial + "|" + x.Parsed.Model + "|" +
                x.Parsed.Hardware + "|" + x.Parsed.Batch)
                .Distinct(StringComparer.Ordinal).Count();
            if (unique != 1)
            {
                _guidedScan.Text = source + "：" + raw +
                    "\n不同规则解析出不同身份，禁止自动选择。请在高级识别配置中消除冲突。";
                return Task.CompletedTask;
            }
            var decoded = matches[0].Parsed;
            _guidedScan.Text = source + "：" + raw +
                "\n解析出的 ECU 唯一编号：" + decoded.Serial +
                (config.Mode == StationProductionConfiguration.FixedStation
                    ? "\n固定型号来自工程/已发布工位配置；此处仅验证编号格式。"
                    : "\n复杂型号映射仍须单独核对已发布的身份表和唯一工程。") +
                "\n此结果不是生产身份放行，也不影响当前工程能否发布。";
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            _guidedScan.Text = source + "：识别检查失败（" +
                error.GetType().Name + "）；请检查高级编码规则。";
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
        if (!_ready || !_mayEdit() || _busy)
        {
            Status = "工位配置尚未就绪，或正在进行真实硬件操作，不能发布。";
            return;
        }
        var project = _workspace.Current;
        if (project is null || string.IsNullOrWhiteSpace(project.Definition.EcuModelCode))
        {
            Status = "请先在开发与调试打开并保存具有正式型号的 ECU 工程。";
            return;
        }
        // Fixed-station commissioning can infer its single product from the
        // opened engineering project. Multi-model setups retain the complete
        // user-edited mapping; they use this SAME apply action and do not
        // require a separate hidden publishing workflow.
        var model = project.Definition.EcuModelCode;
        if (IdentityMode == StationProductionConfiguration.FixedStation)
        {
            if (Products.Count == 0)
            {
                FixedModel = model;
                Products.Add(new ProductRow
                {
                    ModelCode = model, HardwareCode = FixedHardware,
                    ManifestPath = project.ManifestPath
                });
                MarkDirty();
            }
            else if (Products.Count == 1 &&
                (FixedModel != model ||
                 Products[0].ModelCode != model ||
                 Products[0].HardwareCode != FixedHardware ||
                 !string.Equals(Products[0].ManifestPath,
                     project.ManifestPath, StringComparison.OrdinalIgnoreCase)))
            {
                Status = "本工位已配置另一型号工程。请在本页“产品与 ECU 工程”核对后应用；" +
                    "不会自动覆盖原型号和已发布版本。";
                OpenStationDetail(-1);
                return;
            }
        }
        if (Products.Count == 0)
        {
            Status = "尚未配置型号到工程的关系，请先在本页“产品与 ECU 工程”添加绑定。";
            return;
        }

        StationPointerSnapshot[]? snapshots = null;
        try
        {
            // Early R1 created an identical fake two-row sample set on every
            // new station. It is not real scanned evidence and must never be
            // required for the deterministic entire-string parser.
            if (Rules.Count == 1 &&
                Rules[0].Enabled &&
                Rules[0].Format == ProductionBarcodeRule.Serial &&
                Rules[0].Prefix.Length == 0 &&
                Rules[0].ExactLength == 0 &&
                Rules[0].AllowedCharacters == @"[A-Za-z0-9_.-]+" &&
                Rules[0].Samples.Count == 2 &&
                Rules[0].Samples.Any(x => x.Raw == "SN001234" &&
                    x.ShouldAccept && x.ExpectedSerial == "SN001234") &&
                Rules[0].Samples.Any(x => x.Raw == "SN|BAD" && !x.ShouldAccept))
            {
                // These two rows were generated by older AutoTest itself,
                // not measured on the station. Drop only this exact legacy
                // pair automatically; no pointless modal during release.
                Rules[0].Samples.Clear();
                MarkDirty();
            }
            if (!await SaveAsync()) return;
            var config = await StationProductionConfigurationStore.LoadDraftAsync();
            var issues = StationProductionConfigurationStore.InspectRules(config);
            if (issues.Count > 0)
                throw new InvalidDataException("发布预检未通过：" +
                    string.Join("；", issues.Take(3)) +
                    "。复杂编码规则请在高级配置中核对。");
            if (_publishProject is null)
                throw new InvalidOperationException("原生 ECU 工程发布入口未连接。");

            // An unchanged, still-valid complete station release is already
            // applied. Clicking again must not create new publication IDs or
            // invalidate the scanner's accepted identity.
            var oldPublication = await StationProductionConfigurationStore.LoadPublishedAsync();
            if (oldPublication is not null &&
                string.Equals(oldPublication.ConfigurationSha256,
                    StationProductionConfigurationStore.ConfigurationSha(config),
                    StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var station = await StationDeploymentStore.RestoreAsync();
                    var matching = oldPublication.Projects.SingleOrDefault(x =>
                        string.Equals(x.ManifestPath, project.ManifestPath,
                            StringComparison.OrdinalIgnoreCase));
                    if (matching is not null && station is not null &&
                        station.Project.Definition.Id == project.Definition.Id &&
                        station.Project.ManifestSha256 == project.ManifestSha256)
                    {
                        await StationProductionConfigurationStore.VerifyProductAsync(matching);
                        Status = "本套生产配置已经应用到工位，工程、固件和 TEST 均未变化；无需重复发布。";
                        await RefreshOverviewAsync();
                        return;
                    }
                }
                catch (Exception error) when (error is IOException or
                    InvalidDataException or UnauthorizedAccessException or
                    FileNotFoundException or ArgumentException)
                {
                    // The prior release no longer verifies. A fresh complete
                    // preflight follows; never claim that stale assets pass.
                    Status = "已有发布版本已失效，重新核对当前完整配置：" + error.Message;
                }
            }

            // Config is immutable during the single apply operation.
            // Scanner discovery does not gate offline release.
            var generation = _editGeneration;
            _busy = true;
            snapshots = await CaptureStationPointersAsync(project.RootDirectory);
            var projectAccepted = await _publishProject();
            if (!projectAccepted)
            {
                await RestoreStationPointersAsync(snapshots);
                snapshots = null;
                Status = "已取消应用；旧版工位配置保持不变。";
                return;
            }
            var deployed = await StationDeploymentStore.RestoreAsync()
                ?? throw new InvalidDataException("已发布工程的工位部署记录不存在。");
            if (deployed.Project.Definition.Id != project.Definition.Id ||
                !string.Equals(deployed.Project.ManifestSha256, project.ManifestSha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("工程版本在应用过程中发生变化。请重新打开工程核对。");
            if (_editGeneration != generation ||
                _workspace.Current?.ManifestSha256 != project.ManifestSha256)
                throw new IOException("应用过程中工程或工位配置被修改，已阻止应用。");
            // Last pointer commits the station identity, full TEST and FLASH
            // snapshot; no partial change is represented as successful.
            var release = await StationProductionConfigurationStore.PublishAsync(config);
            snapshots = null; // Successfully applied; do not undo on a UI refresh error.
            _onPublished?.Invoke();
            Status = "已一次性应用整套生产配置 · " +
                release.PublicationId.ToString("N")[..10] +
                "；FLASH、完整 TEST、扫码型号和工位规则已核对。" +
                " 实体 START、夹具安全条件及正式 Inspection 仍需实机验收。";
            await RefreshOverviewAsync();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (snapshots is not null)
            {
                try
                {
                    await RestoreStationPointersAsync(snapshots);
                    Status = "生产配置应用未完成，已恢复之前的工位发布版本：" + error.Message;
                }
                catch (Exception rollback) when (rollback is not OutOfMemoryException)
                {
                    Status = "生产配置应用失败且恢复旧版本未完成。必须停止正式生产，" +
                        "检查工位发布文件后才能继续：" + error.Message +
                        "；恢复失败：" + rollback.Message;
                }
            }
            else
                Status = "生产配置检查或界面更新失败：" + error.Message;
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
            _guidedProject.Text = "尚未打开 ECU 工程；请先在开发与调试中打开并保存。";
            _guidedProjectDiagnostics = "没有当前工程，无法执行资源检查。";
        }
        else
        {
            _guidedProject.Text = "型号 " + current.Definition.EcuModelCode +
                " · " + current.Definition.Name +
                "\n完整工程：" + Path.GetFileName(current.ManifestPath);
            _guidedProjectDiagnostics = "当前工程：" + current.ManifestPath;
            try
            {
                var snapshot = await AutomaticTestResourceContract.ResolveAsync(current);
                var flash = await NativeFlashProfileStore.LoadAsync(current.RootDirectory);
                if (_workspace.Current?.ManifestSha256 != current.ManifestSha256) return;
                var errors = snapshot.Issues.ToArray();
                _guidedProject.Text += "\nTEST：" + snapshot.Cases.Count +
                    " 个用例" + (snapshot.ProjectSetup is null ? "" : " · 含项目准备") +
                    (snapshot.ProjectTeardown is null ? "" : " · 含项目收尾") +
                    (snapshot.CanExecute ? " · 资源检查通过" :
                        " · 资源引用异常 " + errors.Length + " 项（查看工程问题）") +
                    "\nFLASH：" + (flash is null
                        ? "尚未保存原生烧录工艺"
                        : "工艺草稿已保存 · 发布时严格核对文件及 SHA");
                _guidedProjectDiagnostics = snapshot.CanExecute
                    ? "TEST 资源闭包检查通过。" +
                      "\n用例数：" + snapshot.Cases.Count +
                      "\n仍需发布原生 FLASH 并完成生产工位版本冻结。"
                    : "TEST 资源引用检查未通过，需回到开发与调试修正，不允许静默替换：" +
                      "\n\n" + string.Join("\n\n", errors.Take(40));
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                _guidedProject.Text += "\nTEST/FLASH：工程资源检查失败（查看工程问题）";
                _guidedProjectDiagnostics = error.ToString();
            }
        }

        if (_guidedDevice is not null)
            _guidedDevice.Text = (_stationDeviceStatus?.Invoke() ??
                    "主控与 ECU CAN：在主界面查看真实连接/回读状态") +
                "\n扫码枪：" + (_scanner?.IsConnected == true
                    ? "已连接 " + _scanner.ConnectedPort
                    : "未连接（工程发布不受影响）") +
                "\n夹具、START、互锁：当前尚无可信实测反馈；正式生产保持阻断。";
        try
        {
            var deployment = await StationDeploymentStore.RestoreAsync();
            var published = await StationProductionConfigurationStore.LoadPublishedAsync();
            var deployed = deployment is not null && current is not null &&
                deployment.Project.Definition.Id == current.Definition.Id &&
                deployment.Project.ManifestSha256 == current.ManifestSha256;
            var station = published?.Projects.FirstOrDefault(x => current is not null &&
                x.ProjectId == current.Definition.Id.Value &&
                x.ManifestSha256 == current.ManifestSha256);
            if (_guidedPublication is not null)
                _guidedPublication.Text = "当前 ECU 工程：" +
                    (deployed ? "已冻结部署" : "尚未完成当前版本的工程部署") +
                    "\n工位产品与编码发布：" +
                    (station is null ? "当前版本尚未发布" :
                        "已发布 " + published!.PublicationId.ToString("N")[..10] +
                        " · TEST " + station.OrderedCaseNames.Count + " 个") +
                    "\n配置编辑不会自动改变已发布生产版本。";
        }
        catch (Exception error)
        {
            if (_guidedPublication is not null)
                _guidedPublication.Text = "发布版本尚未验证：" + error.Message;
        }
        try
        {
            var probe = BuildDraft();
            // Publishing will bind the single currently open ECU project; the
            // first-use summary should evaluate that exact candidate without
            // silently modifying the saved draft just to paint the UI.
            if (current is not null &&
                probe.Mode == StationProductionConfiguration.FixedStation &&
                probe.Products.Count == 0)
                probe = probe with
                {
                    FixedModel = current.Definition.EcuModelCode,
                    Products = [new ProductionProductBinding(
                        current.Definition.EcuModelCode, probe.FixedHardware,
                        current.ManifestPath)]
                };
            var problems = StationProductionConfigurationStore.InspectRules(probe);
            if (_guidedBlocker is not null)
                _guidedBlocker.Text = (problems.Count == 0
                        ? "编码规则：发布静态检查通过（不代表真扫码已验证）。"
                        : "配置待核对：" + problems.Count +
                          " 项。具体原因见“发布详情”。") +
                    "\n生产运行仍阻断：实体 START、夹具反馈/互锁与正式 Inspection 写入未接通。";
        }
        catch (Exception error)
        {
            if (_guidedBlocker is not null)
                _guidedBlocker.Text = "配置预检暂未通过，具体原因见发布详情。" +
                    "\n正式生产 START / Inspection 仍未接通。";
        }
    }
}
