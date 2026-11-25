using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Security.Cryptography;
using System.Text;
using AutoTest.Core;
using AutoTest.Desktop.Projects;
using AutoTest.Desktop.Hardware;
using AutoTest.Desktop.Testing;
using AutoTest.Projects.Project;
using AutoTest.Projects.Resources;
using AutoTest.Testing.Authoring;
using AutoTest.Testing.Runtime;
using Prism.Commands;
using Prism.Mvvm;

namespace AutoTest.Desktop.Shell;

/// <summary>Read-only presentation of one real TEST resource or an executed occurrence.</summary>
public sealed class ProductionInspectionNode : BindableBase
{
    private string _state = "未执行";
    private string _actual = "";
    private string _expected = "";
    private string _detail = "";
    private string _when = "";
    private string _occurrence = "";
    private string _evidence = "";
    private string _quality = "";
    private bool _isExpanded;
    private bool _isSelected;

    public ProductionInspectionNode(string name, string kind,
        StepId? step = null, TestDocumentId? document = null, int sequence = 0)
    {
        Name = name;
        Kind = kind;
        Step = step;
        Document = document;
        IndexLabel = sequence > 0 ? sequence.ToString("D2") : "";
    }

    public string IndexLabel { get; }
    public string Name { get; }
    public string Kind { get; }
    public StepId? Step { get; }
    public TestDocumentId? Document { get; }
    public ObservableCollection<ProductionInspectionNode> Children { get; } = new();
    public ProductionInspectionNode? Parent { get; private set; }
    public bool IsExpanded { get => _isExpanded; set => SetProperty(ref _isExpanded, value); }
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
    public void AddChild(ProductionInspectionNode child)
    {
        child.Parent = this;
        Children.Add(child);
    }
    public string State { get => _state; set => SetProperty(ref _state, value); }
    public string Actual { get => _actual; set => SetProperty(ref _actual, value); }
    public string Expected { get => _expected; set => SetProperty(ref _expected, value); }
    public string Detail { get => _detail; set => SetProperty(ref _detail, value); }
    public string When { get => _when; set => SetProperty(ref _when, value); }
    public string Occurrence { get => _occurrence; set => SetProperty(ref _occurrence, value); }
    public string Evidence { get => _evidence; set => SetProperty(ref _evidence, value); }
    public string Quality { get => _quality; set => SetProperty(ref _quality, value); }
}

public sealed record ProductionInspectionLog(string Time, string Source, string Event,
    string Detail, string Occurrence = "");

/// <summary>
/// Production VIEW is an observer, not a second TEST engine. It displays only
/// verified project assets, the scanner's accepted real input, and durable
/// TEST events. Developer runs are labeled as developer runs; no observed
/// step can ever upgrade the formal production state to PASS.
/// </summary>
public sealed class ProductionInspectionViewModel : BindableBase, IDisposable
{
    private readonly ProjectWorkspaceService _workspace;
    private readonly SerialScannerWorkbenchViewModel _scanner;
    private readonly RealTestWorkbenchService _realTest;
    private readonly HardwareConnectionService _hardware;
    private readonly Action _openScanner;
    private readonly Dictionary<StepId, List<ProductionInspectionNode>> _stepIndex = new();
    private readonly Dictionary<TestDocumentId, List<ProductionInspectionNode>> _caseIndex = new();
    private int _refreshGeneration;
    private int _scanGeneration;
    private string? _lastProjectPath;
    private string _projectName = "未打开 ECU 工程";
    private string _setupIssue = "尚未配置有效的 ECU 测试工程";
    private bool _loadingCases;
    private bool _developerRunActive;
    private string _currentCaseName = "";
    private string _modelCode = "—";
    private string _dutSerial = "等待扫码";
    private string _phase = "等待工程及扫码";
    private string _productionReadiness = "尚未完成自动测试配置检查";
    private string _flashState = "本次烧录尚未开始";
    private string _testState = "本次自动测试尚未开始";
    private string _monitorMode = "未收到运行事实";
    private string _latestRunId = "—";
    private string _reportPath = "";
    private int _observedStepCount;
    private ProductionInspectionNode? _selected;

