using System.IO;
using System.Windows;
using System.Windows.Controls;
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
    private TextBox? _guidedManualInput;
    private string? _lastRealBarcode;
    private string _lastRealBarcodePort = "";
    private string _lastRealBarcodePnp = "";
    private DateTimeOffset? _lastRealBarcodeUtc;
    private int _guidedPreviewSequence;

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
            if (!_scanner.IsConnected ||
                (_lastRealBarcode is not null &&
                 !string.Equals(_lastRealBarcodePort, _scanner.ConnectedPort,
                     StringComparison.OrdinalIgnoreCase)))
            {
                _lastRealBarcode = null;
                _lastRealBarcodePort = "";
                _lastRealBarcodePnp = "";
                _lastRealBarcodeUtc = null;
                if (_guidedScan is not null)
                    _guidedScan.Text = "扫码设备已断开或串口会话发生变化，旧实扫凭证失效。";
            }
            ConnectionHint = _scanner.IsConnected
                ? "真实扫码设备：" + _scanner.ConnectedPort + " · 仅配置验证"
                : "扫码枪未连接；手动输入不能用于真实扫码发布凭证";
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

    private string _guidedProjectDiagnostics = "";

    private void ShowGuidedProjectDiagnostics()
    {
        var message = string.IsNullOrWhiteSpace(_guidedProjectDiagnostics)
            ? "当前没有可用的工程检查详情；请打开 ECU 工程并刷新生产准备。"
            : _guidedProjectDiagnostics;
        MessageBox.Show(this, message.Length > 10000 ? message[..10000] : message,
            "ECU 工程资源核查（仅诊断）", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void GuidedUseCurrentProject()
    {
        if (!_ready || !_mayEdit())
        {
            Status = "正在执行工位任务或生产配置尚未加载，不允许更换绑定。";
            return;
        }
        var project = _workspace.Current;
        if (project is null || string.IsNullOrWhiteSpace(project.Definition.EcuModelCode))
        {
            Status = "请先在开发与调试打开并保存完整 ECU 工程。";
            return;
        }
        if (Products.Count > 1)
        {
            Status = "当前存在多型号工程绑定，不能自动替换。请进入高级配置审查现有产品关系。";
            PreparationTabs.SelectedIndex = 2;
            return;
        }
        if (IdentityMode == StationProductionConfiguration.FixedStation &&
            FixedModel == project.Definition.EcuModelCode &&
            Products.Count == 1 &&
            Products[0].ModelCode == project.Definition.EcuModelCode &&
            Products[0].HardwareCode == FixedHardware &&
            string.Equals(Products[0].ManifestPath, project.ManifestPath,
                StringComparison.OrdinalIgnoreCase))
        {
            Status = "当前 ECU 工程已绑定，无需重复保存或重新增加发布修订。";
            return;
        }
        if (IdentityMode != StationProductionConfiguration.FixedStation)
        {
            var consent = MessageBox.Show(this,
                "目前使用了条码型号/编号登记的高级识别模式。\n" +
                "确定改为固定型号工位吗？现有高级规则和编号表将保留为草稿，但固定工位不再使用它们决定型号。",
                "切换为单型号工位", MessageBoxButton.YesNo, MessageBoxImage.Question,
                MessageBoxResult.No);
            if (consent != MessageBoxResult.Yes) return;
            IdentityMode = StationProductionConfiguration.FixedStation;
        }
        var code = project.Definition.EcuModelCode;
        var hardware = FixedHardware;
        if (Products.Count == 1)
        {
            var existing = Products[0];
            if (existing.ModelCode != code ||
                existing.HardwareCode != hardware ||
                !string.Equals(existing.ManifestPath, project.ManifestPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                var replace = MessageBox.Show(this,
                    "当前仅有一条绑定，但它指向另一份型号/硬件版本/工程。\n" +
                    "确定用已打开的工程替换该草稿绑定吗？旧正式发布不会自动变更，必须重新核对并发布。",
                    "明确替换当前工位工程", MessageBoxButton.YesNo,
                    MessageBoxImage.Warning, MessageBoxResult.No);
                if (replace != MessageBoxResult.Yes) return;
                Products.Clear();
            }
        }
        FixedModel = code;
        if (Products.Count == 0)
            Products.Add(new ProductRow
            {
                ModelCode = code, HardwareCode = hardware,
                ManifestPath = project.ManifestPath
            });
        MarkDirty();
        Status = "已从当前 ECU 工程读取型号并建立单型号绑定；还需真实扫码验证和发布。";
        _ = RefreshGuidedOverviewAsync();
    }

    private void GuidedAdoptRealSample()
    {
        if (!_ready || !_mayEdit())
        {
            Status = "工位配置不可编辑，未改变任何校验样例。";
            return;
        }
        if (string.IsNullOrEmpty(_lastRealBarcode) ||
            _scanner?.IsConnected != true ||
            _lastRealBarcodePort != _scanner.ConnectedPort)
        {
            Status = "请先连接已绑定的真实扫码枪并扫码。手工试算不能登记为现场正例。";
            return;
        }
        if (string.IsNullOrWhiteSpace(_lastRealBarcodePnp))
        {
            Status = "扫码枪尚未按 USB 设备身份完成绑定。请先“记住当前扫码枪”并重新扫码。";
            return;
        }
        if (Rules.Count != 1 || !Rules[0].Enabled ||
            Rules[0].Format != ProductionBarcodeRule.Serial)
        {
            Status = "当前是多规则/复杂条码解析，请在高级识别中逐条配置样例。";
            PreparationTabs.SelectedIndex = 2;
            return;
        }
        var row = Rules[0];
        var match = row.ToModel().Parse(_lastRealBarcode);
        if (!match.Accepted)
        {
            Status = "真实扫码内容不符合当前编码规则：" + match.Detail;
            return;
        }
        // Remove only the known legacy artificial two-sample starter set.
        if (row.Samples.Count == 2 &&
            row.Samples.Any(x => x.Raw == "SN001234" && x.ShouldAccept &&
                x.ExpectedSerial == "SN001234") &&
            row.Samples.Any(x => x.Raw == "SN|BAD" && !x.ShouldAccept))
            row.Samples.Clear();
        if (!row.Samples.Any(x => x.Raw == _lastRealBarcode && x.ShouldAccept))
            row.Samples.Add(new SampleRow
            {
                Raw = _lastRealBarcode,
                ShouldAccept = true,
                ExpectedSerial = match.Serial
            });
        // Explicitly generated parser rejection sample; NEVER a claim that
        // a real scanner sent this malformed barcode.
        var invalid = _lastRealBarcode + "|";
        if (invalid.Length > 256) invalid = "|";
        if (!row.Samples.Any(x => x.Raw == invalid && !x.ShouldAccept) &&
            !row.ToModel().Parse(invalid).Accepted)
            row.Samples.Add(new SampleRow { Raw = invalid, ShouldAccept = false });
        MarkDirty();
        Status = "本次真实扫码已登记为正例；非法字符拒绝反例由软件构造，" +
            "不代表设备曾扫描该错误条码。请核对解析结果并发布。";
        _ = RefreshGuidedOverviewAsync();
    }

    private async Task PreviewGuidedBarcodeAsync(string raw, bool actualDevice)
    {
        var current = ++_guidedPreviewSequence;
        if (_guidedScan is null || !_ready) return;
        if (string.IsNullOrWhiteSpace(raw))
        {
            _guidedScan.Text = "请输入完整编码，或连接扫码枪读取一条真实编号。";
            return;
        }
        _guidedScan.Text = (actualDevice ? "真实 COM 扫码" : "手动模拟输入") +
            "：" + raw + "；正在检查编号和已发布工程…";
        string parsedSerial = "";
        try
        {
            var draft = BuildDraft();
            var parsed = draft.Rules.Where(x => x.Enabled)
                .Select(x => (Rule: x, Parsed: x.Parse(raw)))
                .Where(x => x.Parsed.Accepted).ToArray();
            if (parsed.Length == 0)
            {
                _guidedScan.Text = (actualDevice ? "真实扫码" : "手动试算") +
                    "未匹配任何编码规则：" +
                    string.Join("；", draft.Rules.Where(x => x.Enabled).Take(3)
                        .Select(x => x.Name + "：" + x.Parse(raw).Code));
                return;
            }
            parsedSerial = parsed[0].Parsed.Serial;
            var identityPreview = parsed.Select(x =>
                x.Parsed.Serial + "/" + x.Parsed.Model + "/" +
                x.Parsed.Hardware + "/" + x.Parsed.Batch)
                .Distinct(StringComparer.Ordinal).ToArray();
            if (identityPreview.Length != 1)
            {
                _guidedScan.Text = "多条规则给出不同编号/型号，拒绝自动匹配。请进入高级识别核对。";
                return;
            }
            var result = await StationProductionConfigurationStore.PreviewDraftAsync(draft, raw);
            if (current != _guidedPreviewSequence) return;
            _guidedScan.Text = (actualDevice ? "真实 COM 扫码" : "手动模拟输入") +
                "：" + raw + "\n编号：" + parsed[0].Parsed.Serial +
                (result.Accepted
                    ? "\n型号：" + result.Model +
                      (string.IsNullOrEmpty(result.Hardware) ? "" : " / " + result.Hardware) +
                      "\n候选 ECU 工程：" + Path.GetFileName(result.Project!.ManifestPath) +
                      "；资源预览通过，但未取得生产启动授权。"
                    : "\n发布资源/型号匹配尚未完成：" + result.Detail);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or
            ArgumentException or InvalidOperationException or FormatException or
            System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            if (current == _guidedPreviewSequence)
                _guidedScan.Text = (actualDevice ? "真实扫码已收到" :
                    "手动试算已收到") + "：" + raw +
                    (parsedSerial.Length == 0 ? "" : "\n已解析唯一编号：" + parsedSerial) +
                    "\n尚未通过完整工程冻结检查：" + error.Message;
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
            var problems = StationProductionConfigurationStore.InspectRules(BuildDraft());
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
                _guidedBlocker.Text = "配置还不能发布：" + error.Message +
                    "\n正式生产 START / Inspection 仍未接通。";
        }
    }
}
