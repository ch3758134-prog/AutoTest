using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoTest.Specialists.Scan.Lab;
using Prism.Commands;
using Prism.Mvvm;

namespace AutoTest.Desktop.Tools.Production;

public sealed class LabRuleRow : BindableBase
{
    private string _ruleId = "";
    private string _pattern = "";
    private string _fields = "serial";
    private string _reviewId = "";
    private bool _enabled = true;
    public string RuleId { get => _ruleId; set => SetProperty(ref _ruleId, value); }
    public string Pattern { get => _pattern; set => SetProperty(ref _pattern, value); }
    public string IdentityFieldsCsv { get => _fields; set => SetProperty(ref _fields, value); }
    public string OverlapReviewId { get => _reviewId; set => SetProperty(ref _reviewId, value); }
    public bool Enabled { get => _enabled; set => SetProperty(ref _enabled, value); }
}

public sealed class LabRecipeRow : BindableBase
{
    private string _recipeId = "";
    private string _revision = "1";
    private string _model = "";
    private string _customer = "";
    private string _testKey = "";
    private string _stationKey = "";
    private string _fromUtc = "";
    private string _untilUtc = "";
    private bool _enabled = true;
    public string RecipeId { get => _recipeId; set => SetProperty(ref _recipeId, value); }
    public string Revision { get => _revision; set => SetProperty(ref _revision, value); }
    public string ModelField { get => _model; set => SetProperty(ref _model, value); }
    public string CustomerField { get => _customer; set => SetProperty(ref _customer, value); }
    public string MockTestProgramKey { get => _testKey; set => SetProperty(ref _testKey, value); }
    public string MockStationProfileKey { get => _stationKey; set => SetProperty(ref _stationKey, value); }
    public string EffectiveFromUtc { get => _fromUtc; set => SetProperty(ref _fromUtc, value); }
    public string EffectiveUntilUtc { get => _untilUtc; set => SetProperty(ref _untilUtc, value); }
    public bool Enabled { get => _enabled; set => SetProperty(ref _enabled, value); }
}

public sealed class LabSampleRow : BindableBase
{
    private string _raw = "";
    private string _rule = "";
    private string _recipe = "";
    private string _atUtc = "";
    public string ObservedAtUtc { get => _atUtc; set => SetProperty(ref _atUtc, value); }
    public string RawBarcode { get => _raw; set => SetProperty(ref _raw, value); }
    public string ExpectedRuleId { get => _rule; set => SetProperty(ref _rule, value); }
    public string ExpectedRecipeId { get => _recipe; set => SetProperty(ref _recipe, value); }
}

public sealed record LabHistoryRow(string RunId, string When, string UnitKey,
    string Recipe, string Result, string Origin, bool FactoryReleased);
public sealed record LabScanEventRow(string When, string Input, string Result, string Origin);

// Tool-specific VM. Shell registration and the shared TEST/RECORD facades belong to INTEGRATION.
public sealed class LabProductionToolViewModel : BindableBase
{
    private readonly LabCatalogStore _store;
    private readonly LabProductionOrchestrator _orchestrator;
    private readonly ProductionDiagnosticJournal _diagnostics;
    private readonly LabScannerCaptureProbe _scannerProbe = new();
    private string _scannerEvidence = "输入方式：专用键盘口/人工，物理扫描器身份未验证";
    private bool _errorsOnly;
    private bool _scannerOnly;
    private bool _includeRawBarcodeInLocalExport;
    private string _exportPath = "未导出（默认隐藏原始扫码）";
    private string _sampleReplayAtUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
    private string _sampleReplayStatus = "未执行；只评估当前界面草稿，不修改生产配置";
    private string _overlapStatus = "未检查；有限样本不能证明规则无交集";
    private string _revisionCompareStatus = "未比较；当前编辑尚未发布";