    public ProductionInspectionViewModel(ProjectWorkspaceService workspace,
        SerialScannerWorkbenchViewModel scanner,
        RealTestWorkbenchService realTest, HardwareConnectionService hardware,
        ObservableCollection<MainWindowViewModel.CanMonitorRow> canRecords,
        Action openScanner)
    {
        _workspace = workspace;
        _scanner = scanner;
        _realTest = realTest;
        _hardware = hardware;
        _hardware.ConnectionChanged += OnHardwareConnectionChanged;
        _openScanner = openScanner;
        CanRecords = canRecords;
        OpenScannerCommand = new DelegateCommand(_openScanner);
        RefreshCommand = new DelegateCommand(async () => await RefreshAsync());
        _workspace.CurrentChanged += OnProjectChanged;
        _scanner.PropertyChanged += OnScannerPropertyChanged;
        _realTest.StepBegan += OnStepBegan;
        _realTest.StepReported += OnStepReported;
        _realTest.DevelopmentRunStarted += OnDevelopmentRunStarted;
        _realTest.DevelopmentRunCompleted += OnDevelopmentRunCompleted;
        _realTest.DevelopmentRunFailed += OnDevelopmentRunFailed;
        _ = RefreshAsync();
    }

    public ObservableCollection<ProductionInspectionNode> Cases { get; } = new();
    public ObservableCollection<ProductionInspectionLog> Logs { get; } = new();
    public ObservableCollection<MainWindowViewModel.CanMonitorRow> CanRecords { get; }
    public DelegateCommand RefreshCommand { get; }
    public DelegateCommand OpenScannerCommand { get; }
    public string ProjectName { get => _projectName; private set => SetProperty(ref _projectName, value); }
    public string ModelCode { get => _modelCode; private set => SetProperty(ref _modelCode, value); }
    public string ScannerStatus => _scanner.IsConnected
        ? "扫码枪已连接 · " + _scanner.ConnectedPort
        : "扫码枪未连接 · 请在开发与调试中配置";
    public string DutSerial { get => _dutSerial; private set => SetProperty(ref _dutSerial, value); }
    public string Phase { get => _phase; private set => SetProperty(ref _phase, value); }
    public string ProductionReadiness { get => _productionReadiness; private set => SetProperty(ref _productionReadiness, value); }
    public string FlashState { get => _flashState; private set => SetProperty(ref _flashState, value); }
    public string TestState { get => _testState; private set => SetProperty(ref _testState, value); }
    public string MonitorMode { get => _monitorMode; private set => SetProperty(ref _monitorMode, value); }
    public string LatestRunId { get => _latestRunId; private set => SetProperty(ref _latestRunId, value); }
    public string ReportPath { get => _reportPath; private set => SetProperty(ref _reportPath, value); }
    public int ObservedStepCount { get => _observedStepCount; private set => SetProperty(ref _observedStepCount, value); }
    public int EnabledCaseCount => Cases.Count;
    public bool HasCases => Cases.Count > 0;
    public bool HasCaseListIssue => Cases.Count == 0;
    // An unselected or idle station has no truthful actual/expected values
    // to display. Do not reserve a large empty detail pane until a real
    // step occurrence or archived observation is being inspected.
    public bool ShowEvidenceDetails => Selected?.Step is not null;
    public GridLength EvidenceColumnWidth => ShowEvidenceDetails
        ? new GridLength(3, GridUnitType.Star) : new GridLength(0);
    // The ordinary operator display never presents the project file's internal
    // name, revision, provenance, debug branch, or reference/trace identifiers.
    public string CurrentCaseName => string.IsNullOrWhiteSpace(_currentCaseName) ? "—" : _currentCaseName;
    public string CurrentStepName => Selected?.Step is not null ? Selected.Name : "—";
    public string CurrentStepStatus => Selected?.Step is not null ? Selected.State : "未执行";
    public string CurrentMeasurement => Selected?.Step is not null && !string.IsNullOrWhiteSpace(Selected.Actual)
        ? Selected.Actual : "—";
    public string CurrentExpected => Selected?.Step is not null && !string.IsNullOrWhiteSpace(Selected.Expected)
        ? Selected.Expected : "—";
    public string CurrentFailure => Selected?.Step is not null && !string.IsNullOrWhiteSpace(Selected.Detail)
        ? Selected.Detail : "—";
    public string StatusSummary => _developerRunActive
        ? "开发调试执行中（非正式自动测试）"
        : !string.IsNullOrWhiteSpace(_setupIssue) ? "测试配置未就绪"
        : _loadingCases ? "正在读取测试项目"
        : !_hardware.IsConnected ? "主控未连接"
        : !_scanner.IsConnected ? "扫码设备未连接"
        : "自动测试暂不可用";
    public string CaseListHint => Cases.Count != 0 ? "" : _loadingCases
        ? "正在读取测试用例…" : _workspace.Current is null
        ? _setupIssue + "\n打开工程：右上角“开发与调试” → “文件” → “打开工程”。"
        : _setupIssue + "\n请进入右上角“开发与调试”，核对或启用测试用例。";
    public string OperatorNotice => !string.IsNullOrWhiteSpace(_setupIssue)
        ? _setupIssue : _developerRunActive
        ? "当前显示的是开发调试运行，不能作为 ECU 正式检测结果。"
        : "正式自动测试执行链尚未接通，不能据此作出合格结论。";
    private void NotifyOperatorDisplay()
    {
        RaisePropertyChanged(nameof(EnabledCaseCount));
        RaisePropertyChanged(nameof(HasCases));
        RaisePropertyChanged(nameof(HasCaseListIssue));
        RaisePropertyChanged(nameof(CaseListHint));
        RaisePropertyChanged(nameof(StatusSummary));
        RaisePropertyChanged(nameof(OperatorNotice));
        RaisePropertyChanged(nameof(CurrentCaseName));
        RaisePropertyChanged(nameof(CurrentStepName));
        RaisePropertyChanged(nameof(CurrentStepStatus));
        RaisePropertyChanged(nameof(CurrentMeasurement));
        RaisePropertyChanged(nameof(CurrentExpected));
        RaisePropertyChanged(nameof(CurrentFailure));
    }
    private void OnHardwareConnectionChanged(long _) => OnUi(NotifyOperatorDisplay);

