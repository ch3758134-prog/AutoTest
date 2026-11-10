using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using AutoTest.Application.V1;
using AutoTest.Desktop.Tools.Testing;
using AutoTest.Specialists.Scan.Lab;
using Prism.Commands;
using Prism.Mvvm;

namespace AutoTest.Desktop.Tools.Production;

public sealed record LabScanEventRow(string When, string Input, string Result, string Origin);

/// <summary>
/// R1 real COM scan + explicit, revision-pinned project recipe. No simulated
/// identity, station, runner, factory PASS or in-memory factory ledger.
/// </summary>
public sealed class R1ProductionScannerViewModel : BindableBase
{
    private readonly LabCatalogStore _catalogStore;
    private LabCatalog? _catalog;
    private V1Workspace? _project;
    private string _input = "";
    private string _status = "等待真实 USB COM 扫码输入；未启动测试。";
    private string _match = "尚未收到条码";
    private string _source = "未加载已发布的真实规则/配方";
    private string _revision = "—";
    private string _rulePattern = "", _identityFields = "serial",
        _recipeId = "R1-EOL", _stationProfileFile = "",
        _stationProfileRevision = "1", _exampleBarcode = "";
    private bool _running;
    private string _lastRun = "尚无真实执行";
    private string _lastEvidence = "—";

