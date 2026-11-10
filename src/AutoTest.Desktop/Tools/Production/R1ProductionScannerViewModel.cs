using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using AutoTest.Specialists.Scan.Lab;
using Prism.Commands;
using Prism.Mvvm;

namespace AutoTest.Desktop.Tools.Production;

public sealed record LabScanEventRow(string When, string Input, string Result, string Origin);

/// <summary>
/// The ONLY scanner model instantiated by formal R1 P12. Receives actual
/// serial bytes and matches an explicitly published catalog. No demo catalog,
/// deterministic runner, in-memory factory ledger or simulated production PASS.
/// </summary>
public sealed class R1ProductionScannerViewModel : BindableBase
{
    private readonly LabCatalogStore _catalogStore;
    private LabCatalog? _catalog;
    private string _input = "";
    private string _status = "等待真实 USB COM 扫码输入；未启动测试。";
    private string _match = "尚未收到条码";
    private string _source = "未加载已发布的真实规则/配方";
    private string _revision = "—";

    public R1ProductionScannerViewModel() : this(new LabCatalogStore(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AutoTest", "lab-production", "catalog"))) { }

    public R1ProductionScannerViewModel(LabCatalogStore catalogStore)
    {
        _catalogStore = catalogStore ?? throw new ArgumentNullException(nameof(catalogStore));
        PreviewCommand = new DelegateCommand(PreviewScan);
        LoadCommand = new DelegateCommand(() => _ = LoadPublishedAsync());
    }

    public DelegateCommand PreviewCommand { get; }
    public DelegateCommand LoadCommand { get; }
    public ObservableCollection<LabScanEventRow> ScanEvents { get; } = new();
    public string ScanInput { get => _input; set => SetProperty(ref _input, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string MatchDetail { get => _match; private set => SetProperty(ref _match, value); }
    public string CatalogSource { get => _source; private set => SetProperty(ref _source, value); }
    public string RuleRevision { get => _revision; private set => SetProperty(ref _revision, value); }

    public async Task LoadPublishedAsync()
    {
        // Never fall back to the former built-in Mock demonstration recipe.
        _catalog = null;
        try
        {
            var published = await _catalogStore.LoadAsync();
            if (published is null || !published.Published ||
                LabCatalogValidator.Validate(published, true).Count != 0 ||
                published.Recipes.Any(x => x.TestProgram.Source != LabOrigin.Physical ||
                    x.StationProfile.Source != LabOrigin.Physical))
            {
                CatalogSource = "缺少有效的已发布物理配方；不使用演示/Mock 资源。";
                RuleRevision = "—";
                MatchDetail = "CATALOG_NOT_READY";
                Status = "扫码可接收和记录，但没有正式配方，不允许据此启动生产测试。";
                return;
            }
            _catalog = published;
            CatalogSource = "本地已发布、校验通过的物理身份规则 / 配方";
            RuleRevision = published.Revision;
            MatchDetail = "正式规则已加载；等待下一次实际扫码。";
            Status = "已就绪：实际扫码与配方匹配（不会自动给出出厂结果）。";
        }
        catch (Exception e)
        {
            CatalogSource = "配方读取失败：" + e.Message;
            MatchDetail = "CATALOG_LOAD_FAILED";
            Status = "拒绝运行：配方文件无效或不可读取。";
        }
    }

    public void AcceptSerialBarcode(string barcode, string origin)
    {
        Process(barcode, origin);
    }

    public void PreviewScan() => Process(ScanInput, "Manual / operator entry");

    private void Process(string raw, string origin)
    {
        var admission = LabScannerAdmission.Admit(raw,
            dedicatedFieldFocused: true, busy: false);
        var code = admission.Code;
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
                MatchDetail = "真实条码已收到，但尚无经过验证的物理身份规则和工程配方。";
                Status = "没有启动自动测试；不使用演示配方。";
            }
            else
            {
                var match = LabIdentityMatcher.Match(_catalog,
                    admission.NormalizedText, _catalog.Revision);
                code = match.Code;
                MatchDetail = match.Accepted
                    ? "身份匹配：" + match.Identity!.UnitKey + "；配方：" +
                      match.Recipe!.RecipeId + " / r" + match.Recipe.Revision
                    : "身份拒绝：" + match.Code;
                Status = match.Accepted
                    ? "身份规则已匹配。尚需绑定当前工程 TEST 和正式生产记录；不自动放行。"
                    : "匹配失败；没有执行测试。";
            }
        }
        ScanEvents.Insert(0, new LabScanEventRow(
            DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            admission.Allowed ? admission.NormalizedText : raw, code, origin));
        while (ScanEvents.Count > 500) ScanEvents.RemoveAt(ScanEvents.Count - 1);
    }
}