    public ProductionInspectionNode? Selected
    {
        get => _selected;
        set
        {
            var previous = _selected;
            if (!SetProperty(ref _selected, value)) return;
            if (previous is not null) previous.IsSelected = false;
            if (value is not null)
            {
                for (var parent = value.Parent; parent is not null; parent = parent.Parent)
                    parent.IsExpanded = true;
                value.IsSelected = true;
            }
            NotifySelectedReadout();
        }
    }
    public string SelectedName => Selected?.Name ?? "选择用例或步骤查看";
    public string SelectedKind => Selected?.Kind ?? "工程测试树";
    public string SelectedState => Selected?.State ?? "未选择";
    public string SelectedExpected => string.IsNullOrWhiteSpace(Selected?.Expected) ? "—" : Selected.Expected;
    public string SelectedActual => string.IsNullOrWhiteSpace(Selected?.Actual) ? "—" : Selected.Actual;
    public string SelectedDetail => string.IsNullOrWhiteSpace(Selected?.Detail) ? "暂无本次真实执行记录" : Selected.Detail;
    public string SelectedTime => string.IsNullOrWhiteSpace(Selected?.When) ? "—" : Selected.When;
    public string SelectedOccurrence => string.IsNullOrWhiteSpace(Selected?.Occurrence) ? "—" : Selected.Occurrence;
    public string SelectedEvidence => string.IsNullOrWhiteSpace(Selected?.Evidence) ? "—" : Selected.Evidence;
    public string SelectedQuality => string.IsNullOrWhiteSpace(Selected?.Quality) ? "—" : Selected.Quality;

    private void NotifySelectedReadout()
    {
        RaisePropertyChanged(nameof(ShowEvidenceDetails));
        RaisePropertyChanged(nameof(EvidenceColumnWidth));
        RaisePropertyChanged(nameof(SelectedName));
        RaisePropertyChanged(nameof(SelectedKind));
        RaisePropertyChanged(nameof(SelectedState));
        RaisePropertyChanged(nameof(SelectedExpected));
        RaisePropertyChanged(nameof(SelectedActual));
        RaisePropertyChanged(nameof(SelectedDetail));
        RaisePropertyChanged(nameof(SelectedTime));
        RaisePropertyChanged(nameof(SelectedOccurrence));
        RaisePropertyChanged(nameof(SelectedEvidence));
        RaisePropertyChanged(nameof(SelectedQuality));
        NotifyOperatorDisplay();
    }