    private LabCatalog _publishedCatalog;
    private CancellationTokenSource? _current;
    private string _scanInput = "M000001";
    private string _status = "就绪：演示 MOCK，不允许真实出厂放行";
    private string _matched = "未预览";
    private string _ruleRevision = "";
    private string _catalogSource = "内置 MOCK 样例（非生产配方）";
    private string _lastRun = "尚未运行";
    private string _draftState = "当前操作不影响已发布版本";
    private bool _busy;
    private bool _allowRetest;
    private LabMatch? _preview;
    private LabMockScenario _scenario = LabMockScenario.Pass;
    private LabRuleRow? _selectedRule;
    private LabRecipeRow? _selectedRecipe;
    private LabSampleRow? _selectedSample;

    public LabProductionToolViewModel() : this(CreateDefaultStore(),
        new LabProductionOrchestrator(new LabDeterministicMockRunner(), new LabMemoryLedger())) { }

    public LabProductionToolViewModel(LabCatalogStore store, LabProductionOrchestrator orchestrator,
        ProductionDiagnosticJournal? diagnostics = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _diagnostics = diagnostics ?? new ProductionDiagnosticJournal();
        _publishedCatalog = LabDemoCatalog.Create();
        RuleRevision = _publishedCatalog.Revision;
        PopulateRows(_publishedCatalog);
        PreviewCommand = new DelegateCommand(PreviewScan, () => !IsBusy);
        Trace("Startup", "Info", "PRODUCTION_MOCK_ONLY",
            "生产 UI 已初始化；仅 MOCK，暂无正式 TEST/RECORD 与 Physical 授权");
        RunMockCommand = new DelegateCommand(() => _ = RunMockAsync(), () => !IsBusy);
        AbortCommand = new DelegateCommand(() =>
        {
            Trace("Run", "Warning", "ABORT_REQUESTED", "操作员请求取消当前 Mock 作业");
            _current?.Cancel();
        }, () => IsBusy);
        SaveDraftCommand = new DelegateCommand(() => _ = SaveDraftAsync(), () => !IsBusy);
        PublishCommand = new DelegateCommand(() => _ = PublishAsync(), () => !IsBusy);
        LoadCommand = new DelegateCommand(() => _ = LoadAsync(), () => !IsBusy);
        AddRuleCommand = new DelegateCommand(() => Rules.Add(new LabRuleRow()), () => !IsBusy);
        RemoveRuleCommand = new DelegateCommand(() => { if (SelectedRule is not null) Rules.Remove(SelectedRule); }, () => !IsBusy);
        AddRecipeCommand = new DelegateCommand(() => Recipes.Add(new LabRecipeRow()), () => !IsBusy);
        RemoveRecipeCommand = new DelegateCommand(() => { if (SelectedRecipe is not null) Recipes.Remove(SelectedRecipe); }, () => !IsBusy);
        AddSampleCommand = new DelegateCommand(() => Samples.Add(new LabSampleRow()), () => !IsBusy);
        RemoveSampleCommand = new DelegateCommand(() => { if (SelectedSample is not null) Samples.Remove(SelectedSample); }, () => !IsBusy);
        ReplaySamplesCommand = new DelegateCommand(ReplayDraftSamples, () => !IsBusy);
        InspectOverlapCommand = new DelegateCommand(InspectDraftRuleOverlap, () => !IsBusy);
        CompareDraftCommand = new DelegateCommand(CompareDraftWithPublished, () => !IsBusy);
    }

    public ObservableCollection<LabRuleRow> Rules { get; } = new();
    public ObservableCollection<LabRecipeRow> Recipes { get; } = new();
    public ObservableCollection<LabSampleRow> Samples { get; } = new();
    public ObservableCollection<LabHistoryRow> History { get; } = new();
    public ObservableCollection<LabScanEventRow> ScanEvents { get; } = new();
    public ObservableCollection<ProductionDiagnosticEntry> DiagnosticEvents { get; } = new();
    public ObservableCollection<LabSampleReplayRow> SampleReplayRows { get; } = new();
    public ObservableCollection<LabRuleOverlapFinding> RuleOverlapRows { get; } = new();
    public ObservableCollection<LabCatalogChange> RevisionChangeRows { get; } = new();
    public string SampleReplayAtUtc
    {
        get => _sampleReplayAtUtc;
        set => SetProperty(ref _sampleReplayAtUtc, value);
    }
    public string SampleReplayStatus
    {
        get => _sampleReplayStatus;
        private set => SetProperty(ref _sampleReplayStatus, value);
    }
    public string OverlapStatus
    {
        get => _overlapStatus;
        private set => SetProperty(ref _overlapStatus, value);
    }
    public string RevisionCompareStatus
    {
        get => _revisionCompareStatus;
        private set => SetProperty(ref _revisionCompareStatus, value);
    }

