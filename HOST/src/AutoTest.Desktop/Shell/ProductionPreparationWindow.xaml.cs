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
using AutoTest.Projects.Project;
using Microsoft.Win32;

namespace AutoTest.Desktop.Shell;

/// <summary>One offline engineering preparation workbench. All scan previews are
/// pure reads, not physical commands or another ECU TEST recipe.</summary>
public partial class ProductionPreparationWindow : Window, INotifyPropertyChanged
{
    public sealed record IdentityModeChoice(string Code, string Label);

    public sealed class ProductRow
    {
        public string ModelCode { get; set; } = "";
        public string HardwareCode { get; set; } = "";
        public string ManifestPath { get; set; } = "";
        public ProductionProductBinding ToModel() => new(ModelCode.Trim(),
            HardwareCode.Trim(), ManifestPath.Trim());
    }
    public sealed class IdentityRow
    {
        public string Serial { get; set; } = "";
        public string ModelCode { get; set; } = "";
        public string HardwareCode { get; set; } = "";
        public string ExpiresUtc { get; set; } = "";
        public string Batch { get; set; } = "";
        public ProductionIdentityRow ToModel()
        {
            DateTimeOffset? valid = null;
            if (!string.IsNullOrWhiteSpace(ExpiresUtc))
            {
                if (!DateTimeOffset.TryParse(ExpiresUtc, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal, out var parsed))
                    throw new FormatException("编号 " + Serial + " 的截止时间无法解析。");
                valid = parsed.ToUniversalTime();
            }
            return new(Serial.Trim(), ModelCode.Trim(), HardwareCode.Trim(),
                valid, Batch.Trim());
        }
    }
    public sealed class SampleRow
    {
        public string Raw { get; set; } = "";
        public bool ShouldAccept { get; set; }
        public string ExpectedSerial { get; set; } = "";
        public string ExpectedModel { get; set; } = "";
        public ProductionBarcodeSample ToModel() =>
            new(Raw, ShouldAccept, ExpectedSerial.Trim(), ExpectedModel.Trim());
    }
    public sealed class RuleRow
    {
        public string RuleId { get; set; } = "";
        public string Name { get; set; } = "";
        public int Revision { get; set; } = 1;
        public bool Enabled { get; set; } = true;
        public string Format { get; set; } = ProductionBarcodeRule.Serial;
        public string Prefix { get; set; } = "";
        public int ExactLength { get; set; }
        public string AllowedCharacters { get; set; } = @"[A-Za-z0-9_.-]+";
        public string Separator { get; set; } = "|";
        public int SerialField { get; set; } = 1;
        public int ModelField { get; set; }
        public int HardwareField { get; set; }
        public int BatchField { get; set; }
        public int SerialStart { get; set; } = 1;
        public int SerialLength { get; set; }
        public int ModelStart { get; set; }
        public int ModelLength { get; set; }
        public int HardwareStart { get; set; }
        public int HardwareLength { get; set; }
        public string RegexExpression { get; set; } = "";
        public ObservableCollection<SampleRow> Samples { get; } = new();

        public static RuleRow From(ProductionBarcodeRule r)
        {
            var row = new RuleRow
            {
                RuleId = r.RuleId, Name = r.Name, Revision = r.Revision,
                Enabled = r.Enabled, Format = r.Format, Prefix = r.Prefix,
                ExactLength = r.ExactLength, AllowedCharacters = r.AllowedCharacters,
                Separator = r.Separator, SerialField = r.SerialField,
                ModelField = r.ModelField, HardwareField = r.HardwareField,
                BatchField = r.BatchField, SerialStart = r.SerialStart,
                SerialLength = r.SerialLength, ModelStart = r.ModelStart,
                ModelLength = r.ModelLength, HardwareStart = r.HardwareStart,
                HardwareLength = r.HardwareLength, RegexExpression = r.RegexExpression
            };
            foreach (var sample in r.Samples)
                row.Samples.Add(new SampleRow
                {
                    Raw = sample.Raw, ShouldAccept = sample.ShouldAccept,
                    ExpectedSerial = sample.ExpectedSerial,
                    ExpectedModel = sample.ExpectedModel
                });
            return row;
        }
        public ProductionBarcodeRule ToModel() => new(
            RuleId, Name.Trim(), Revision, Enabled, Format.Trim(), Prefix,
            ExactLength, AllowedCharacters, Separator, SerialField, ModelField,
            HardwareField, BatchField, SerialStart, SerialLength, ModelStart,
            ModelLength, HardwareStart, HardwareLength, RegexExpression,
            Samples.Select(x => x.ToModel()).ToArray());
    }
    public sealed record ClampOrderChoice(int Value, string Label);
    public IReadOnlyList<ClampOrderChoice> ClampOrderChoices { get; } =
    [
        new(0, "请选择双路压紧顺序"),
        new(1, "夹具 1 → 夹具 2"),
        new(2, "夹具 2 → 夹具 1")
    ];