    private static void OnUi(Action action)
    {
        var dispatcher = global::System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }

    private void OnProjectChanged(object? sender, EventArgs e) => OnUi(() =>
    {
        // ProjectWorkspaceService also fires CurrentChanged after autosaves.
        // A revision update must not erase the scanner identity or active run.
        var path = _workspace.Current?.ManifestPath;
        if (!string.Equals(path, _lastProjectPath, StringComparison.OrdinalIgnoreCase))
        {
            _lastProjectPath = path;
            Interlocked.Increment(ref _scanGeneration);
            DutSerial = "等待扫码";
            Phase = "等待扫码";
            ProductionReadiness = "尚未完成自动测试配置检查";
            FlashState = "本次烧录尚未开始";
            TestState = "本次自动测试尚未开始";
            MonitorMode = "未收到运行事实";
            LatestRunId = "—";
            ReportPath = "";
            ObservedStepCount = 0;
            _developerRunActive = false;
            _currentCaseName = "";
            Logs.Clear();
        }
        _ = RefreshAsync();
    });

    private async Task RefreshAsync()
    {
        var version = Interlocked.Increment(ref _refreshGeneration);
        var opened = _workspace.Current;
        _loadingCases = opened is not null;
        NotifyOperatorDisplay();
        if (opened is null)
        {
            OnUi(() =>
            {
                if (version != _refreshGeneration) return;
                Cases.Clear();
                _stepIndex.Clear();
                _caseIndex.Clear();
                Selected = null;
                ProjectName = "未打开 ECU 工程";
                ModelCode = "—";
                _loadingCases = false;
                _setupIssue = "尚未打开 ECU 工程";
                ProductionReadiness = "请在开发与调试中打开 ECU 工程";
                NotifyOperatorDisplay();
            });
            return;
        }

        try
        {
            var sources = await LoadVerifiedCasesAsync(opened);
            OnUi(() =>
            {
                if (version != _refreshGeneration ||
                    _workspace.Current?.ManifestPath != opened.ManifestPath) return;
                Cases.Clear();
                _stepIndex.Clear();
                _caseIndex.Clear();
                Selected = null;
                ProjectName = opened.Definition.Name;
                ModelCode = opened.Definition.EcuModelCode ?? "—";
                _loadingCases = false;
                _setupIssue = "";
                var caseIndex = 0;
                foreach (var source in sources)
                {
                    var root = new ProductionInspectionNode(source.Name, "TEST 用例",
                        document: source.Document.Id, sequence: ++caseIndex);
                    foreach (var phase in Enum.GetValues<TestPhase>())
                    {
                        var phaseNode = new ProductionInspectionNode(phase switch
                        {
                            TestPhase.Prepare => "准备",
                            TestPhase.Test => "测试",
                            _ => "清理"
                        }, "执行阶段");
                        phaseNode.IsExpanded = phase == TestPhase.Test;
                        foreach (var step in source.Document.Get(phase))
                            phaseNode.AddChild(ConvertStep(step));
                        root.AddChild(phaseNode);
                    }
                    Cases.Add(root);
                    if (!_caseIndex.TryGetValue(source.Document.Id, out var roots))
                        _caseIndex[source.Document.Id] = roots = new();
                    roots.Add(root);
                }
                NotifyOperatorDisplay();
                AddLog("工程", "用例树已核对", "读取 " + Cases.Count +
                    " 个 Enabled 用例；未执行任何设备动作");
            });
        }
        catch (Exception error) when (error is IOException or InvalidDataException or
                                      UnauthorizedAccessException or ArgumentException or
                                      ProjectDataException)
        {
            OnUi(() =>
            {
                if (version != _refreshGeneration) return;
                Cases.Clear();
                _stepIndex.Clear();
                _caseIndex.Clear();
                Selected = null;
                ProjectName = opened.Definition.Name;
                ModelCode = opened.Definition.EcuModelCode ?? "—";
                _loadingCases = false;
                _setupIssue = error.Message;
                ProductionReadiness = "测试配置不可用：" + error.Message;
                NotifyOperatorDisplay();
                AddLog("工程", "读取失败", error.Message);
            });
        }
    }