    // Preview only: this never mutates the published production snapshot.
    public void ReplayDraftSamples()
    {
        if (IsBusy) return;
        SampleReplayRows.Clear();
        if (!DateTimeOffset.TryParse(SampleReplayAtUtc, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var observedAt))
        {
            SampleReplayStatus = "SAMPLE_DEFAULT_TIME_INVALID：请输入 UTC 时间，例如 2026-11-09T00:00:00Z";
            Trace("SampleReplay", "Warning", "SAMPLE_DEFAULT_TIME_INVALID",
                "样本回放未执行；默认 UTC 时间格式错误");
            return;
        }
        try
        {
            var draft = BuildDraft();
            var report = LabCatalogDiagnostics.ReplaySamples(draft, observedAt);
            foreach (var row in report.Rows)
            {
                SampleReplayRows.Add(row);
                Trace("SampleReplay", row.Passed ? "Info" : "Warning", row.Code,
                    "DraftSample=" + row.RowNumber + "; ExpectedRule=" + row.ExpectedRuleId +
                    "; ActualRule=" + row.ActualRuleId +
                    "; ExpectedRecipe=" + row.ExpectedRecipeId +
                    "; ActualRecipe=" + row.ActualRecipeId +
                    "; Origin=DRAFT_SOFTWARE_ONLY",
                    rawBarcode: row.RawBarcode);
            }
            var validationCount = LabCatalogValidator.Validate(draft, forPublication: true).Count;
            SampleReplayStatus = "草稿样本回放：" + report.Passed + "/" + report.ExaminedSamples +
                " 通过；失败 " + report.Failed + "；总样本 " + report.TotalSamples +
                (report.Truncated ? "；超过单批上限，结果不完整" : "") +
                "；发布校验问题 " + validationCount +
                "；仅样本诊断，不能证明规则无冲突/生产可放行";
            Trace("SampleReplay", report.AllPassed && validationCount == 0 ? "Info" : "Warning",
                report.Truncated ? "SAMPLE_REPLAY_TRUNCATED" :
                report.Failed > 0 ? "SAMPLE_REPLAY_FAILED" :
                validationCount > 0 ? "CATALOG_VALIDATION_BLOCKED" : "SAMPLE_REPLAY_FINISHED",
                "Examined=" + report.ExaminedSamples + "; Passed=" + report.Passed +
                "; Failed=" + report.Failed + "; Total=" + report.TotalSamples +
                "; PublicationBlockers=" + validationCount + "; FactoryReleased=false");
        }
        catch (Exception ex)
        {
            SampleReplayStatus = "样本回放异常：" + ex.GetType().Name;
            Trace("SampleReplay", "Error", "SAMPLE_REPLAY_ERROR", ex.GetType().Name);
        }
    }