    public R1ProductionScannerViewModel() : this(new LabCatalogStore(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AutoTest", "lab-production", "catalog"))) { }

    public R1ProductionScannerViewModel(LabCatalogStore catalogStore)
    {
        _catalogStore = catalogStore ?? throw new ArgumentNullException(nameof(catalogStore));
        PreviewCommand = new DelegateCommand(PreviewScan, () => !IsRunning);
        LoadCommand = new DelegateCommand(() => _ = LoadPublishedAsync(), () => !IsRunning);
        PublishRuleCommand = new DelegateCommand(() => _ = PublishCurrentProjectRuleAsync(), () => !IsRunning);
    }

    public event Action<R1PhysicalScanBinding>? PhysicalScanMatched;
    public DelegateCommand PreviewCommand { get; }
    public DelegateCommand LoadCommand { get; }
    public DelegateCommand PublishRuleCommand { get; }
    public ObservableCollection<LabScanEventRow> ScanEvents { get; } = new();
    public string ScanInput { get => _input; set => SetProperty(ref _input, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string MatchDetail { get => _match; private set => SetProperty(ref _match, value); }
    public string CatalogSource { get => _source; private set => SetProperty(ref _source, value); }
    public string RuleRevision { get => _revision; private set => SetProperty(ref _revision, value); }
    public string RulePattern { get => _rulePattern; set => SetProperty(ref _rulePattern, value); }
    public string IdentityFields { get => _identityFields; set => SetProperty(ref _identityFields, value); }
    public string RecipeId { get => _recipeId; set => SetProperty(ref _recipeId, value); }
    public string StationProfileFile { get => _stationProfileFile; set => SetProperty(ref _stationProfileFile, value); }
    public string StationProfileRevision { get => _stationProfileRevision; set => SetProperty(ref _stationProfileRevision, value); }
    public string ExampleBarcode { get => _exampleBarcode; set => SetProperty(ref _exampleBarcode, value); }
    public string LastRun { get => _lastRun; private set => SetProperty(ref _lastRun, value); }
    public string LastEvidence { get => _lastEvidence; private set => SetProperty(ref _lastEvidence, value); }
    public bool IsRunning
    {
        get => _running;
        private set
        {
            if (!SetProperty(ref _running, value)) return;
            PreviewCommand.RaiseCanExecuteChanged();
            LoadCommand.RaiseCanExecuteChanged();
            PublishRuleCommand.RaiseCanExecuteChanged();
        }
    }

    public void AttachProject(V1Workspace project) =>
        _project = project ?? throw new ArgumentNullException(nameof(project));

    public async Task LoadPublishedAsync()
    {
        if (IsRunning) throw new InvalidOperationException("生产运行中不能更换规则。");
        _catalog = null;
        try
        {
            var published = await _catalogStore.LoadAsync();
            if (published is null || !published.Published ||
                LabCatalogValidator.Validate(published, true).Count != 0 ||
                published.Recipes.Any(x => x.TestProgram.Source != LabOrigin.Physical ||
                    x.StationProfile.Source != LabOrigin.Physical))
            {
                CatalogSource = "缺少已发布、有效的物理配方；不会使用演示资源。";
                RuleRevision = "—";
                MatchDetail = "CATALOG_NOT_READY";
                Status = "扫码可以接收，但没有正式配方，不能启动测试。";
                return;
            }
            _catalog = published;
            CatalogSource = "已发布的物理扫码规则 / 工程 TEST Revision 配方";
            RuleRevision = published.Revision;
            MatchDetail = "规则已载入，等待真实扫码。";
            Status = "已加载规则；每次身份匹配后核对当前工程和真实测试程序。";
        }
        catch (Exception ex)
        {
            CatalogSource = "规则读取失败：" + ex.Message;
            MatchDetail = "CATALOG_LOAD_FAILED";
            Status = "规则数据无效，不启动测试。";
        }
    }

    /// <summary>
    /// Explicit operator configuration. Station profile is a real local file
    /// fingerprint, NOT an assertion that its hardware has passed validation.
    /// </summary>
    public async Task PublishCurrentProjectRuleAsync()
    {
        if (IsRunning) return;
        try
        {
            var project = _project ?? throw new InvalidOperationException("PROJECT_NOT_BOUND");
            if (project.ProjectPath is null || project.LabProgramAssetId is null ||
                project.DbcAssetId is null || project.StaleDependencies.Count != 0)
                throw new InvalidOperationException("PROJECT_TEST_DBC_NOT_SAVED");
            if (string.IsNullOrWhiteSpace(StationProfileFile) ||
                !File.Exists(StationProfileFile))
                throw new InvalidOperationException("STATION_PROFILE_FILE_REQUIRED");
            var stationInfo = new FileInfo(StationProfileFile);
            if (stationInfo.Length is <= 0 or > 4 * 1024 * 1024)
                throw new InvalidDataException("STATION_PROFILE_FILE_INVALID");
            var original = await File.ReadAllBytesAsync(StationProfileFile);
            var stationSha = Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant();
            var program = project.Assets.Single(x => x.Id == project.LabProgramAssetId.Value);
            var recipe = new LabRecipe(RecipeId.Trim(), "1", "", "",
                new LabArtifact(program.Id.Value.ToString("D"),
                    program.Revision.ToString(CultureInfo.InvariantCulture),
                    program.Sha256, LabOrigin.Physical, true),
                new LabArtifact(Path.GetFullPath(StationProfileFile), StationProfileRevision.Trim(),
                    stationSha, LabOrigin.Physical, true),
                null, false, true);
            var candidate = new LabCatalog("draft", false,
                new[] { new LabIdentityRule("R1-PHYSICAL-SCAN", RulePattern.Trim(),
                    IdentityFields.Trim(), true) },
                new[] { recipe },
                new[] { new LabRuleSample(ExampleBarcode.Trim(),
                    "R1-PHYSICAL-SCAN", recipe.RecipeId) });
            var errors = LabCatalogValidator.Validate(candidate, true);
            if (errors.Count != 0)
                throw new InvalidOperationException("CATALOG_INVALID: " + string.Join("; ", errors));
            var published = await _catalogStore.PublishAsync(candidate);
            _catalog = published;
            CatalogSource = "物理规则已发布，工程 TEST " + program.Id.Value +
                " r" + program.Revision + " SHA256=" + program.Sha256;
            RuleRevision = published.Revision;
            MatchDetail = "已发布当前工程绑定规则；等待扫码。";
            Status = "规则发布完成；仍以实际设备会话和 TEST 运行结果为准。";
        }
        catch (Exception e)
        {
            Status = "发布拒绝：" + e.Message;
        }
    }

    public void AcceptSerialBarcode(string barcode, string origin) => Process(barcode, origin);
    public void PreviewScan() => Process(ScanInput, "Manual / operator entry");

    public void MarkRunCompleted(R1PhysicalScanBinding binding, Guid? runId,
        string result, string? evidence)
    {
        IsRunning = false;
        LastRun = (runId?.ToString("D") ?? "无 RunId") + " · " + result +
            " · DUT=" + binding.UnitKey;
        LastEvidence = string.IsNullOrWhiteSpace(evidence) ? "无完整证据" : evidence;
        Status = "扫码 TEST 已结束；判定为真实测试结果，尚未授权出厂放行。";
    }

    private void Process(string raw, string origin)
    {
        if (IsRunning)
        {
            ScanEvents.Insert(0, new LabScanEventRow(DateTimeOffset.Now.ToString("O"),
                raw, "STATION_BUSY", origin));
            Status = "当前真实测试运行中，拒绝覆盖本次 DUT 身份。";
            return;
        }
        var admission = LabScannerAdmission.Admit(raw, dedicatedFieldFocused: true, busy: false);
        var code = admission.Code;
        R1PhysicalScanBinding? binding = null;
        if (!admission.Allowed)
        {
            MatchDetail = "输入拒绝：" + code;
            Status = "扫码未通过格式检查；没有执行测试。";
        }
        else
        {
            ScanInput = admission.NormalizedText;
            if (_catalog is null)
            {
                code = "CATALOG_NOT_READY";
                MatchDetail = "收到真实条码，但没有已发布的物理规则。";
                Status = "没有启动自动测试。";
            }
            else
            {
                var match = LabIdentityMatcher.Match(_catalog, admission.NormalizedText, _catalog.Revision);
                code = match.Code;
                MatchDetail = match.Accepted
                    ? "身份匹配：" + match.Identity!.UnitKey + "；配方：" +
                      match.Recipe!.RecipeId + " / r" + match.Recipe.Revision
                    : "身份拒绝：" + match.Code;
                if (match.Accepted)
                {
                    try
                    {
                        binding = R1PhysicalScanBinding.FromMatch(match,
                            _project ?? throw new InvalidOperationException("PROJECT_NOT_BOUND"));
                        code = "PROJECT_TEST_MATCHED";
                        Status = "真实扫码和当前工程 TEST 版本一致，交由唯一真实运行会话。";
                    }
                    catch (Exception ex)
                    {
                        code = "PROJECT_TEST_BINDING_INVALID";
                        MatchDetail = "当前工程/测试程序与扫码配方不匹配：" + ex.Message;
                        Status = "拒绝运行，请检查工程保存状态及配方 Revision。";
                    }
                }
                else Status = "身份匹配失败；没有执行测试。";
            }
        }
        ScanEvents.Insert(0, new LabScanEventRow(DateTimeOffset.Now.ToString("O"),
            admission.Allowed ? admission.NormalizedText : raw, code, origin));
        while (ScanEvents.Count > 500) ScanEvents.RemoveAt(ScanEvents.Count - 1);
        if (binding is not null)
        {
            IsRunning = true;
            try { PhysicalScanMatched?.Invoke(binding); }
            catch (Exception ex)
            {
                IsRunning = false;
                Status = "真实运行交接失败：" + ex.Message;
            }
            if (PhysicalScanMatched is null)
            {
                IsRunning = false;
                Status = "未绑定正式 TEST 运行器，扫码只记录，不执行。";
            }
        }
    }
}