    public sealed class FixtureRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private bool _useClamp1, _useClamp2;
        private int _firstClamp;
        private void Changed(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        public string StationSlotId { get; set; } = "SLOT-01";
        public bool UseClamp1
        {
            get => _useClamp1;
            set { if (_useClamp1 == value) return; _useClamp1 = value; Changed(nameof(UseClamp1)); }
        }
        public bool UseClamp2
        {
            get => _useClamp2;
            set { if (_useClamp2 == value) return; _useClamp2 = value; Changed(nameof(UseClamp2)); }
        }
        public int FirstClamp
        {
            get => _firstClamp;
            set { if (_firstClamp == value) return; _firstClamp = value; Changed(nameof(FirstClamp)); }
        }
        public string ClampClosedFeedback { get; set; } = "NOT_WIRED";
        public string ClampOpenFeedback { get; set; } = "NOT_WIRED";
        public string StartInput { get; set; } = "NOT_WIRED";
        public string StopInput { get; set; } = "NOT_WIRED";
        public string InterlockInput { get; set; } = "NOT_WIRED";
        public int MovementTimeoutMs { get; set; } = 3000;
        public bool AutoClampEnabled { get; set; } = true;
        public bool Clamp1PressHigh { get; set; } = true;
        public bool Clamp2PressHigh { get; set; } = true;
        public int SettleDelayMs { get; set; } = 0;
        public static FixtureRow From(ProductionFixtureConfiguration x) => new()
        {
            StationSlotId = x.StationSlotId, UseClamp1 = x.UseClamp1,
            UseClamp2 = x.UseClamp2, FirstClamp = x.FirstClamp,
            ClampClosedFeedback = x.ClampClosedFeedback,
            ClampOpenFeedback = x.ClampOpenFeedback,
            StartInput = x.StartInput, StopInput = x.StopInput,
            InterlockInput = x.InterlockInput,
            MovementTimeoutMs = x.MovementTimeoutMs,
            AutoClampEnabled = x.AutoClampEnabled,
            Clamp1PressHigh = x.Clamp1PressHigh,
            Clamp2PressHigh = x.Clamp2PressHigh,
            SettleDelayMs = x.SettleDelayMs
        };
        public ProductionFixtureConfiguration ToModel() =>
            new(StationSlotId.Trim(), UseClamp1, UseClamp2,
                "MCU:PTB1/J1-B5", "MCU:PTC2/J1-B4",
                ClampClosedFeedback.Trim(), ClampOpenFeedback.Trim(),
                StartInput.Trim(), StopInput.Trim(), InterlockInput.Trim(),
                MovementTimeoutMs,
                "AFTER_DURABLE_RECORD_AND_SAFE_RELEASE_CONFIRMATION",
                FirstClamp, Clamp1PressHigh, Clamp2PressHigh, SettleDelayMs,
                AutoClampEnabled);
    }