    public void InspectDraftRuleOverlap()
    {
        if (IsBusy) return;
        RuleOverlapRows.Clear();
        try
        {
            var report = LabCatalogDiagnostics.InspectRuleOverlap(BuildDraft());
            foreach (var row in report.Findings) RuleOverlapRows.Add(row);
            var confirmed = report.Findings.Count(x =>
                x.Classification is "IDENTICAL_REGEX" or "SAMPLE_COLLISION");
            OverlapStatus = "检查规则 " + report.EnabledRules + "，样本 " +
                report.ExaminedSamples + "/" + report.TotalSamples +
                "；已证实冲突对 " + confirmed +
                (report.Incomplete ? "；检查不完整" : "") +
                "；其他规则对即使未发现冲突也不能认定无交集";
            Trace("RuleOverlap", confirmed > 0 || report.Incomplete ? "Warning" : "Info",
                report.Incomplete ? "OVERLAP_DIAG_INCOMPLETE" :
                confirmed > 0 ? "CONFIRMED_RULE_OVERLAP" : "OVERLAP_UNPROVEN",
                "RuleCount=" + report.EnabledRules + "; SampleCount=" +
                report.ExaminedSamples + "; ConfirmedPairs=" + confirmed +
                "; FullDisjointnessUnproven=true; Origin=DRAFT_SOFTWARE_ONLY");
        }
        catch (Exception ex)
        {
            OverlapStatus = "重叠诊断异常：" + ex.GetType().Name;
            Trace("RuleOverlap", "Error", "RULE_OVERLAP_ERROR", ex.GetType().Name);
        }
    }

    public void CompareDraftWithPublished()
    {
        if (IsBusy) return;
        RevisionChangeRows.Clear();
        try
        {
            var comparison = LabCatalogDiagnostics.Compare(_publishedCatalog, BuildDraft());
            foreach (var change in comparison.Changes) RevisionChangeRows.Add(change);
            RevisionCompareStatus = "已发布 " + comparison.PublishedRevision +
                " ↔ 当前草稿（未发布）；差异项 " + comparison.Changes.Count +
                "；不执行保存、发布或工位动作";
            Trace("RevisionCompare", "Info", "CATALOG_DIFF_FINISHED",
                "ChangedItems=" + comparison.Changes.Count +
                "; PublishedRevision=" + comparison.PublishedRevision +
                "; DraftNotPublished=true");
        }
        catch (Exception ex)
        {
            RevisionCompareStatus = "修订比较异常：" + ex.GetType().Name;
            Trace("RevisionCompare", "Error", "CATALOG_DIFF_ERROR", ex.GetType().Name);
        }
    }
    public bool ErrorsOnly
    {
        get => _errorsOnly;
        set { if (SetProperty(ref _errorsOnly, value)) RefreshDiagnostics(); }
    }
    public bool ScannerOnly
    {
        get => _scannerOnly;
        set { if (SetProperty(ref _scannerOnly, value)) RefreshDiagnostics(); }
    }
    public bool IncludeRawBarcodeInLocalExport
    {
        get => _includeRawBarcodeInLocalExport;
        set => SetProperty(ref _includeRawBarcodeInLocalExport, value);
    }
    public string ExportPath { get => _exportPath; private set => SetProperty(ref _exportPath, value); }
    public long DroppedDiagnosticEvents => _diagnostics.DroppedCount;
    public string ScannerEvidence { get => _scannerEvidence; private set => SetProperty(ref _scannerEvidence, value); }

    public void ScannerFieldFocused() => _scannerProbe.FocusAcquired();
    public void ScannerFieldLostFocus() => _scannerProbe.FocusLost();
    public void ScannerTextChanged(string text, bool focused) => _scannerProbe.TextChanged(text, focused);

    // Enter in focused dedicated scanner field; input device identity remains UNVERIFIED.
    public void PreviewScannerEnter(string text, bool focused)
    {
        ScanInput = text;
        var capture = _scannerProbe.CompleteEnter(text, focused, IsBusy);
        ScannerEvidence = $"Attempt={capture.AttemptId:N} · {capture.DecisionCode} · " +
            $"字符={capture.CharacterCount} · 变更事件={capture.TextChangeEvents} · " +
            $"耗时={capture.ElapsedMilliseconds:F0}ms · 键盘口/人工未区分";
        Trace("ScannerAcquisition", capture.Accepted ? "Info" : "Warning",
            capture.DecisionCode, $"Focusable={capture.DedicatedFieldFocused}; " +
            $"Busy={capture.StationBusy}; Enter=true; " +
            $"Length={capture.CharacterCount}; Changes={capture.TextChangeEvents}; " +
            $"ElapsedMs={capture.ElapsedMilliseconds:F0}; " +
            "DeviceIdentity=UNVERIFIED; Transport=FOCUSED_KEYBOARD_INPUT",
            rawBarcode: capture.RawText, captureId: capture.AttemptId);
        if (!capture.Accepted)
        {
            if (!IsBusy)
            {
                _preview = null;
                MatchDetail = capture.DecisionCode;
                Status = "扫码枪诊断拒绝：" + capture.DecisionCode;
            }
            return;
        }
        PreviewScan();
    }
    public string GetDiagnosticText() =>
        _diagnostics.CopyText(ErrorsOnly, ScannerOnly ? "ScannerAcquisition" : null);