    private ProductionInspectionNode ConvertStep(StepNode step)
    {
        var item = new ProductionInspectionNode(step.Name, step.Type, step.Id);
        if (!StepExecutionPolicy.IsEnabled(step)) item.State = "已禁用";
        if (!_stepIndex.TryGetValue(step.Id, out var indexed))
            _stepIndex[step.Id] = indexed = new();
        indexed.Add(item);
        foreach (var slot in step.Slots.OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            var slotNode = new ProductionInspectionNode(slot.Key, "流程分支");
            foreach (var child in slot.Value)
                slotNode.AddChild(ConvertStep(child));
            item.AddChild(slotNode);
        }
        return item;
    }

    internal sealed record VerifiedCase(string Name, TestDocument Document);

    /// <summary>Identities are taken from the current immutable asset references, never sample cases.</summary>
    internal static async Task<IReadOnlyList<VerifiedCase>> LoadVerifiedCasesAsync(
        OpenProjectContext opened, CancellationToken ct = default)
    {
        var assets = opened.Definition.Assets;
        var projects = assets.Where(a =>
            a.Kind == ProjectWorkspaceService.TestProjectKind).ToArray();
        if (projects.Length == 0)
            throw new InvalidDataException("当前工程未建立测试项目，也没有可执行用例");
        if (projects.Length > 1)
            throw new InvalidDataException("当前工程登记了多个测试项目，无法确定本次执行用例");
        var projectAsset = projects[0];
        var bytes = await ReadPinnedAssetAsync(opened.RootDirectory,
            projectAsset.RelativePath, projectAsset.Sha256, ct);
        var testProject = TestProjectCodec.Decode(bytes);
        if (!testProject.Packages.Any())
            throw new InvalidDataException("当前测试项目尚未关联任何测试用例");
        if (!testProject.Packages.Any(p => p.Enabled))
            throw new InvalidDataException("当前测试项目的用例全部未启用");
        var result = new List<VerifiedCase>();
        foreach (var entry in testProject.Packages.Where(p => p.Enabled))
        {
            var asset = assets.SingleOrDefault(a =>
                a.Id.Value == entry.PackageAssetId &&
                a.Kind == ProjectWorkspaceService.TestProgramKind)
                ?? throw new InvalidDataException("Enabled 用例引用不存在：" + entry.DisplayName);
            if (entry.PackageRevision != asset.Revision ||
                !entry.PackageSha256.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("用例引用修订已过期：" + entry.DisplayName);
            var data = await ReadPinnedAssetAsync(opened.RootDirectory,
                asset.RelativePath, asset.Sha256, ct);
            result.Add(new(entry.DisplayName,
                TestDocumentCodec.Deserialize(Encoding.UTF8.GetString(data))));
        }
        return result;
    }

    private static async Task<byte[]> ReadPinnedAssetAsync(string root, string relative,
        string expectedSha, CancellationToken ct)
    {
        var path = ProjectPathPolicy.Normalize(root, relative);
        var bytes = await File.ReadAllBytesAsync(path, ct);
        if (!Convert.ToHexString(SHA256.HashData(bytes))
                .Equals(expectedSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("工程资源 SHA 不匹配：" + relative);
        return bytes;
    }

    private void OnScannerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SerialScannerWorkbenchViewModel.IsConnected) or
            nameof(SerialScannerWorkbenchViewModel.ConnectedPort))
            OnUi(() => { RaisePropertyChanged(nameof(ScannerStatus)); NotifyOperatorDisplay(); });
        if (e.PropertyName != nameof(SerialScannerWorkbenchViewModel.LastDecision)) return;
        OnUi(() =>
        {
            var decision = _scanner.LastDecision;
            if (string.IsNullOrWhiteSpace(decision)) return;
            var serial = _scanner.LastSerial;
            var model = _scanner.LastModel;
            if (decision != "SCAN_MODEL_MATCHED" || string.IsNullOrWhiteSpace(serial))
            {
                // Rejected input does not replace the previously displayed DUT/result.
                AddLog("扫码", "输入未接受：" + decision, model + " · " + serial);
                return;
            }
            if (_realTest.IsRunning)
            {
                AddLog("扫码", "已忽略", "当前研发 TEST 正在运行；不能切换本次 ECU");
                return;
            }
            DutSerial = serial;
            Phase = "已识别 ECU，检查配置";
            ProductionReadiness = "正在校验已发布烧录参数、固件与全部 Enabled 用例";
            FlashState = "本次前置烧录尚未开始";
            TestState = "本次自动测试尚未开始";
            MonitorMode = "尚未启动本次自动测试";
            LatestRunId = "—";
            ReportPath = "";
            ObservedStepCount = 0;
            Logs.Clear();
            _ = RefreshAsync();
            AddLog("扫码", "型号与工程匹配", model + " · " + serial);
            _ = InspectProductionConfigurationAsync(_workspace.Current, model, serial,
                Interlocked.Increment(ref _scanGeneration));
        });
    }

