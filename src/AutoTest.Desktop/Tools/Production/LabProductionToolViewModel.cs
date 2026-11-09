using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
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

    public LabProductionToolViewModel(LabCatalogStore store, LabProductionOrchestrator orchestrator)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _publishedCatalog = LabDemoCatalog.Create();
        RuleRevision = _publishedCatalog.Revision;
        PopulateRows(_publishedCatalog);
        PreviewCommand = new DelegateCommand(PreviewScan, () => !IsBusy);
        RunMockCommand = new DelegateCommand(() => _ = RunMockAsync(), () => !IsBusy);
        AbortCommand = new DelegateCommand(() => _current?.Cancel(), () => IsBusy);
        SaveDraftCommand = new DelegateCommand(() => _ = SaveDraftAsync(), () => !IsBusy);
        PublishCommand = new DelegateCommand(() => _ = PublishAsync(), () => !IsBusy);
        LoadCommand = new DelegateCommand(() => _ = LoadAsync(), () => !IsBusy);
        AddRuleCommand = new DelegateCommand(() => Rules.Add(new LabRuleRow()), () => !IsBusy);
        RemoveRuleCommand = new DelegateCommand(() => { if (SelectedRule is not null) Rules.Remove(SelectedRule); }, () => !IsBusy);
        AddRecipeCommand = new DelegateCommand(() => Recipes.Add(new LabRecipeRow()), () => !IsBusy);
        RemoveRecipeCommand = new DelegateCommand(() => { if (SelectedRecipe is not null) Recipes.Remove(SelectedRecipe); }, () => !IsBusy);
        AddSampleCommand = new DelegateCommand(() => Samples.Add(new LabSampleRow()), () => !IsBusy);
        RemoveSampleCommand = new DelegateCommand(() => { if (SelectedSample is not null) Samples.Remove(SelectedSample); }, () => !IsBusy);
    }

    public ObservableCollection<LabRuleRow> Rules { get; } = new();
    public ObservableCollection<LabRecipeRow> Recipes { get; } = new();
    public ObservableCollection<LabSampleRow> Samples { get; } = new();
    public ObservableCollection<LabHistoryRow> History { get; } = new();
    public ObservableCollection<LabScanEventRow> ScanEvents { get; } = new();
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
            return;
        }
        _preview = LabIdentityMatcher.Match(_publishedCatalog, input.NormalizedText,
            _publishedCatalog.Revision);
        MatchDetail = _preview.Accepted
            ? $"唯一匹配：{_preview.Identity!.UnitKey} / {_preview.Recipe!.RecipeId} / 规则 {_preview.Identity.RuleId}"
            : $"阻断：{_preview.Code} [{string.Join(",", _preview.CandidateIds)}]";
        Status = _preview.Accepted ? "匹配成功（仅 MOCK 演示，尚未运行）" : "扫码匹配未通过，禁止执行";
        AddScanEvent(input.NormalizedText, _preview.Code);
    }

    public async Task RunMockAsync()
    {
        if (IsBusy) return;
        PreviewScan();
        if (_preview?.Accepted != true) return;
        var preflight = _orchestrator.Preflight(_publishedCatalog, _preview,
            LabOrigin.Mock, AllowRetest);
        if (!preflight.Ready)
        {
            Status = "预检拒绝：" + string.Join(" / ", preflight.Blockers);
            return;
        }
        IsBusy = true;
        _current = new CancellationTokenSource();
        Status = "正在执行 TEST Mock（无物理输出）";
        try
        {
            var result = await _orchestrator.RunAsync(_publishedCatalog, _preview,
                ScanInput, LabOrigin.Mock, SelectedScenario, AllowRetest, _current.Token);
            LastRun = $"{result.Request.RunId:N} | {result.Status} | {result.ResultCode}";
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
        }
        catch (Exception ex) { Status = "草稿保存失败：" + ex.Message; }
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
        }
        catch (Exception ex) { Status = "发布阻断：" + ex.Message; }
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
        }
        catch (Exception ex) { Status = "配置加载失败：" + ex.Message; }
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
        RemoveRuleCommand, AddRecipeCommand, RemoveRecipeCommand, AddSampleCommand, RemoveSampleCommand };

    private static LabCatalogStore CreateDefaultStore()
    {
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(folder)) folder = Path.GetTempPath();
        return new LabCatalogStore(Path.Combine(folder, "AutoTest", "lab-production", "catalog"));
    }
}