    public async Task ExportDiagnosticsAsync(string filePath, CancellationToken ct = default)
    {
        Trace("Export", "Info", "EXPORT_REQUESTED",
            IncludeRawBarcodeInLocalExport
                ? "用户明确选择原始扫码本地导出；该文件可能包含敏感编号"
                : "用户请求导出诊断 JSONL（原始扫码脱敏）");
        try
        {
            await _diagnostics.ExportJsonLinesAsync(filePath,
                includeRawBarcode: IncludeRawBarcodeInLocalExport, cancellationToken: ct);
            ExportPath = Path.GetFullPath(filePath);
            Trace("Export", "Info", "EXPORT_COMPLETED", "诊断文件已经写入指定位置");
        }
        catch (Exception ex)
        {
            ExportPath = "导出失败：" + ex.GetType().Name;
            Trace("Export", "Error", "EXPORT_FAILED", ex.GetType().Name);
        }
    }

    private void Trace(string stage, string severity, string code, string summary,
        Guid? runId = null, string? rawBarcode = null, string recipeRevision = "",
        Guid? captureId = null)
    {
        _diagnostics.Append(stage, severity, code, summary,
            "Mock/ProductionUI", RuleRevision, recipeRevision, runId, rawBarcode, captureId);
        RefreshDiagnostics();
    }

    private void RefreshDiagnostics()
    {
        // UI is bounded. Export contains all retained journal entries independent of filters.
        var newest = _diagnostics.Snapshot(ErrorsOnly,
            ScannerOnly ? "ScannerAcquisition" : null).TakeLast(500).Reverse().ToArray();
        DiagnosticEvents.Clear();
        foreach (var entry in newest) DiagnosticEvents.Add(entry);
        RaisePropertyChanged(nameof(DroppedDiagnosticEvents));
    }

    public IReadOnlyList<LabMockScenario> Scenarios { get; } = Enum.GetValues<LabMockScenario>();
    public DelegateCommand PreviewCommand { get; }
    public DelegateCommand RunMockCommand { get; }
    public DelegateCommand AbortCommand { get; }
    public DelegateCommand SaveDraftCommand { get; }
    public DelegateCommand PublishCommand { get; }
    public DelegateCommand LoadCommand { get; }
    public DelegateCommand AddRuleCommand { get; }
    public DelegateCommand RemoveRuleCommand { get; }
    public DelegateCommand AddRecipeCommand { get; }
    public DelegateCommand RemoveRecipeCommand { get; }
    public DelegateCommand AddSampleCommand { get; }
    public DelegateCommand RemoveSampleCommand { get; }
    public DelegateCommand ReplaySamplesCommand { get; }
    public DelegateCommand InspectOverlapCommand { get; }
    public DelegateCommand CompareDraftCommand { get; }