    private async Task InspectProductionConfigurationAsync(OpenProjectContext? opened,
        string model, string serial, int generation)
    {
        if (opened is null) return;
        var result = await ProductionPreparationService.PrepareAsync(opened, model, serial);
        OnUi(() =>
        {
            if (generation != _scanGeneration ||
                _workspace.Current?.ManifestPath != opened.ManifestPath) return;
            if (result.ConfigurationReady)
            {
                ProductionReadiness = "配置预检通过 · " +
                    result.Preparation!.OrderedEnabledCases.Count +
                    " 个 Enabled 用例 · 烧录参数已冻结";
                Phase = "自动测试执行尚未接通";
                FlashState = "前置烧录尚未执行 · " +
                    result.Preparation.EcuCanKbps + " kbit/s · Job " +
                    result.Preparation.JobFingerprint[..12];
                AddLog("自动测试配置", "只读预检通过",
                    "配置指纹 " + result.Preparation.PreparationFingerprint[..12] +
                    "；没有获得物理启动授权");
            }
            else
            {
                ProductionReadiness = result.Explanation;
                Phase = "配置预检未通过";
                AddLog("自动测试配置", "预检拒绝", result.Explanation);
            }
        });
    }

    private void OnDevelopmentRunStarted(TestDocumentId id, string name) => OnUi(() =>
    {
        _developerRunActive = true;
        _currentCaseName = name;
        MonitorMode = "研发 TEST 实时观察 · 仅为开发调试观察";
        NotifyOperatorDisplay();
        TestState = "开发 TEST 已开始：" + name;
        Phase = "观察研发 TEST";
        if (_caseIndex.TryGetValue(id, out var matches) && matches.Count == 1)
        {
            matches[0].State = "研发执行中";
            Selected = matches[0];
        }
        else AddLog("TEST", "用例关联不可唯一确定",
            name + "；运行事实仍显示，但不归属当前用例");
        AddLog("TEST", "研发执行开始", name);
    });

    private void OnStepBegan(StepId step, string name, TestPhase phase) => OnUi(() =>
    {
        if (!_stepIndex.TryGetValue(step, out var matches) || matches.Count != 1)
        {
            AddLog("步骤", "开始", name + "；当前工程中无法唯一对应步骤");
            return;
        }
        var node = matches[0];
        // A new occurrence must never show an earlier run's value as current.
        node.State = "执行中";
        node.Actual = "";
        node.Expected = "";
        node.Quality = "";
        node.Evidence = "";
        node.Occurrence = "";
        node.When = "";
        node.Detail = "真实 TEST 步骤已开始；等待实际结果及 RECORD 证据";
        Selected = node;
        NotifySelectedReadout();
        TestState = PhaseLabel(phase) + " · " + name;
        AddLog("步骤", "执行中", name + " · " + PhaseLabel(phase));
    });

