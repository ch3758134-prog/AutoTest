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
    private ComboBox? _guidedClampMode;
    private StackPanel? _guidedDualOrder;
    private bool _syncingGuidedClampMode;

    private sealed record GuidedClampOption(int Value, string Label);
    private static readonly GuidedClampOption[] GuidedClampOptions =
    [
        new(0, "不使用夹具"),
        new(1, "夹具 1"),
        new(2, "夹具 2"),
        new(3, "双路夹具")
    ];

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
            Text = "生产准备",
            Foreground = GuidedInk,
            FontSize = 21,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 5)
        });
        root.Children.Add(GuidedText(
            "工程在开发区编辑；这里确认工程版本、工位设备，然后发布。扫码枪离线不影响工程发布。"));

        root.Children.Add(GuidedCard("当前产品工程", out var projectBody));
        _guidedProject = GuidedText("正在检查 ECU 工程…");
        projectBody.Children.Add(_guidedProject);
        var projectActions = new WrapPanel();
        var projectIssues = GuidedButton("查看工程问题");
        projectIssues.Click += (_, _) => ShowGuidedProjectDiagnostics();
        projectActions.Children.Add(projectIssues);
        projectBody.Children.Add(projectActions);

        root.Children.Add(GuidedCard("工位设备", out var deviceBody));
        _guidedDevice = GuidedText("正在读取设备状态…");
        deviceBody.Children.Add(_guidedDevice);
        _guidedScan = GuidedText("尚未扫码。设备试扫用于连接验收，不是工程发布的前置条件。");
        deviceBody.Children.Add(_guidedScan);
        var connect = GuidedButton("连接和验证扫码枪");
        connect.Click += (_, _) => PreparationTabs.SelectedIndex = 1;
        deviceBody.Children.Add(connect);
        deviceBody.Children.Add(GuidedText(
            "夹具反馈、实体 START 和互锁必须来自可验证的硬件输入；填写来源名称不能代替接线或到位确认。"));

        root.Children.Add(GuidedCard("自动夹具", out var fixtureBody));
        var fixtureSelect = new WrapPanel { Margin = new Thickness(0, 2, 0, 8) };
        fixtureSelect.Children.Add(new TextBlock
        {
            Text = "使用夹具",
            Foreground = GuidedMuted,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        });
        _guidedClampMode = new ComboBox
        {
            Width = 195, MinHeight = 33,
            ItemsSource = GuidedClampOptions,
            DisplayMemberPath = nameof(GuidedClampOption.Label),
            SelectedValuePath = nameof(GuidedClampOption.Value)
        };
        _guidedClampMode.SelectionChanged += (_, _) => OnGuidedClampModeSelected();
        fixtureSelect.Children.Add(_guidedClampMode);
        fixtureBody.Children.Add(fixtureSelect);

        var process = new WrapPanel { Margin = new Thickness(0, 3, 0, 10) };
        var processSteps = new[]
        {
            "检测前：压紧", " → ", "自动烧录 + TEST", " → ", "正常完成后：释放"
        };
        for (var i = 0; i < processSteps.Length; i++)
        {
            process.Children.Add(new TextBlock
            {
                Text = processSteps[i],
                FontSize = 13,
                FontWeight = i % 2 == 0 ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = i % 2 == 0 ? GuidedInk : GuidedMuted,
                Margin = new Thickness(0, 3, 0, 3)
            });
        }
        fixtureBody.Children.Add(process);
        _guidedDualOrder = new StackPanel { Visibility = Visibility.Collapsed };
        var orderRow = new WrapPanel { Margin = new Thickness(0, 2, 0, 8) };
        orderRow.Children.Add(new TextBlock
        {
            Text = "两路夹具先后顺序",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = GuidedMuted,
            Margin = new Thickness(0, 0, 12, 0)
        });
        var order = new ComboBox
        {
            Width = 225, MinHeight = 32,
            ItemsSource = ClampOrderChoices,
            DisplayMemberPath = nameof(ClampOrderChoice.Label),
            SelectedValuePath = nameof(ClampOrderChoice.Value)
        };
        order.SetBinding(Selector.SelectedValueProperty,
            new Binding("Fixture.FirstClamp")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
        order.SelectionChanged += (_, _) => OnGuidedFixtureChanged();
        orderRow.Children.Add(order);
        _guidedDualOrder.Children.Add(orderRow);
        fixtureBody.Children.Add(_guidedDualOrder);

        _guidedFixturePlan = GuidedText("正在读取夹具设置…");
        fixtureBody.Children.Add(_guidedFixturePlan);
        var settings = GuidedButton("输出接线、超时与反馈设置");
        settings.Click += (_, _) => PreparationTabs.SelectedIndex = 1;
        fixtureBody.Children.Add(settings);
        fixtureBody.Children.Add(GuidedText(
            "配置页面不会驱动夹具。仅在真实整组执行时才按已发布配置发出压紧/释放指令；" +
            "异常停止不自动释放。输出 GPIO 回读不等于机械到位。"));

        root.Children.Add(GuidedCard("发布与生产准入", out var releaseBody));
        _guidedPublication = GuidedText("正在检查发布记录…");
        releaseBody.Children.Add(_guidedPublication);
        _guidedBlocker = GuidedText("正式生产准入还需要实体 START、夹具反馈和 Inspection 记录闭环。");
        releaseBody.Children.Add(_guidedBlocker);
        var publishActions = new WrapPanel();
        var publish = GuidedButton("核对并发布当前工程", primary: true);
        publish.Click += async (_, _) => await GuidedPublishAsync();
        publishActions.Children.Add(publish);
        var advanced = GuidedButton("高级配置 / 多型号");
        advanced.Click += (_, _) => PreparationTabs.SelectedIndex = 2;
        publishActions.Children.Add(advanced);
        var details = GuidedButton("发布详情");
        details.Click += (_, _) => PreparationTabs.SelectedIndex = 3;
        publishActions.Children.Add(details);
        releaseBody.Children.Add(publishActions);
        releaseBody.Children.Add(GuidedText(
            "发布只冻结当前工程的 FLASH、TEST、资源以及单型号工位识别配置；" +
            "不会操作夹具、启动烧录、运行 TEST，也不代表允许正式生产放行。"));

        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
            Content = root
        };
    }

    private void OnGuidedClampModeSelected()
    {
        if (_guidedClampMode is null || _syncingGuidedClampMode || !_ready) return;
        if (!_mayEdit() || _busy)
        {
            SyncGuidedClampMode();
            Status = "工位运行中或配置锁定，不能改变夹具选择。";
            return;
        }
        if (_guidedClampMode.SelectedValue is not int choice || choice is < 0 or > 3)
            return;
        var first = choice is 1 or 3;
        var second = choice is 2 or 3;
        if (Fixture.UseClamp1 == first && Fixture.UseClamp2 == second)
        {
            UpdateGuidedFixtureText();
            return;
        }
        var wasDual = Fixture.UseClamp1 && Fixture.UseClamp2;
        Fixture.UseClamp1 = first;
        Fixture.UseClamp2 = second;
        if (choice == 3 && !wasDual)
            Fixture.FirstClamp = 0; // never guess which valve should move first
        MarkDirty();
        UpdateGuidedFixtureText();
    }

    private void SyncGuidedClampMode()
    {
        if (_guidedClampMode is null) return;
        var mode = (Fixture.UseClamp1 ? 1 : 0) |
                   (Fixture.UseClamp2 ? 2 : 0);
        _syncingGuidedClampMode = true;
        try { _guidedClampMode.SelectedValue = mode; }
        finally { _syncingGuidedClampMode = false; }
        if (_guidedDualOrder is not null)
            _guidedDualOrder.Visibility = mode == 3
                ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnGuidedFixtureChanged()
    {
        if (!_ready || _syncingGuidedClampMode) return;
        if (!_mayEdit() || _busy)
        {
            Status = "当前工位正在执行或配置锁定，不能修改夹具设置。";
            return;
        }
        MarkDirty();
        UpdateGuidedFixtureText();
    }

    private void UpdateGuidedFixtureText()
    {
        SyncGuidedClampMode();
        if (_guidedFixturePlan is null) return;
        try
        {
            var plan = StationFixtureExecutionPlan.From(Fixture.ToModel());
            _guidedFixturePlan.Text = plan.Press.Count switch
            {
                0 => "未使用自动夹具，测试前后都不会发送夹具动作。",
                1 => "检测前压紧夹具 " + plan.Press[0].Clamp +
                     "；检测正常完成且记录保存后释放夹具 " +
                     plan.Release[0].Clamp + "。",
                _ => "压紧：" + string.Join(" → ",
                        plan.Press.Select(x => "夹具 " + x.Clamp)) +
                     "；释放：" + string.Join(" → ",
                        plan.Release.Select(x => "夹具 " + x.Clamp)) + "。"
            };
        }
        catch (Exception)
        {
            _guidedFixturePlan.Text = "双路夹具需要选择先压紧哪一路；" +
                "未选定前不能发布执行。";
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
        // The ordinary path publishes the selected engineering project.
        // No user-maintained duplicate model/manifest binding is required.
        // Existing advanced multi-model relations are never erased silently.
        if (IdentityMode != StationProductionConfiguration.FixedStation ||
            Products.Count > 1)
        {
            Status = "当前使用多型号或高级身份配置；请在高级配置核对型号映射，" +
                "然后通过发布详情的受控流程处理。不会自动覆盖现有型号关系。";
            return;
        }
        var model = project.Definition.EcuModelCode;
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
        else if (FixedModel != model ||
            Products[0].ModelCode != model ||
            Products[0].HardwareCode != FixedHardware ||
            !string.Equals(Products[0].ManifestPath, project.ManifestPath,
                StringComparison.OrdinalIgnoreCase))
        {
            Status = "已保存的工位工程绑定与当前 ECU 工程不一致。为防错型号，" +
                "不会静默替换；请进入高级配置核对并明确调整。";
            return;
        }

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
                var clean = MessageBox.Show(this,
                    "当前还保留旧版软件自动填入的两条演示编号，" +
                    "它们不是现场扫码证据。\n\n是否清除这两条演示记录，" +
                    "按“整串作为 ECU 唯一编号”的默认规则发布？" +
                    "\n这不会修改已发布版本，也不会操作硬件。",
                    "清理旧演示扫码配置", MessageBoxButton.YesNo,
                    MessageBoxImage.Question, MessageBoxResult.No);
                if (clean != MessageBoxResult.Yes)
                {
                    Status = "已保留旧演示配置；此次未发布。";
                    return;
                }
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

            // Saving and publishing are different actions. A real scanner is
            // deliberately NOT required to publish this offline project.
            // Runtime still enforces the exact bound COM/PnP observation.
            var generation = _editGeneration;
            _busy = true;
            var projectAccepted = await _publishProject();
            if (!projectAccepted)
            {
                Status = "已取消 ECU 工程发布；工位识别配置未发布。";
                return;
            }
            var deployed = await StationDeploymentStore.RestoreAsync()
                ?? throw new InvalidDataException("已发布工程的工位部署记录不存在。");
            if (deployed.Project.Definition.Id != project.Definition.Id ||
                !string.Equals(deployed.Project.ManifestSha256, project.ManifestSha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("工程版本在发布过程中发生变化。请重新打开工程核对。");
            if (_editGeneration != generation ||
                _workspace.Current?.ManifestSha256 != project.ManifestSha256)
                throw new IOException("发布过程中工程或工位配置被修改，已阻止发布工位映射。");
            // The station publication still re-verifies every TEST/DBC/WAVE
            // and immutable FLASH SHA. Offline publication != physical scan.
            var release = await StationProductionConfigurationStore.PublishAsync(config);
            _onPublished?.Invoke();
            Status = "生产配置发布成功 · " +
                release.PublicationId.ToString("N")[..10] +
                "；工程、原生 FLASH、TEST 与型号绑定已冻结。" +
                "扫码枪及实体 START/夹具/Inspection 仍须在运行前验收。";
            await RefreshOverviewAsync();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Status = "生产配置没有完整发布：" + error.Message +
                "；如果工程快照已单独更新，也不代表工位正式准入成功。";
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