    public string ScanInput { get => _scanInput; set => SetProperty(ref _scanInput, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string MatchDetail { get => _matched; private set => SetProperty(ref _matched, value); }
    public string RuleRevision { get => _ruleRevision; private set => SetProperty(ref _ruleRevision, value); }
    public string CatalogSource { get => _catalogSource; private set => SetProperty(ref _catalogSource, value); }
    public string LastRun { get => _lastRun; private set => SetProperty(ref _lastRun, value); }
    public string DraftState { get => _draftState; private set => SetProperty(ref _draftState, value); }
    public LabMockScenario SelectedScenario { get => _scenario; set => SetProperty(ref _scenario, value); }
    public bool AllowRetest { get => _allowRetest; set => SetProperty(ref _allowRetest, value); }
    public LabRuleRow? SelectedRule { get => _selectedRule; set => SetProperty(ref _selectedRule, value); }
    public LabRecipeRow? SelectedRecipe { get => _selectedRecipe; set => SetProperty(ref _selectedRecipe, value); }
    public LabSampleRow? SelectedSample { get => _selectedSample; set => SetProperty(ref _selectedSample, value); }
    public bool IsBusy
    {
        get => _busy;
        private set
        {
            if (!SetProperty(ref _busy, value)) return;
            foreach (var cmd in Commands()) cmd.RaiseCanExecuteChanged();
        }
    }
    public int MockRunCount => _orchestrator.History.Count;
    public int DistinctMockUnits => _orchestrator.DistinctDemoUnits;
    public int FactoryReleasedCount => _orchestrator.FactoryReleasedUnits;

    // Called only from a dedicated barcode TextBox / explicit button, never a global key hook.
    public void PreviewScan()
    {
        var input = LabScannerAdmission.Admit(ScanInput, dedicatedFieldFocused: true, busy: IsBusy);
        if (!input.Allowed)
        {
            _preview = null; MatchDetail = input.Code; Status = input.Code;
            AddScanEvent(ScanInput, input.Code);
            Trace("Scan", "Warning", input.Code, "扫码输入被拒绝", rawBarcode: ScanInput);
            return;
        }
        Trace("ScanCapture", "Info", "SCAN_CAPTURED",
            "专用输入框已捕获扫码文本；键盘模拟设备身份未独立验证，尚未创建 Run",
            rawBarcode: input.NormalizedText);
        _preview = LabIdentityMatcher.Match(_publishedCatalog, input.NormalizedText,
            _publishedCatalog.Revision);
        MatchDetail = _preview.Accepted
            ? $"唯一匹配：{_preview.Identity!.UnitKey} / {_preview.Recipe!.RecipeId} / 规则 {_preview.Identity.RuleId}"
            : $"阻断：{_preview.Code} [{string.Join(",", _preview.CandidateIds)}]";
        Status = _preview.Accepted ? "匹配成功（仅 MOCK 演示，尚未运行）" : "扫码匹配未通过，禁止执行";
        AddScanEvent(input.NormalizedText, _preview.Code);
        Trace("IdentityMatch", _preview.Accepted ? "Info" : "Warning", _preview.Code,
            _preview.Accepted
                ? "已唯一匹配 Rule=" + _preview.Identity!.RuleId +
                  "; Recipe=" + _preview.Recipe!.RecipeId +
                  "; RecipeRevision=" + _preview.Recipe.Revision + "; 尚未创建 Run"
                : "身份或配方未通过唯一匹配；候选数=" + _preview.CandidateIds.Count,
            rawBarcode: input.NormalizedText,
            recipeRevision: _preview.Recipe?.Revision ?? "");
    }

    public async Task RunMockAsync()
    {
        if (IsBusy)
        {
            Trace("RunAdmission", "Warning", "STATION_BUSY_SCAN_REJECTED",
                "运行期间拒绝第二次启动");
            return;
        }
        PreviewScan();
        if (_preview?.Accepted != true) return;
        var preflight = _orchestrator.Preflight(_publishedCatalog, _preview,
            LabOrigin.Mock, AllowRetest);
        if (!preflight.Ready)
        {
            Status = "预检拒绝：" + string.Join(" / ", preflight.Blockers);
            foreach (var blocker in preflight.Blockers)
                Trace("Preflight", "Warning", blocker, "本轮未执行测试");
            return;
        }
        IsBusy = true;
        _current = new CancellationTokenSource();
        Status = "正在执行 TEST Mock（无物理输出）";
        Trace("Run", "Info", "MOCK_RUN_REQUESTED",
            "已获得模拟工位；Scenario=" + SelectedScenario +
            "; Recipe=" + _preview.Recipe?.RecipeId +
            "; 交由独立 Mock Runner（尚非正式 RunCoordinator）",
            recipeRevision: _preview.Recipe?.Revision ?? "");
        try
        {
            var result = await _orchestrator.RunAsync(_publishedCatalog, _preview,
                ScanInput, LabOrigin.Mock, SelectedScenario, AllowRetest, _current.Token);
            LastRun = $"{result.Request.RunId:N} | {result.Status} | {result.ResultCode}";
            Trace("RunResult", result.Status == LabRunStatus.MockPass ? "Info" : "Warning",
                result.ResultCode, "模拟结果=" + result.Status +
                "; StepFact 条数=" + result.StepFacts.Count +
                "; ElapsedMs=" + (result.FinishedUtc - result.Request.StartedUtc).TotalMilliseconds.ToString("F0") +
                "; FactoryReleased=false",
                result.Request.RunId, recipeRevision: result.Request.RecipeRevision);
            for (var index = 0; index < result.StepFacts.Count; index++)
                Trace("MockStepFact", "Info", "MOCK_STEP_" + (index + 1),
                    "已接收模拟步骤事实（内容留在 RunRecord；诊断摘要不记录敏感数据）",
                    result.Request.RunId, recipeRevision: result.Request.RecipeRevision);
            History.Insert(0, new LabHistoryRow(result.Request.RunId.ToString("N"),
                result.FinishedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                result.Request.UnitKey, result.Request.RecipeId, result.Status.ToString(),
                result.ActualOrigin.ToString(), false));
            Status = result.Status == LabRunStatus.MockPass
                ? "MOCK PASS：仅代表模拟步骤结果；出厂放行仍禁止"
                : $"{result.Status}：{result.ResultCode}（不放行）";
            RaisePropertyChanged(nameof(MockRunCount));
            RaisePropertyChanged(nameof(DistinctMockUnits));
            RaisePropertyChanged(nameof(FactoryReleasedCount));
        }
        catch (Exception ex)
        {
            Status = "执行/记录失败：" + ex.GetType().Name + " - " + ex.Message;
            LastRun = "UNKNOWN：未确认记录成功，不可放行";
            Trace("RunOrLedger", "Error", "RUN_OR_RECORD_EXCEPTION",
                "运行/落档抛出 " + ex.GetType().Name + "，未声明持久成功");
        }
        finally
        {
            _current.Dispose(); _current = null; IsBusy = false;
        }
    }

    public async Task SaveDraftAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var saved = await _store.SaveDraftAsync(BuildDraft());
            DraftState = "草稿已保存 " + saved.Revision + "；生产仍使用已发布快照";
            Status = "草稿已保存（未经验证，不可投入真实生产）";
            Trace("Catalog", "Info", "DRAFT_SAVED", "草稿已保存，已发布快照未变化");
        }
        catch (Exception ex)
        {
            Status = "草稿保存失败：" + ex.Message;
            Trace("Catalog", "Error", "DRAFT_SAVE_FAILED", ex.GetType().Name);
        }
        finally { IsBusy = false; }
    }