    private void OnStepReported(RealStepFact fact) => OnUi(() =>
    {
        ObservedStepCount++;
        if (_stepIndex.TryGetValue(fact.Step, out var nodes) && nodes.Count == 1)
        {
            var node = nodes[0];
            node.State = StepStatusLabel(fact.Status);
            node.Actual = fact.Actual;
            node.Expected = fact.Expected;
            node.Detail = fact.Detail;
            node.When = fact.At.ToLocalTime().ToString("HH:mm:ss.fff");
            node.Occurrence = fact.StepOccurrenceId == Guid.Empty ? "" :
                fact.StepOccurrenceId.ToString("D");
            node.Evidence = fact.Evidence ?? "";
            node.Quality = fact.ObservationStatus?.ToString() ?? "";
            Selected = node;
            NotifySelectedReadout();
        }
        AddLog("步骤", fact.Type + " · " + StepStatusLabel(fact.Status),
            fact.Actual + (fact.Detail.Length == 0 ? "" : " · " + fact.Detail) +
            (fact.LoopIteration is null ? "" : " · 循环第 " + fact.LoopIteration + " 次"),
            fact.StepOccurrenceId);
    });

    private void OnDevelopmentRunCompleted(TestDocumentId id, RealRunResult result) => OnUi(() =>
    {
        if (_caseIndex.TryGetValue(id, out var matches) && matches.Count == 1)
            matches[0].State = "研发" + VerdictLabel(result.Verdict);
        NotifySelectedReadout();
        _developerRunActive = false;
        MonitorMode = "研发 TEST 完成 · 不作为正式检测结果";
        NotifyOperatorDisplay();
        LatestRunId = result.Run.Value.ToString("D");
        ReportPath = result.EvidencePath ?? "";
        TestState = "研发 TEST 结束：" + VerdictLabel(result.Verdict);
        Phase = "等待下一次自动测试";
        AddLog("TEST", "研发运行完成",
            "RunId " + LatestRunId + " · " + VerdictLabel(result.Verdict));
    });

    private void OnDevelopmentRunFailed(TestDocumentId id, string error) => OnUi(() =>
    {
        if (_caseIndex.TryGetValue(id, out var matches) && matches.Count == 1)
            matches[0].State = "研发执行异常";
        _developerRunActive = false;
        MonitorMode = "研发 TEST 异常 · 不作为正式检测结果";
        NotifyOperatorDisplay();
        TestState = "研发 TEST 异常：" + error;
        Phase = "运行异常";
        AddLog("TEST", "研发执行异常", error);
    });

    private static string PhaseLabel(TestPhase phase) => phase switch
    {
        TestPhase.Prepare => "准备",
        TestPhase.Test => "测试",
        _ => "清理"
    };

    private static string StepStatusLabel(RealStepStatus status) => status switch
    {
        RealStepStatus.Completed => "已完成",
        RealStepStatus.Skipped => "跳过",
        RealStepStatus.ConditionFalse => "条件不成立",
        RealStepStatus.NoData => "无数据",
        RealStepStatus.Timeout => "超时",
        RealStepStatus.InvalidData => "数据无效",
        RealStepStatus.DeviceError => "设备异常",
        RealStepStatus.Cancelled => "已取消",
        _ => status.ToString()
    };

    private static string VerdictLabel(RealTestVerdict verdict) => verdict switch
    {
        RealTestVerdict.Passed => "通过",
        RealTestVerdict.Failed => "失败",
        RealTestVerdict.Inconclusive => "结论不足",
        RealTestVerdict.Aborted => "已中止",
        _ => "异常"
    };

    private void AddLog(string source, string name, string detail, Guid? occurrence = null)
    {
        Logs.Add(new(DateTime.Now.ToString("HH:mm:ss.fff"), source, name, detail,
            occurrence is { } id && id != Guid.Empty ? id.ToString("D") : ""));
        while (Logs.Count > 600) Logs.RemoveAt(0);
    }

    public void Dispose()
    {
        _workspace.CurrentChanged -= OnProjectChanged;
        _scanner.PropertyChanged -= OnScannerPropertyChanged;
        _hardware.ConnectionChanged -= OnHardwareConnectionChanged;
        _realTest.StepBegan -= OnStepBegan;
        _realTest.StepReported -= OnStepReported;
        _realTest.DevelopmentRunStarted -= OnDevelopmentRunStarted;
        _realTest.DevelopmentRunCompleted -= OnDevelopmentRunCompleted;
        _realTest.DevelopmentRunFailed -= OnDevelopmentRunFailed;
    }
}
