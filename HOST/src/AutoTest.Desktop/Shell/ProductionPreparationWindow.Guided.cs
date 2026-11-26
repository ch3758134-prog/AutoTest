using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AutoTest.Desktop.Projects;

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
            Margin = new Thickness(20, 18, 22, 15),
            MaxWidth = 970
        };
        root.Children.Add(new TextBlock
        {
            Text = "工位交付准备",
            Foreground = GuidedInk,
            FontSize = 21,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        });
        root.Children.Add(GuidedText(
            "按当前 ECU 工程 → 实际扫码验证 → 核对发布。默认适用于一个型号的工位；复杂编码和多型号匹配在高级配置中。"));

        root.Children.Add(GuidedCard("1  当前 ECU 工程", out var projectBody));
        _guidedProject = GuidedText("正在读取当前工程…");
        projectBody.Children.Add(_guidedProject);
        var projectActions = new WrapPanel();
        var useCurrent = GuidedButton("使用当前打开的工程", primary: true);
        useCurrent.Click += (_, _) => GuidedUseCurrentProject();
        projectActions.Children.Add(useCurrent);
        var advanced = GuidedButton("多型号或复杂绑定");
        advanced.Click += (_, _) => PreparationTabs.SelectedIndex = 2;
        projectActions.Children.Add(advanced);
        projectBody.Children.Add(projectActions);

        root.Children.Add(GuidedCard("2  扫码设备与编号验证", out var scanBody));
        _guidedDevice = GuidedText("正在检查扫码设备…");
        scanBody.Children.Add(_guidedDevice);
        _guidedScan = GuidedText("尚未获得本次真实扫码数据。");
        scanBody.Children.Add(_guidedScan);
        var scannerActions = new WrapPanel();
        var scanner = GuidedButton("连接或更换扫码枪");
        scanner.Click += (_, _) => PreparationTabs.SelectedIndex = 1;
        scannerActions.Children.Add(scanner);
        var addReal = GuidedButton("使用最近真实扫码作校验样例");
        addReal.Click += (_, _) => GuidedAdoptRealSample();
        scannerActions.Children.Add(addReal);
        scanBody.Children.Add(scannerActions);
        scanBody.Children.Add(GuidedText(
            "整串编号是默认方案。使用真实扫码作正例时，软件会明确生成一条非法字符拒绝反例；反例不是现场扫码。"));
        var sampleRow = new WrapPanel { Margin = new Thickness(0, 5, 0, 0) };
        _guidedManualInput = new TextBox
        {
            Width = 320, MinHeight = 34, VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 5, 10, 0),
            ToolTip = "手工输入用于离线预览，不能替代真实 USB COM 扫码。"
        };
        sampleRow.Children.Add(_guidedManualInput);
        var preview = GuidedButton("手动输入试算");
        preview.Click += async (_, _) =>
        {
            if (_guidedManualInput is not null)
                await PreviewGuidedBarcodeAsync(_guidedManualInput.Text, false);
        };
        sampleRow.Children.Add(preview);
        scanBody.Children.Add(sampleRow);

        root.Children.Add(GuidedCard("3  工程版本与发布检查", out var releaseBody));
        _guidedPublication = GuidedText("正在核对已发布版本…");
        releaseBody.Children.Add(_guidedPublication);
        _guidedBlocker = GuidedText("工位实体 START、机械反馈及正式 Inspection 仍需现场接通和验收。");
        releaseBody.Children.Add(_guidedBlocker);
        var publishActions = new WrapPanel();
        var publish = GuidedButton("核对并发布当前工位配置", primary: true);
        publish.Click += async (_, _) => await GuidedPublishAsync();
        publishActions.Children.Add(publish);
        var details = GuidedButton("查看发布详情与批量试扫");
        details.Click += (_, _) => PreparationTabs.SelectedIndex = 3;
        publishActions.Children.Add(details);
        releaseBody.Children.Add(publishActions);
        releaseBody.Children.Add(GuidedText(
            "发布只冻结 ECU 工程、FLASH、TEST 与编码配置，不会压夹具、烧录或启动检测。当前正式生产 START/Inspection 门禁不会因发布而解除。"));

        var view = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
            Content = root
        };
        return view;
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
                    "\n尚未通过完整工程冻结检查：" + error.Message;
        }
    }

    private async Task GuidedPublishAsync()
    {
        if (!_ready || !_mayEdit() || _busy) { Status = "工位正在运行或配置未就绪，无法发布。"; return; }
        if (_workspace.Current is not { } project)
        { Status = "先打开并保存完整 ECU 工程。"; return; }
        if (IdentityMode != StationProductionConfiguration.FixedStation || Products.Count != 1)
        {
            Status = "当前属于多型号/高级配置；请在高级识别确认映射后进入发布详情。";
            PreparationTabs.SelectedIndex = 2;
            return;
        }
        if (Products[0].ModelCode != project.Definition.EcuModelCode ||
            Products[0].HardwareCode != FixedHardware ||
            !string.Equals(Products[0].ManifestPath, project.ManifestPath,
                StringComparison.OrdinalIgnoreCase) ||
            FixedModel != project.Definition.EcuModelCode)
        {
            Status = "当前绑定与打开的 ECU 工程不一致。请先点击“使用当前打开的工程”。";
            return;
        }
        if (_scanner?.IsConnected != true || string.IsNullOrEmpty(_lastRealBarcode) ||
            _lastRealBarcodePort != _scanner.ConnectedPort ||
            _lastRealBarcodeUtc is not { } scanned ||
            DateTimeOffset.UtcNow - scanned > TimeSpan.FromMinutes(30))
        {
            Status = "尚无本次连接会话的有效真实扫码（30 分钟内）。" +
                "请先连接已绑定的 USB COM 扫码枪并现场试扫；手动输入不算。";
            return;
        }
        try
        {
            var bound = StationScannerDeviceBinding.Load();
            var actual = bound is null ? null : StationScannerDeviceBinding.Match(bound);
            if (actual is null ||
                !string.Equals(actual.PortName, _scanner.ConnectedPort,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("当前 COM 不是已绑定的实体扫码枪；" +
                    "请在扫码与工位设备中完成“记住当前扫码枪”。");
            if (!string.Equals(_lastRealBarcodePnp, actual.PnpInstanceId,
                StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("本次扫码发生于设备绑定之前，或扫码后实体设备身份已变化；" +
                    "请使用已绑定扫码枪重新扫描 ECU 编号。");
            if (!await SaveAsync()) return;
            var config = await StationProductionConfigurationStore.LoadDraftAsync();
            var issues = StationProductionConfigurationStore.InspectRules(config);
            if (issues.Count != 0)
                throw new InvalidDataException("编码规则/样例还未通过：" +
                    string.Join("；", issues.Take(5)));
            if (_publishProject is null)
                throw new InvalidOperationException("工程发布功能尚未接线。");
            var frozenEditGeneration = _editGeneration;
            var frozenRealScan = _lastRealBarcode;
            var frozenPort = _lastRealBarcodePort;
            var frozenPnp = _lastRealBarcodePnp;
            _busy = true;
            // UI's owner is responsible for saving floating editors, asking
            // the engineer to confirm, publishing FLASH and pinning TEST.
            // A cancelled dialog MUST return false; never publish the station
            // mapping on a stale previous release.
            var accepted = await _publishProject();
            if (!accepted)
            {
                Status = "已取消工程发布；工位配置没有发布，不能进入正式生产。";
                return;
            }
            var deployment = await StationDeploymentStore.RestoreAsync()
                ?? throw new InvalidDataException("工程发布后工位部署指针不存在。");
            if (deployment.Project.Definition.Id != project.Definition.Id ||
                !string.Equals(deployment.Project.ManifestSha256,
                    project.ManifestSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("工程发布后的版本/身份不等于准备时核对的 ECU 工程。");
            if (_editGeneration != frozenEditGeneration ||
                _lastRealBarcode != frozenRealScan ||
                _scanner?.IsConnected != true || _scanner.ConnectedPort != frozenPort ||
                _lastRealBarcodePnp != frozenPnp)
                throw new IOException("发布过程中工位配置或真实扫码来源发生变化，请重新核对后发布。");
            var testScan = await StationProductionConfigurationStore.PreviewDraftAsync(
                config, frozenRealScan!);
            if (!testScan.Accepted || testScan.Project is null ||
                testScan.Project.ProjectId != deployment.Project.Definition.Id.Value)
                throw new InvalidDataException("真实扫码没有唯一指向刚冻结的 ECU 工程：" +
                    testScan.Detail);
            var released = await StationProductionConfigurationStore.PublishAsync(config);
            _onPublished?.Invoke();
            Status = "工程、原生 FLASH、TEST 与本工位编码绑定已发布 · " +
                released.PublicationId.ToString("N")[..10] +
                "。正式生产仍需实体 START、夹具真实反馈与 Inspection 接线验收。";
            await RefreshOverviewAsync();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // A hardware/registry/COM exception must never escape an async
            // WPF Click event or be reinterpreted as a successful publication.
            Status = "工位整体发布未完成（不代表已获生产资格）：" + error.Message;
        }
        finally { _busy = false; }
    }

    private async Task RefreshGuidedOverviewAsync()
    {
        if (_guidedProject is null || !_ready) return;
        var current = _workspace.Current;
        if (current is null)
            _guidedProject.Text = "没有打开工程。请先进入“开发与调试”创建并保存完整 ECU 工程。";
        else
            _guidedProject.Text = "当前：" + current.Definition.Name +
                " · ECU 型号 " + current.Definition.EcuModelCode +
                "\n" + Path.GetFileName(current.ManifestPath) +
                (Products.Any(x => x.ModelCode == current.Definition.EcuModelCode &&
                    string.Equals(x.ManifestPath, current.ManifestPath,
                        StringComparison.OrdinalIgnoreCase))
                    ? " · 已列入工位绑定草稿" : " · 尚未绑定到当前工位");

        if (_guidedDevice is not null)
            _guidedDevice.Text = _scanner?.IsConnected == true
                ? "真实扫码设备：" + _scanner.ConnectedPort +
                  "；只有已绑定 PnP 设备的本次实扫可以用于工位发布。"
                : "扫码枪未连接。可以手动试算编码，但不能冒充真实 COM 设备验证。";
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
                        ? "编码规则静态校验无冲突；仍需真实设备与工程冻结验证。"
                        : "当前编码/产品配置问题：" + string.Join("；", problems.Take(3))) +
                    "\n正式生产仍阻断：可信实体 START、夹具机械反馈/互锁和正式 Inspection 写入尚未接通。";
        }
        catch (Exception error)
        {
            if (_guidedBlocker is not null)
                _guidedBlocker.Text = "配置还不能发布：" + error.Message +
                    "\n正式生产 START / Inspection 仍未接通。";
        }
    }
}