    public async Task PublishAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var next = await _store.PublishAsync(BuildDraft());
            _publishedCatalog = next;
            RuleRevision = next.Revision;
            CatalogSource = "本地发布的 MOCK 配方（不能作为 Physical 认证）";
            DraftState = "发布成功：运行将锁定版本 " + next.Revision;
            _preview = null; MatchDetail = "版本已更新，请重新扫码";
            Status = "发布成功（仅允许 Mock 场景；未知正则交集需要人工审查）";
            Trace("Catalog", "Info", "CATALOG_PUBLISHED", "新的本地 MOCK 修订已发布");
        }
        catch (Exception ex)
        {
            Status = "发布阻断：" + ex.Message;
            Trace("Catalog", "Warning", "PUBLICATION_BLOCKED", ex.GetType().Name);
        }
        finally { IsBusy = false; }
    }

    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var published = await _store.LoadAsync();
            var draft = await _store.LoadDraftAsync();
            if (published is not null) _publishedCatalog = published;
            PopulateRows(draft ?? published ?? _publishedCatalog);
            RuleRevision = _publishedCatalog.Revision;
            CatalogSource = published is null ? "内置 MOCK 样例（非生产配方）" : "本地发布的 MOCK 配方";
            DraftState = draft is null ? "没有单独保存的草稿" : "已恢复草稿；未覆盖已发布版本";
            MatchDetail = "重新加载后请扫码预览"; _preview = null;
            Status = "配置已加载（Physical 始终禁止）";
            Trace("Catalog", "Info", "CATALOG_LOADED", "本地草稿/已发布 MOCK 修订加载完成");
        }
        catch (Exception ex)
        {
            Status = "配置加载失败：" + ex.Message;
            Trace("Catalog", "Error", "CATALOG_LOAD_FAILED", ex.GetType().Name);
        }
        finally { IsBusy = false; }
    }

    private void AddScanEvent(string raw, string code) => ScanEvents.Insert(0,
        new LabScanEventRow(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss"), raw, code, "Keyboard/Manual · Mock context"));

    private LabCatalog BuildDraft()
    {
        static LabArtifact MockAsset(string key) => new(key, "mock-editor-v1", "MOCK_ONLY",
            LabOrigin.Mock, !string.IsNullOrWhiteSpace(key));
        return new LabCatalog(_publishedCatalog.Revision, false,
            Rules.Select(x => new LabIdentityRule(x.RuleId, x.Pattern, x.IdentityFieldsCsv,
                x.Enabled, x.OverlapReviewId)).ToArray(),
            Recipes.Select(x => new LabRecipe(x.RecipeId, x.Revision, x.ModelField,
                x.CustomerField, MockAsset(x.MockTestProgramKey),
                MockAsset(x.MockStationProfileKey), null, false, x.Enabled,
                x.EffectiveFromUtc, x.EffectiveUntilUtc)).ToArray(),
            Samples.Select(x => new LabRuleSample(x.RawBarcode, x.ExpectedRuleId,
                x.ExpectedRecipeId, x.ObservedAtUtc)).ToArray());
    }

    private void PopulateRows(LabCatalog catalog)
    {
        Rules.Clear(); Recipes.Clear(); Samples.Clear();
        foreach (var x in catalog.IdentityRules)
            Rules.Add(new LabRuleRow { RuleId = x.RuleId, Pattern = x.Pattern,
                IdentityFieldsCsv = x.IdentityFieldsCsv, OverlapReviewId = x.OverlapReviewId,
                Enabled = x.Enabled });
        foreach (var x in catalog.Recipes)
            Recipes.Add(new LabRecipeRow { RecipeId = x.RecipeId, Revision = x.Revision,
                ModelField = x.ModelField, CustomerField = x.CustomerField,
                MockTestProgramKey = x.TestProgram.Key, MockStationProfileKey = x.StationProfile.Key,
                EffectiveFromUtc = x.EffectiveFromUtc,
                EffectiveUntilUtc = x.EffectiveUntilUtc,
                Enabled = x.Enabled });
        foreach (var x in catalog.Samples)
            Samples.Add(new LabSampleRow { RawBarcode = x.RawBarcode,
                ExpectedRuleId = x.ExpectedRuleId, ExpectedRecipeId = x.ExpectedRecipeId,
                ObservedAtUtc = x.ObservedAtUtc });
    }

    private IEnumerable<DelegateCommand> Commands() => new[] { PreviewCommand, RunMockCommand,
        AbortCommand, SaveDraftCommand, PublishCommand, LoadCommand, AddRuleCommand,
        RemoveRuleCommand, AddRecipeCommand, RemoveRecipeCommand, AddSampleCommand,
        RemoveSampleCommand, ReplaySamplesCommand, InspectOverlapCommand, CompareDraftCommand };

    private static LabCatalogStore CreateDefaultStore()
    {
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(folder)) folder = Path.GetTempPath();
        return new LabCatalogStore(Path.Combine(folder, "AutoTest", "lab-production", "catalog"));
    }
}