    private readonly ProjectWorkspaceService _workspace;
    private readonly SerialScannerWorkbenchViewModel? _scanner;
    private readonly Action? _onPublished;
    private readonly Func<ProductionPublishedProduct, Task<string>>? _activateProject;
    private readonly Func<Task<bool>>? _publishProject;
    private readonly Action? _openSpecialProcedure;
    private long _editGeneration;
    private readonly Func<bool> _mayEdit;
    private readonly Func<string>? _stationDeviceStatus;
    private readonly DispatcherTimer _autosave = new()
        { Interval = TimeSpan.FromMilliseconds(850) };
    private bool _ready, _busy, _dirty, _closingAfterSave;
    private int _revision;
    private StationProductionConfiguration? _lastSaved;
    public ObservableCollection<ProductRow> Products { get; } = new();
    public ObservableCollection<IdentityRow> IdentityRows { get; } = new();
    public ObservableCollection<RuleRow> Rules { get; } = new();
    public ObservableCollection<string> PreviewResults { get; } = new();

    private string _identityMode = StationProductionConfiguration.FixedStation;
    public string IdentityMode
    {
        get => _identityMode;
        set { _identityMode = value; Changed(nameof(IdentityMode)); }
    }
    private string _fixedModel = "";
    public string FixedModel
    {
        get => _fixedModel;
        set { _fixedModel = value; Changed(nameof(FixedModel)); }
    }
    private string _fixedHardware = "";
    public string FixedHardware
    {
        get => _fixedHardware;
        set { _fixedHardware = value; Changed(nameof(FixedHardware)); }
    }
    private FixtureRow _fixture = FixtureRow.From(ProductionFixtureConfiguration.Draft);
    public FixtureRow Fixture
    {
        get => _fixture;
        private set { _fixture = value; Changed(nameof(Fixture)); }
    }
    private string _status = "正在读取生产准备配置…";
    public string Status { get => _status; private set { _status = value; Changed(nameof(Status)); } }
    private string _publicationOverview = "尚未发布生产工位配置。";
    public string PublicationOverview
    {
        get => _publicationOverview;
        private set
        {
            _publicationOverview = value;
            // Presentation-only readouts: assigning text never writes into
            // a read-only ViewModel property via a TwoWay binding.
            PublishedProductSummary.Text = value;
            PublishedVerificationSummary.Text = value;
            Changed(nameof(PublicationOverview));
        }
    }
    private string _issueSummary = "尚未运行发布检查。";
    public string IssueSummary
    {
        get => _issueSummary;
        private set { _issueSummary = value; Changed(nameof(IssueSummary)); }
    }
    private string _stationBlockerSummary = "实体输入与机械反馈尚未确认。";
    public string StationBlockerSummary
    {
        get => _stationBlockerSummary;
        private set { _stationBlockerSummary = value; Changed(nameof(StationBlockerSummary)); }
    }
    private string _connectionHint = "仅离线检查，不发送任何夹具 / CAN / UDS 命令";
    public string ConnectionHint
    {
        get => _connectionHint;
        private set { _connectionHint = value; Changed(nameof(ConnectionHint)); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed(string name) => PropertyChanged?.Invoke(this,
        new PropertyChangedEventArgs(name));

    public ProductionPreparationWindow(ProjectWorkspaceService workspace, Func<bool> mayEdit,
        SerialScannerWorkbenchViewModel? scanner = null, Action? onPublished = null,
        Func<ProductionPublishedProduct, Task<string>>? activateProject = null,
        Func<Task<bool>>? publishProject = null,
        Action? openSpecialProcedure = null,
        Func<string>? stationDeviceStatus = null)
    {
        _workspace = workspace;
        _mayEdit = mayEdit;
        _scanner = scanner;
        _onPublished = onPublished;
        _activateProject = activateProject;
        _publishProject = publishProject;
        _openSpecialProcedure = openSpecialProcedure;
        _stationDeviceStatus = stationDeviceStatus;
        InitializeComponent();
        ConfigureProductPreparationWorkspace();
        DataContext = this;
        ProductionScannerDeviceHost.Content = scanner is null
            ? new TextBlock { Text = "真实扫码设备尚未初始化。", Margin = new Thickness(10) }
            : new SerialScannerSetupView { DataContext = scanner };
        // No hidden combo-box entries: these are identity SOURCE modes.
        ModeBox.ItemsSource = new[]
        {
            new IdentityModeChoice(StationProductionConfiguration.FixedStation,
                "固定型号工位 · 只扫唯一编号"),
            new IdentityModeChoice(StationProductionConfiguration.BarcodeModel,
                "条码内容包含产品型号"),
            new IdentityModeChoice(StationProductionConfiguration.RegistryModel,
                "编号登记表确定型号")
        };
        _autosave.Tick += async (_, _) =>
        {
            _autosave.Stop();
            if (_dirty && !_busy) await SaveAsync();
        };
        Loaded += async (_, _) => await LoadAsync();
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            if (_scanner is not null)
            {
                _scanner.CompleteBarcodeReceived -= OnRealSample;
                _scanner.PropertyChanged -= OnGuidedScannerPropertyChanged;
            }
            _autosave.Stop();
        };
        if (_scanner is not null)
        {
            _scanner.CompleteBarcodeReceived += OnRealSample;
            _scanner.PropertyChanged += OnGuidedScannerPropertyChanged;
            ConnectionHint = _scanner.IsConnected
                ? "真实扫码设备：" + _scanner.ConnectedPort + " · 仅离线试扫"
                : "扫码枪未连接：仍可手动试扫；连接在独立扫码设备工具中完成";
        }
    }

    private void OnRealSample(string raw)
    {
        Dispatcher.BeginInvoke((Action)(() =>
        {
            if (!IsVisible || !_ready || _busy) return;
            var lines = PreviewInput.Text.Split(['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries).Length;
            if (lines >= 199)
                PreviewInput.Clear(); // Keep the latest physical observation.
            PreviewInput.AppendText((PreviewInput.Text.Length > 0 &&
                !PreviewInput.Text.EndsWith("\n", StringComparison.Ordinal) ? "\n" : "") +
                raw + "\n");
            // Physical scanner identity and admission remain owned by the
            // formal operator scan gate. Preparation merely previews the
            // barcode parser and never treats a scan as publication consent.
            _ = PreviewGuidedBarcodeAsync(raw, true);
        }));
    }

    private async Task LoadAsync()
    {
        if (_ready) return;
        try
        {
            var doc = await StationProductionConfigurationStore.LoadDraftAsync();
            _lastSaved = doc;
            _revision = doc.Revision;
            IdentityMode = doc.Mode;
            FixedModel = doc.FixedModel;
            FixedHardware = doc.FixedHardware;
            Fixture = FixtureRow.From(doc.Fixture);
            foreach (var rule in doc.Rules) Rules.Add(RuleRow.From(rule));
            foreach (var x in doc.Products) Products.Add(new ProductRow
            { ModelCode = x.ModelCode, HardwareCode = x.HardwareCode, ManifestPath = x.ManifestPath });
            foreach (var x in doc.IdentityRows) IdentityRows.Add(new IdentityRow
            {
                Serial = x.Serial, ModelCode = x.ModelCode, HardwareCode = x.HardwareCode,
                ExpiresUtc = x.ExpiresUtc?.ToString("O") ?? "", Batch = x.Batch
            });
            if (string.IsNullOrEmpty(FixedModel) &&
                _workspace.Current?.Definition.EcuModelCode is { } model)
            {
                FixedModel = model;
                // The operator still explicitly binds the current engineering project.
            }
            RulesGrid.SelectedIndex = Rules.Count > 0 ? 0 : -1;
            _ready = true;
            Status = "生产准备草稿已读取。配置保存不等于发布或实体工位确认。";
            await RefreshOverviewAsync();
        }
        catch (Exception error)
        {
            Status = "无法加载生产配置：" + error.Message;
            IssueSummary = Status;
        }
    }

    private StationProductionConfiguration BuildDraft()
    {
        var rules = Rules.Select(r =>
        {
            var current = r.ToModel();
            var saved = _lastSaved?.Rules.FirstOrDefault(x => x.RuleId == r.RuleId);
            if (saved is not null && !string.Equals(
                    System.Text.Json.JsonSerializer.Serialize(saved with { Revision = 0 }),
                    System.Text.Json.JsonSerializer.Serialize(current with { Revision = 0 }),
                    StringComparison.Ordinal))
                current = current with { Revision = Math.Max(saved.Revision + 1, r.Revision) };
            return current;
        }).ToArray();
        return new(1, Math.Max(1, _revision + 1), IdentityMode,
            FixedModel.Trim(), FixedHardware.Trim(), rules,
            IdentityRows.Select(x => x.ToModel()).ToArray(),
            Products.Select(x => x.ToModel()).ToArray(), Fixture.ToModel());
    }

    private void MarkDirty()
    {
        if (!_ready) return;
        _editGeneration++;
        _dirty = true;
        if (_busy) return;
        Status = "草稿已修改 · 即将自动保存（未发布）";
        _autosave.Stop();
        _autosave.Start();
        StationBlockerSummary = string.Join("；", Fixture.ToModel().PhysicalBlockers());
    }
    private void DraftChanged(object sender, TextChangedEventArgs e) => MarkDirty();
    private void DraftSelectionChanged(object sender, SelectionChangedEventArgs e) => MarkDirty();
    private void DraftSelectionChanged(object sender, RoutedEventArgs e) => MarkDirty();
    private void GridEditEnding(object sender, DataGridCellEditEndingEventArgs e) =>
        Dispatcher.BeginInvoke((Action)MarkDirty, DispatcherPriority.Background);

    private async Task<bool> SaveAsync()
    {
        if (!_mayEdit()) { Status = "工位正在运行，禁止修改生产配置。"; return false; }
        if (_busy) return false;
        // Idempotent save: publishing an unchanged draft must not silently
        // increment its revision or invalidate an accepted product identity.
        if (!_dirty && _lastSaved is not null) return true;
        _autosave.Stop();
        _busy = true;
        var generation = _editGeneration;
        try
        {
            var data = BuildDraft();
            await StationProductionConfigurationStore.SaveDraftAsync(data);
            _lastSaved = data;
            _revision = data.Revision;
            _dirty = generation != _editGeneration;
            if (_dirty) _autosave.Start();
            Status = _dirty ? "保存期间有后续修改，继续保存新草稿。" :
                "生产配置草稿已保存（尚未发布）";
            await RefreshOverviewAsync();
            return true;
        }
        catch (Exception error)
        {
            Status = "草稿未能保存，编辑内容仍在窗口：" + error.Message;
            IssueSummary = Status;
            return false;
        }
        finally { _busy = false; }
    }

    private async void Save_Click(object sender, RoutedEventArgs e) => await SaveAsync();

    private async void PublishProject_Click(object sender, RoutedEventArgs e)
    {
        if (_publishProject is null) { Status = "工程发布入口尚未接入。"; return; }
        if (!await SaveAsync()) return;
        try
        {
            var success = await _publishProject();
            Status = success ? "当前 ECU 工程已核对并冻结，未执行实体测试。" :
                "工程发布已取消或校验未通过；未取得新生产发布资格。";
            await RefreshOverviewAsync();
        }
        catch (Exception error) { Status = "工程发布未完成：" + error.Message; }
    }

    private async void Publish_Click(object sender, RoutedEventArgs e)
    {
        if (!await SaveAsync()) return;
        if (!_mayEdit()) { Status = "正在执行真实工位任务，禁止发布。"; return; }
        _busy = true;
        try
        {
            var config = BuildDraft();
            // BuildDraft increments revision, so publication must use the
            // EXACT bytes saved above, not a newly synthesised revision.
            config = await StationProductionConfigurationStore.LoadDraftAsync();
            var released = await StationProductionConfigurationStore.PublishAsync(config);
            _onPublished?.Invoke();
            Status = "已发布工位产品识别与工程关联 · " +
                released.PublicationId.ToString("N")[..10] +
                "。实体 START/机械反馈仍必须真实验收。";
            await RefreshOverviewAsync();
        }
        catch (Exception error)
        {
            Status = "发布失败，原已发布版本保持不变：" + error.Message;
            IssueSummary = Status;
        }
        finally { _busy = false; }
    }

    private async Task RefreshOverviewAsync()
    {
        var published = await StationProductionConfigurationStore.LoadPublishedAsync();
        PublicationOverview = published is null
            ? "尚未正式发布：先确认 ECU 工程已发布原生 FLASH，再绑定工程、配置编码形制及型号来源，最后发布本工位配置。"
            : "版本：" + published.Configuration.Revision +
              " · 发布 ID：" + published.PublicationId.ToString("N") +
              "\n编码形制：" + published.Configuration.Rules.Count(x => x.Enabled) +
              " 条 · 产品绑定：" + published.Projects.Count + " 型号/变型" +
              "\n" + string.Join("\n", published.Projects.Select(x =>
                  x.ModelCode + (string.IsNullOrWhiteSpace(x.HardwareCode)
                      ? "" : " / " + x.HardwareCode) +
                  " → " + Path.GetFileName(x.ManifestPath) +
                  " · FLASH SHA " + x.FlashJobFingerprint[..12] +
                  " · 固件 SHA " + x.FirmwareSha256[..12] +
                  " · TEST " + x.OrderedCaseNames.Count + " 个（" +
                  string.Join("、", x.OrderedCaseNames.Take(6)) + "）"));
        var issues = StationProductionConfigurationStore.InspectRules(BuildDraft());
        if (published is null)
            issues = issues.Concat(["尚未发布工位编码与产品工程的完整配置。"]).ToArray();
        IssueSummary = issues.Count == 0
            ? "编码规则与样例静态检查无冲突。仍需项目发布指纹校验及工位真实输入验收。"
            : string.Join("\n", issues.Take(8));
        StationBlockerSummary = string.Join("\n", Fixture.ToModel().PhysicalBlockers());
        if (published is not null)
        {
            PublicationOverview += "\n规则与产品适用范围：" +
                string.Join("；", published.Configuration.Rules.Where(x => x.Enabled)
                    .Select(x => x.Name + " v" + x.Revision +
                        " [" + x.Format + "] → " +
                        (published.Configuration.Mode == StationProductionConfiguration.FixedStation
                            ? published.Configuration.FixedModel :
                         published.Configuration.Mode == StationProductionConfiguration.BarcodeModel
                            ? "编码型号字段" : "受控编号映射表")));
            PublicationOverview += "\n工位：" + published.Configuration.Fixture.StationSlotId +
                " · 扫码设备：" + (_scanner?.IsConnected == true
                    ? _scanner.ConnectedPort : "未连接") +
                " · START/STOP/机械到位：仅已登记输入来源，未完成真实电气验证";
        }
        await RefreshGuidedOverviewAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        try { await RefreshOverviewAsync(); Status = "已重新检查发布配置与样例。"; }
        catch (Exception error) { Status = "检查失败：" + error.Message; }
    }
    private void ValidateRules_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var issues = StationProductionConfigurationStore.InspectRules(BuildDraft());
            IssueSummary = issues.Count == 0 ? "规则及样例检查通过（未运行实体扫码）。" :
                string.Join("\n", issues);
        }
        catch (Exception error) { IssueSummary = error.Message; }
    }
    private void AddCurrentProject_Click(object sender, RoutedEventArgs e)
    {
        var opened = _workspace.Current;
        if (opened?.Definition.EcuModelCode is not { Length: > 0 } code)
        { Status = "请先打开具有正式型号的 ECU 工程。"; return; }
        if (Products.Any(x => x.ModelCode == code && x.HardwareCode == FixedHardware))
        { Status = "同型号/硬件版本已经绑定，请编辑已有记录。"; return; }
        Products.Add(new ProductRow { ModelCode = code, HardwareCode = FixedHardware,
            ManifestPath = opened.ManifestPath });
        if (string.IsNullOrEmpty(FixedModel)) FixedModel = code;
        MarkDirty();
        Status = "当前工程已加入候选；还须具有已发布原生 FLASH 及完整 TEST 后方能发布工位。";
    }
    private void AddManifest_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog
        { Filter = "AutoTest ECU 工程 (*.json)|*.json", Title = "选择 ECU 工程清单" };
        if (picker.ShowDialog(this) != true) return;
        Products.Add(new ProductRow { ModelCode = FixedModel, HardwareCode = FixedHardware,
            ManifestPath = Path.GetFullPath(picker.FileName) });
        MarkDirty();
    }
    private void RemoveProject_Click(object sender, RoutedEventArgs e)
    {
        if (ProductsGrid.SelectedItem is ProductRow selected)
        { Products.Remove(selected); MarkDirty(); }
    }
    private async void ActivateProject_Click(object sender, RoutedEventArgs e)
    {
        if (_activateProject is null || ProductsGrid.SelectedItem is not ProductRow selected)
        { Status = "请选中一条已发布产品工程绑定。"; return; }
        if (_dirty) { Status = "请先保存并发布当前修改，再切换工位型号。"; return; }
        try
        {
            var publication = await StationProductionConfigurationStore.LoadPublishedAsync()
                ?? throw new InvalidDataException("工位尚未发布产品配置。");
            var pin = publication.Projects.SingleOrDefault(x =>
                x.ModelCode == selected.ModelCode &&
                x.HardwareCode == selected.HardwareCode &&
                string.Equals(x.ManifestPath, selected.ManifestPath,
                    StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException("选中的工程没有对应的已发布固定版本。");
            Status = await _activateProject(pin);
            await RefreshOverviewAsync();
        }
        catch (Exception error) { Status = "工位切换被拒绝：" + error.Message; }
    }

    private void AddRule_Click(object sender, RoutedEventArgs e)
    {
        var rule = RuleRow.From(ProductionBarcodeRule.NewSerial());
        rule.Name = "新编码形制 " + (Rules.Count + 1);
        Rules.Add(rule); RulesGrid.SelectedItem = rule; MarkDirty();
    }
    private void CloneRule_Click(object sender, RoutedEventArgs e)
    {
        if (RulesGrid.SelectedItem is not RuleRow selected) return;
        var cloned = RuleRow.From(selected.ToModel() with
        {
            RuleId = Guid.NewGuid().ToString("N"), Revision = 1,
            Name = selected.Name + "（副本）"
        });
        Rules.Add(cloned); RulesGrid.SelectedItem = cloned; MarkDirty();
    }
    private void RemoveRule_Click(object sender, RoutedEventArgs e)
    {
        if (RulesGrid.SelectedItem is RuleRow selected)
        { Rules.Remove(selected); RulesGrid.SelectedIndex = Rules.Count > 0 ? 0 : -1; MarkDirty(); }
    }
    private void AddPositiveSample_Click(object sender, RoutedEventArgs e)
    {
        if (RulesGrid.SelectedItem is RuleRow rule)
        { rule.Samples.Add(new SampleRow { Raw = "", ShouldAccept = true }); MarkDirty(); }
    }
    private void AddNegativeSample_Click(object sender, RoutedEventArgs e)
    {
        if (RulesGrid.SelectedItem is RuleRow rule)
        { rule.Samples.Add(new SampleRow { Raw = "", ShouldAccept = false }); MarkDirty(); }
    }
    private void AddIdentity_Click(object sender, RoutedEventArgs e)
    { IdentityRows.Add(new IdentityRow { ModelCode = FixedModel,
        HardwareCode = FixedHardware }); MarkDirty(); }
    private void RemoveIdentity_Click(object sender, RoutedEventArgs e)
    {
        if (IdentityGrid.SelectedItem is IdentityRow row)
        { IdentityRows.Remove(row); MarkDirty(); }
    }

    private static string[] ParseCsvRow(string line)
    {
        var fields = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                { cell.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (ch == ',' && !quoted)
            { fields.Add(cell.ToString()); cell.Clear(); }
            else cell.Append(ch);
        }
        if (quoted) throw new FormatException("CSV 包含未闭合的双引号。");
        fields.Add(cell.ToString());
        return fields.ToArray();
    }

    private void ImportIdentityCsv_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new OpenFileDialog
            { Filter = "CSV 编号表 (*.csv)|*.csv", Title = "导入受控编号与型号绑定表" };
            if (picker.ShowDialog(this) != true) return;
            var lines = File.ReadAllLines(picker.FileName, new UTF8Encoding(false, true));
            if (lines.Length > 100001 || new FileInfo(picker.FileName).Length > 8_000_000)
                throw new InvalidDataException("CSV 超出十万条编号或 8MB 上限。");
            var rows = new List<IdentityRow>();
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = ParseCsvRow(line);
                if (parts.Length < 2 || parts.Length > 5)
                    throw new InvalidDataException("CSV 必须为编号,型号,硬件版本,截止日期,批次。");
                if (parts[0].Trim().Equals("Serial", StringComparison.OrdinalIgnoreCase) ||
                    parts[0].Trim() == "唯一编号") continue;
                rows.Add(new IdentityRow
                {
                    Serial = parts[0].Trim(), ModelCode = parts[1].Trim(),
                    HardwareCode = parts.Length > 2 ? parts[2].Trim() : "",
                    ExpiresUtc = parts.Length > 3 ? parts[3].Trim() : "",
                    Batch = parts.Length > 4 ? parts[4].Trim() : ""
                });
            }
            var seen = IdentityRows.Select(x => x.Serial)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var row in rows)
                if (string.IsNullOrWhiteSpace(row.Serial) || !seen.Add(row.Serial))
                    throw new InvalidDataException("CSV 与当前表存在重复或空编号：" + row.Serial);
            foreach (var row in rows) IdentityRows.Add(row);
            MarkDirty();
            Status = "CSV 已导入 " + rows.Count + " 个编号，尚需保存、审查并发布。";
        }
        catch (Exception error)
        { Status = "CSV 未导入：" + error.Message; }
    }

    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        PreviewResults.Clear();
        try
        {
            var config = BuildDraft();
            config.Validate();
            var inputs = PreviewInput.Text.Split(
                ['\r', '\n'], StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries);
            if (inputs.Length > 200)
                throw new InvalidDataException("单批最多 200 条；不执行真实硬件动作。");
            var pins = new List<ProductionPublishedProduct>();
            foreach (var item in config.Products)
                pins.Add(await StationProductionConfigurationStore.InspectProjectAsync(
                    item, CancellationToken.None));
            var snapshot = new PublishedStationProductionConfiguration(1,
                StationProductionConfigurationStore.ConfigurationSha(config),
                Guid.NewGuid(), DateTimeOffset.UtcNow, config, pins);
            foreach (var raw in inputs)
            {
                var match = ProductionIdentityResolver.Resolve(snapshot, raw);
                if (!match.Accepted)
                {
                    PreviewResults.Add("✕ 原始编码：" + raw + "\n拒绝：" + match.Code +
                        " · " + match.Detail);
                    continue;
                }
                var pin = match.Project!;
                PreviewResults.Add(
                    "✓ 原始编码：" + raw + "\n规则：" + match.RuleId +
                    " v" + match.RuleRevision + "\n唯一编号：" + match.Serial +
                    " · 型号：" + match.Model + " · 硬件：" + match.Hardware +
                    "\n型号来源：" + match.ModelBasis + "\n唯一工程：" +
                    pin.ManifestPath + "\n工程 SHA：" + pin.ManifestSha256 +
                    "\n前置 FLASH 工艺：" + pin.NativeFlashSha256 +
                    "\n固件 SHA：" + pin.FirmwareSha256 +
                    "\nTEST (" + pin.OrderedCaseNames.Count + ")：" +
                    string.Join(" → ", pin.OrderedCaseNames) +
                    "\n工位：" + config.Fixture.StationSlotId +
                    " · 夹具要求：" +
                    (config.Fixture.UseClamp1 ? "夹具1 " : "") +
                    (config.Fixture.UseClamp2 ? "夹具2 " : "") +
                    "\n结论：资源预览匹配；正式执行仍须部署、实体 START、机械反馈和记录。");
            }
            if (inputs.Length == 0) PreviewResults.Add("请每行输入一个完整条码。");
            Status = "离线批量试扫完成；不会发送任何实体动作。";
        }
        catch (Exception error)
        {
            PreviewResults.Add("试扫未完成：" + error.Message);
            Status = "请先修复编码规则、项目发布或编号表。";
        }
    }

    private async void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_dirty && !await SaveAsync()) return;
        _closingAfterSave = true;
        Close();
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closingAfterSave || !_dirty) return;
        e.Cancel = true;
        if (await SaveAsync())
        {
            _closingAfterSave = true;
            Close();
        }
    }
}
