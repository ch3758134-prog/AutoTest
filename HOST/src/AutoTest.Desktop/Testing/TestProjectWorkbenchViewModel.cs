using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Threading;
using System.Windows;
using AutoTest.Desktop.Projects;
using AutoTest.Desktop.Hardware;
using AutoTest.Testing.Preparation;
using AutoTest.Testing.Runtime;
using AutoTest.Core;
using AutoTest.Projects.Resources;
using AutoTest.Testing.Authoring;
using Prism.Commands;
using Prism.Mvvm;

namespace AutoTest.Desktop.Testing;

public sealed record TestProjectPackageChoice(AssetEntry Asset)
{
    public string Label => Asset.Name;
}

public sealed class TestProjectPackageRow : BindableBase
{
    private bool _enabled;
    private bool _runSelected;
    private int _order;
    private string _stepCountLabel = "读取中";
    private int _revision;
    private string _sha256;
    private int? _latestRevision;
    private string? _latestSha256;
    public string StepCountLabel { get => _stepCountLabel; set => SetProperty(ref _stepCountLabel, value); }
    public int Order { get => _order; set => SetProperty(ref _order, value); }
    public Guid ItemId { get; }
    public Guid AssetId { get; }
    public int Revision { get => _revision; private set => SetProperty(ref _revision, value); }
    public string Sha256 { get => _sha256; private set => SetProperty(ref _sha256, value); }
    public int? LatestRevision => _latestRevision;
    public string? LatestSha256 => _latestSha256;
    // Only strictly newer revisions can be adopted by the formal project.
    // An unchanged revision with a different SHA256 is a broken provenance.
    public bool CanAdoptLatest => _latestRevision > Revision &&
        !string.IsNullOrWhiteSpace(_latestSha256);
    public bool HasReferenceMismatch => _latestRevision is null ||
        !MatchesPinnedRevision(_latestRevision.Value, _latestSha256);
    public string PinStatus => _latestRevision is null ? "资源已移除" :
        _latestRevision < Revision ? "资源修订倒退 · 禁止自动回退" :
        _latestRevision == Revision && !MatchesPinnedRevision(_latestRevision.Value, _latestSha256)
            ? "同修订 SHA256 不一致 · 需修复" :
        CanAdoptLatest ? "绑定 r" + Revision + " → 当前 r" + _latestRevision + " · 待确认" : "修订一致";
    public string PinDetail => _latestRevision is null ? "当前工程已找不到此 TEST 定义" :
        "项目固定 r" + Revision + " / " + Sha256 +
        "；当前资源 r" + _latestRevision + " / " + _latestSha256;
    public bool MatchesPinnedRevision(int revision, string? sha256) =>
        revision == Revision && !string.IsNullOrWhiteSpace(sha256) &&
        string.Equals(sha256, Sha256, StringComparison.OrdinalIgnoreCase);

    public void RefreshAvailableRevision(int? revision, string? sha256)
    {
        // A background count/metadata refresh must not invalidate an already
        // prepared physical run when its actual resource revision did not change.
        if (_latestRevision == revision &&
            string.Equals(_latestSha256, sha256, StringComparison.OrdinalIgnoreCase))
            return;
        _latestRevision = revision;
        _latestSha256 = sha256;
        RaisePropertyChanged(nameof(LatestRevision));
        RaisePropertyChanged(nameof(LatestSha256));
        RaisePropertyChanged(nameof(CanAdoptLatest));
        RaisePropertyChanged(nameof(HasReferenceMismatch));
        RaisePropertyChanged(nameof(PinStatus));
        RaisePropertyChanged(nameof(PinDetail));
    }

    public void AdoptCurrentRevision()
    {
        if (!CanAdoptLatest || _latestRevision is not { } revision ||
            string.IsNullOrWhiteSpace(_latestSha256)) return;
        Revision = revision;
        Sha256 = _latestSha256;
        RaisePropertyChanged(nameof(CanAdoptLatest));
        RaisePropertyChanged(nameof(HasReferenceMismatch));
        RaisePropertyChanged(nameof(PinStatus));
        RaisePropertyChanged(nameof(PinDetail));
    }
    public string Name { get; }
    public TestProjectPackageRow(TestProjectEntry item)
    {
        ItemId = item.ItemId;
        AssetId = item.PackageAssetId;
        _revision = item.PackageRevision;
        _sha256 = item.PackageSha256;
        Name = item.DisplayName;
        _enabled = item.Enabled;
        _runSelected = item.Enabled;
    }
    /// <summary>Engineer-only selection: never serialized as production Enabled.</summary>
    public bool RunSelected
    {
        get => _runSelected;
        set => SetProperty(ref _runSelected, value);
    }
    /// <summary>
    /// Persisted project scope: newly added entries are included by default.
    /// Exclusion is an exceptional engineer decision, not a second enable step.
    /// It is unrelated to temporary RunSelected debug choices.
    /// </summary>
    public bool ExcludedFromAutomaticTest
    {
        get => !_enabled;
        set => Enabled = !value;
    }
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (SetProperty(ref _enabled, value))
                RaisePropertyChanged(nameof(ExcludedFromAutomaticTest));
        }
    }
    public TestProjectEntry ToEntry() => new(ItemId, AssetId, Revision, Sha256, Name, Enabled);
}

public sealed class TestProjectWorkbenchViewModel : BindableBase
{
    private readonly ProjectWorkspaceService _workspace;
    private readonly TestEditorDialogs _dialogs;
    private readonly ProjectRunSelectionState? _selection;
    private readonly RealTestWorkbenchService? _realTest;
    private readonly HardwareConnectionService? _hardware;
    private sealed record FrozenSelectedCase(TestProjectEntry Entry, TestDocument Document,
        string PlanSha256, string TestConsent, string FlashConsent);
    private readonly List<FrozenSelectedCase> _frozen = new();
    private long _frozenStationEpoch;
    private string? _frozenManifestSha;
    private string _frozenDut = "";
    private string _dutIdentity = "";
    private string _flashApproval = "";
    private string _expectedFlashApproval = "";
    private CancellationTokenSource? _batchCancellation;
    public ObservableCollection<string> BatchResults { get; } = new();
    public string DutIdentity
    {
        get => _dutIdentity;
        set { if (SetProperty(ref _dutIdentity, value) && !IsBusy) ResetFrozenSelection(); }
    }
    public string FlashApproval { get => _flashApproval; set => SetProperty(ref _flashApproval, value); }
    public string ExpectedFlashApproval
    {
        get => _expectedFlashApproval;
        private set => SetProperty(ref _expectedFlashApproval, value);
    }
    public bool IsPrepared => _frozen.Count > 0;
    private TestProjectEditorSession _session;
    private bool _busy;
    private string _name;
    private TestProjectEntry? _projectSetup, _projectTeardown;
    public string ProjectSetupLabel => _projectSetup is null ? "未设置（可选）"
        : _projectSetup.DisplayName + " · r" + _projectSetup.PackageRevision;
    public string ProjectTeardownLabel => _projectTeardown is null ? "未设置（可选）"
        : _projectTeardown.DisplayName + " · r" + _projectTeardown.PackageRevision;
    private string _status = "从右侧选择测试用例并加入执行列表。";
    private TestProjectPackageChoice? _selectedAvailable;
    private TestProjectPackageRow? _selectedPackage;
    public Action<Guid>? OpenPackageRequested { get; set; }
    public Func<Task>? CreateNewPackageRequested { get; set; }
    public Action? PackageRowsChanged { get; set; }
    public void OpenSelectedPackage()
    {
        if (SelectedPackage is { } row) OpenPackageRequested?.Invoke(row.AssetId);
    }

    /// <summary>Focus an existing invocation without updating a revision or
    /// implying that its latest resource is safe to use.</summary>
    public bool FocusInvocation(Guid invocationId)
    {
        var row = Packages.FirstOrDefault(x => x.ItemId == invocationId);
        if (row is null) return false;
        SelectedPackage = row;
        return true;
    }
    public void OpenPackage(TestProjectPackageRow row)
    {
        if (Packages.Contains(row)) OpenPackageRequested?.Invoke(row.AssetId);
    }
    public void MoveRow(TestProjectPackageRow row, int direction)
    {
        if (!Packages.Contains(row)) return;
        SelectedPackage = row;
        Move(direction);
    }
    public async Task RemoveRowAsync(TestProjectPackageRow row)
    {
        if (!Packages.Contains(row)) return;
        SelectedPackage = row;
        await RemoveSelectedPackageAsync();
    }

    private readonly DispatcherTimer _autosaveTimer = new() { Interval = TimeSpan.FromMilliseconds(850) };

    public TestProjectWorkbenchViewModel(ProjectWorkspaceService workspace,
        TestEditorDialogs dialogs, TestProjectEditorSession session,
        ProjectRunSelectionState? selection = null,
        RealTestWorkbenchService? realTest = null, HardwareConnectionService? hardware = null)
    {
        _workspace = workspace; _dialogs = dialogs; _session = session;
        _autosaveTimer.Tick += async (_, _) =>
        {
            _autosaveTimer.Stop();
            // Invalid or transient user input must remain visible; never create
            // an unwanted project revision while the operator is still typing.
            if (IsBusy || !IsDirty || _realTest?.IsRunning == true ||
                _hardware?.IsTestRunApproved == true || string.IsNullOrWhiteSpace(Name)) return;
            try { await SaveAsync(silent: true); }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                StatusMessage = "测试项目自动保存失败，修改仍保留：" + error.Message;
                System.Diagnostics.Trace.TraceWarning("TEST project autosave failed: " + error);
            }
        };
        _selection = selection; _realTest = realTest; _hardware = hardware;
        if (_selection is not null) _selection.Changed += OnNavigationSelectionChanged;
        _name = session.Document.Name;
        AddCommand = new DelegateCommand(Add, () => SelectedAvailable is not null && !IsBusy);
        CreateProjectSetupCommand = new DelegateCommand(
            async () => await CreateProjectStageAsync(true), () => !IsBusy);
        CreateProjectTeardownCommand = new DelegateCommand(
            async () => await CreateProjectStageAsync(false), () => !IsBusy);
        SetProjectSetupCommand = new DelegateCommand(() => SetProjectStage(true),
            () => SelectedAvailable is not null && !IsBusy);
        SetProjectTeardownCommand = new DelegateCommand(() => SetProjectStage(false),
            () => SelectedAvailable is not null && !IsBusy);
        OpenProjectSetupCommand = new DelegateCommand(() => OpenProjectStage(true),
            () => _projectSetup is not null && !IsBusy);
        OpenProjectTeardownCommand = new DelegateCommand(() => OpenProjectStage(false),
            () => _projectTeardown is not null && !IsBusy);
        RemoveProjectSetupCommand = new DelegateCommand(() => RemoveProjectStage(true),
            () => _projectSetup is not null && !IsBusy);
        RemoveProjectTeardownCommand = new DelegateCommand(() => RemoveProjectStage(false),
            () => _projectTeardown is not null && !IsBusy);
        CreateNewPackageCommand = new DelegateCommand(async () =>
        {
            if (!IsBusy && CreateNewPackageRequested is { } create) await create();
        }, () => !IsBusy);
        RemoveCommand = new DelegateCommand(async () => await RemoveSelectedPackageAsync(),
            () => SelectedPackage is not null && !IsBusy);
        UpCommand = new DelegateCommand(() => Move(-1), () => CanMove(-1));
        DownCommand = new DelegateCommand(() => Move(1), () => CanMove(1));
        SaveCommand = new DelegateCommand(async () => await SaveAsync(), () => !IsBusy && IsDirty);
        ReloadCommand = new DelegateCommand(async () => await ReloadAsync(), () => !IsBusy);
        SelectAllCommand = new DelegateCommand(() => SetSelection(true));
        ClearSelectionCommand = new DelegateCommand(() => SetSelection(false));
        PreflightSelectedCommand = new DelegateCommand(async () => await PrepareSelectedAsync(),
            () => !IsBusy);
        RunSelectedCommand = new DelegateCommand(async () => await RunSelectedAsync(),
            () => !IsBusy && IsPrepared && !IsDirty);
        StopSelectedCommand = new DelegateCommand(StopSelected,
            () => _batchCancellation is not null);
        LoadRows(session.Document);
        RefreshAvailable();
        RefreshReferences();
    }

    public ObservableCollection<TestProjectPackageChoice> AvailablePackages { get; } = new();
    public ObservableCollection<TestProjectPackageRow> Packages { get; } = new();
    public string Name
    {
        get => _name;
        set { if (SetProperty(ref _name, value)) RefreshCommands(); }
    }
    public string AssetName => _session.AssetName;
    public string FilePath => _session.FilePath;
    public int Revision => _session.AssetRevision;
    public string StatusMessage { get => _status; private set => SetProperty(ref _status, value); }
    public bool IsBusy
    {
        get => _busy;
        private set { if (SetProperty(ref _busy, value)) RefreshCommands(); }
    }
    // Dirty detection is valid even while the user is midway through typing a name.
    // Never serialize+validate on each TextBox keystroke.
    public bool IsDirty => !string.Equals(Name, _session.Document.Name, StringComparison.Ordinal) ||
        !Packages.Select(x => x.ToEntry()).SequenceEqual(_session.Document.Packages) ||
        !Equals(_projectSetup, _session.Document.ProjectSetup) ||
        !Equals(_projectTeardown, _session.Document.ProjectTeardown);
    public TestProjectPackageChoice? SelectedAvailable
    {
        get => _selectedAvailable;
        set { if (SetProperty(ref _selectedAvailable, value)) RefreshCommands(); }
    }
    public TestProjectPackageRow? SelectedPackage
    {
        get => _selectedPackage;
        set { if (SetProperty(ref _selectedPackage, value)) RefreshCommands(); }
    }

    public DelegateCommand AddCommand { get; }
    public DelegateCommand CreateProjectSetupCommand { get; }
    public DelegateCommand CreateProjectTeardownCommand { get; }
    public DelegateCommand SetProjectSetupCommand { get; }
    public DelegateCommand SetProjectTeardownCommand { get; }
    public DelegateCommand OpenProjectSetupCommand { get; }
    public DelegateCommand OpenProjectTeardownCommand { get; }
    public DelegateCommand RemoveProjectSetupCommand { get; }
    public DelegateCommand RemoveProjectTeardownCommand { get; }
    public DelegateCommand CreateNewPackageCommand { get; }
    public DelegateCommand RemoveCommand { get; }
    public DelegateCommand UpCommand { get; }
    public DelegateCommand DownCommand { get; }
    public DelegateCommand SaveCommand { get; }
    public DelegateCommand ReloadCommand { get; }
    public DelegateCommand SelectAllCommand { get; }
    public DelegateCommand ClearSelectionCommand { get; }
    public DelegateCommand PreflightSelectedCommand { get; }
    public DelegateCommand RunSelectedCommand { get; }
    public DelegateCommand StopSelectedCommand { get; }
    public int SelectedCount => Packages.Count(x => x.RunSelected);
    public int TotalCount => Packages.Count;
    public string SelectionSummary => SelectedCount + " / " + TotalCount + " 个用例（本次实际运行范围）";

    private void Renumber()
    {
        for (var i = 0; i < Packages.Count; i++) Packages[i].Order = i + 1;
    }
    private void OnNavigationSelectionChanged(Guid projectId)
    {
        if (projectId != _session.AssetId.Value) return;
        foreach (var row in Packages)
        {
            var value = _selection!.Get(projectId, row.ItemId, row.Enabled).IsSelected;
            if (row.RunSelected != value) row.RunSelected = value;
        }
    }

    private void LoadRows(TestProjectDocument doc)
    {
        _projectSetup = doc.ProjectSetup;
        _projectTeardown = doc.ProjectTeardown;
        RaisePropertyChanged(nameof(ProjectSetupLabel));
        RaisePropertyChanged(nameof(ProjectTeardownLabel));
        foreach (var row in Packages) row.PropertyChanged -= RowChanged;
        Packages.Clear();
        foreach (var item in doc.Packages)
        {
            var row = new TestProjectPackageRow(item);
            if (_selection is not null)
                row.RunSelected = _selection.Get(_session.AssetId.Value, row.ItemId, row.Enabled).IsSelected;
            row.PropertyChanged += RowChanged;
            Packages.Add(row);
        }
        Renumber();
        ResetFrozenSelection();
        SelectedPackage = null;
        RaisePropertyChanged(nameof(SelectedCount));
        RaisePropertyChanged(nameof(TotalCount));
        RaisePropertyChanged(nameof(SelectionSummary));
        RaisePropertyChanged(nameof(IsDirty));
        PackageRowsChanged?.Invoke();
        RefreshReferences();
    }
    private void RowChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Step counts and derived labels are informational. A late asynchronous
        // count refresh must NEVER clear a frozen test batch or disable Run.
        if (e.PropertyName is nameof(TestProjectPackageRow.StepCountLabel) or
            nameof(TestProjectPackageRow.PinStatus) or
            nameof(TestProjectPackageRow.PinDetail) or
            nameof(TestProjectPackageRow.CanAdoptLatest) or
            nameof(TestProjectPackageRow.HasReferenceMismatch))
            return;
        if (!IsBusy) ResetFrozenSelection();
        if (e.PropertyName == nameof(TestProjectPackageRow.RunSelected))
        {
            if (sender is TestProjectPackageRow row && _selection is not null)
                _selection.Get(_session.AssetId.Value, row.ItemId, row.Enabled).IsSelected = row.RunSelected;
            RaisePropertyChanged(nameof(SelectedCount));
            RaisePropertyChanged(nameof(SelectionSummary));
        }
        RefreshCommands();
        PackageRowsChanged?.Invoke();
    }
    private void SetSelection(bool select)
    {
        foreach (var row in Packages) row.RunSelected = select;
        StatusMessage = select ? "已选择本项目所有用例，仅影响本次检查。" :
            "已清空本次检查选择；工程默认未修改。";
        PackageRowsChanged?.Invoke();
    }
    private void ResetFrozenSelection()
    {
        if (_batchCancellation is not null) return;
        _frozen.Clear();
        _frozenManifestSha = null;
        _frozenDut = "";
        ExpectedFlashApproval = "";
        FlashApproval = "";
        RaisePropertyChanged(nameof(IsPrepared));
        RunSelectedCommand?.RaiseCanExecuteChanged();
    }

    /// <summary>Freeze every selected and revision-pinned package before ANY
    /// physical TEST authorization. There is no second TEST executor here.</summary>
    private async Task PrepareSelectedAsync()
    {
        if (IsBusy) return;
        ResetFrozenSelection();
        if (_realTest is null || _hardware is null)
        {
            StatusMessage = "BLOCKED_BY_BACKEND：项目批量真实执行服务尚未注入。";
            return;
        }
        if (IsDirty)
        {
            await SaveAsync(silent: true);
            if (IsDirty)
            {
                StatusMessage = "当前测试项目有未保存或无效修改，请先修正后重试检查。";
                return;
            }
        }
        if (DutIdentity.Length > 0 &&
            (string.IsNullOrWhiteSpace(DutIdentity) || DutIdentity != DutIdentity.Trim() ||
             DutIdentity.Length > 256 || DutIdentity.Any(char.IsControl)))
        {
            StatusMessage = "ECU 编号格式不合法；开发批次允许留空，按未绑定 DUT 诊断记录归档。";
            return;
        }
        var selected = Packages.Where(x => x.RunSelected).ToArray();
        if (selected.Length == 0)
        {
            StatusMessage = "至少勾选一个测试用例。";
            return;
        }
        if (!_hardware.IsConnected || _hardware.CurrentStationEpoch <= 0)
        {
            StatusMessage = "BLOCKED_BY_HW：先连接真实 USB 主控，不能使用模拟设备。";
            return;
        }
        IsBusy = true;
        try
        {
            var context = _workspace.Current ??
                throw new InvalidOperationException("工程已经关闭。");
            var projectAsset = context.Definition.Assets.SingleOrDefault(a =>
                a.Id == _session.AssetId && a.Kind == ProjectWorkspaceService.TestProjectKind);
            if (projectAsset is null || projectAsset.Revision != _session.AssetRevision ||
                !projectAsset.Sha256.Equals(_session.FileSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("测试项目自身修订已变化；请重新打开并确认。");

            var fixedManifest = context.ManifestSha256;
            var fixedEpoch = _hardware.CurrentStationEpoch;
            var prepared = new List<FrozenSelectedCase>();
            string flashConsent = "";
            foreach (var row in selected)
            {
                var asset = context.Definition.Assets.FirstOrDefault(a =>
                    a.Id.Value == row.AssetId && a.Kind == ProjectWorkspaceService.TestProgramKind);
                if (asset is null)
                    throw new InvalidDataException("用例文件不存在：" + row.Name);
                if (!row.MatchesPinnedRevision(asset.Revision, asset.Sha256))
                    throw new InvalidDataException("测试用例“" + row.Name + "”的固定引用与当前 TEST 资源不一致：" +
                        "项目 r" + row.Revision + "，当前资源 r" + asset.Revision + "。" +
                        (row.CanAdoptLatest
                            ? "请在用例列表核对并明确采用新的受控修订。"
                            : "资源丢失、倒退或同版本 SHA256 异常时禁止更新引用；请先恢复或重新建立合规 TEST 资产。"));
                var opened = await _workspace.OpenTestEditorAsync(AssetId.Create(row.AssetId));
                if (!row.MatchesPinnedRevision(opened.AssetRevision, opened.FileHash))
                    throw new IOException("准备时测试用例“" + row.Name + "”的内容或修订已变化，请重新检查。");
                var plan = _realTest.Prepare(opened.Document);
                if (!plan.Diagnostics.IsEmpty)
                    throw new InvalidDataException(row.Name + " 步骤预检：" +
                        string.Join("; ", plan.Diagnostics));
                var runDocument = plan.RestoreDocument();
                var mappings = TestRunStepScope.RequiredMappings(runDocument);
                var actionCheck = new ConfiguredStationActionPort(_hardware, _workspace);
                await actionCheck.PrepareAsync(mappings, CancellationToken.None);
                actionCheck.ValidateRunSteps(runDocument);

                var jobs = ConfiguredRealTestFlashPort.FreezeJobs(opened.Document, context.RootDirectory);
                var caseFlash = jobs.Count == 0 ? "" : jobs.Values.Single().Consent;
                if (caseFlash.Length > 0)
                {
                    if (flashConsent.Length > 0)
                        throw new NotSupportedException(
                            "R1 首版一个批次只允许一个 FLASH；请拆为独立批次分别授权。");
                    flashConsent = caseFlash;
                }
                prepared.Add(new FrozenSelectedCase(row.ToEntry(), runDocument,
                    plan.Fingerprint, RealTestWorkbenchService.ExpectedApproval(plan), caseFlash));
            }
            if (!ReferenceEquals(context, _workspace.Current) ||
                context.ManifestSha256 != fixedManifest ||
                _hardware.CurrentStationEpoch != fixedEpoch ||
                !_hardware.IsConnected)
                throw new IOException("准备期间工程或设备会话已变化，必须重新确认。");

            _frozen.AddRange(prepared);
            _frozenManifestSha = fixedManifest;
            _frozenStationEpoch = fixedEpoch;
            _frozenDut = DutIdentity;
            ExpectedFlashApproval = flashConsent;
            FlashApproval = "";
            BatchResults.Clear();
            StatusMessage = "已冻结 " + _frozen.Count + " 条选中用例的修订、DBC/KEY 和实际步骤资源。" +
                (flashConsent.Length == 0 ? " 可运行。" : " 运行前需手动输入唯一 FLASH 授权。");
            RaisePropertyChanged(nameof(IsPrepared));
        }
        catch (Exception ex)
        {
            // UI preflight performs no physical writes. Format/profile/DLL/unknown
            // resource errors must be reported inline instead of escaping an
            // async WPF DelegateCommand and crashing the application.
            ResetFrozenSelection();
            StatusMessage = "BLOCKED_PREFLIGHT：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
            RunSelectedCommand.RaiseCanExecuteChanged();
        }
    }

    private void StopSelected()
    {
        _batchCancellation?.Cancel();
        _realTest?.RequestStop();
        StatusMessage = "已请求停止；当前真实步骤将中止并尝试工位清理。";
    }

    /// <summary>Engineer RunSelected is a temporary ordered scope. All
    /// selected functional TEST cases share one real physical grant and CAN
    /// sender set, with each case retaining its own RunId/CaseAttempt/RECORD.
    /// A solitary explicit FLASH debug case stays isolated under its original
    /// destructive authorization; FLASH cannot be mixed with a continuous
    /// functional batch.</summary>
    private async Task RunSelectedAsync()
    {
        if (IsBusy || IsDirty || _realTest is null || _hardware is null ||
            _frozen.Count == 0 || _frozenManifestSha is null) return;
        if (DutIdentity != _frozenDut ||
            _hardware.CurrentStationEpoch != _frozenStationEpoch || !_hardware.IsConnected ||
            _workspace.Current?.ManifestSha256 != _frozenManifestSha ||
            !Packages.Where(x => x.RunSelected).Select(x => x.ItemId)
                .SequenceEqual(_frozen.Select(x => x.Entry.ItemId)))
        {
            ResetFrozenSelection();
            StatusMessage = "工程、用例选择、ECU 编号或主控会话发生变化，请重新核对。";
            return;
        }
        if (ExpectedFlashApproval.Length > 0 &&
            !string.Equals(FlashApproval, ExpectedFlashApproval, StringComparison.Ordinal))
        {
            StatusMessage = "FLASH 未授权；必须手动输入完整的 AUTHORIZE ECU FLASH + SHA256。";
            return;
        }

        var batch = _frozen.ToArray();
        var fixedFlashApproval = FlashApproval;
        if (batch.Length > 1 && batch.Any(x => x.FlashConsent.Length > 0))
        {
            StatusMessage = "开发勾选范围包含内嵌 FLASH。连续 TEST 不得与编程授权混用；" +
                "请先以独立工程烧录工艺完成 ECU 编程，再执行纯 TEST 勾选范围。";
            return;
        }
        _batchCancellation = new CancellationTokenSource();
        var token = _batchCancellation.Token;
        IsBusy = true;
        StopSelectedCommand.RaiseCanExecuteChanged();
        BatchResults.Clear();
        var index = 0;
        var functionalFailure = false;
        RealTestWorkbenchService.ProjectExecutionSession? shared = null;
        try
        {
            if (batch.All(x => x.FlashConsent.Length == 0))
            {
                shared = await _realTest.OpenProjectSessionAsync(
                    batch.Select(x => x.Document).ToArray(),
                    Guid.NewGuid(), _frozenDut, token,
                    allowOrderedSubset: true);
                BatchResults.Add("开发勾选范围共用一个真实硬件会话：" +
                    shared.ProjectRunId + "；周期 CAN 将跨用例保持。");
            }
            else
                BatchResults.Add("单用例 FLASH 调试：单独物理许可与清理；不作为正式生产。");

            foreach (var item in batch)
            {
                token.ThrowIfCancellationRequested();
                index++;
                if (!_hardware.IsConnected || _hardware.CurrentStationEpoch != _frozenStationEpoch ||
                    _workspace.Current?.ManifestSha256 != _frozenManifestSha ||
                    DutIdentity != _frozenDut)
                    throw new IOException("批次中途工程、ECU 编号或 USB 主控会话已变化。");
                var latest = await _workspace.OpenTestEditorAsync(AssetId.Create(item.Entry.PackageAssetId), token);
                if (latest.AssetRevision != item.Entry.PackageRevision ||
                    !latest.FileHash.Equals(item.Entry.PackageSha256, StringComparison.OrdinalIgnoreCase) ||
                    _realTest.Prepare(latest.Document).Fingerprint != item.PlanSha256)
                    throw new IOException("运行前 TEST 资源指纹已变动：" + item.Entry.DisplayName);

                StatusMessage = "正在执行 " + index + "/" + batch.Length + "：" +
                    item.Entry.DisplayName + "；真实步骤持续写入 RECORD。";
                void OnStep(RealStepFact fact)
                {
                    var dispatcher = global::System.Windows.Application.Current?.Dispatcher;
                    dispatcher?.BeginInvoke(new Action(() =>
                    {
                        if (_batchCancellation is not null)
                            StatusMessage = "用例 " + index + "/" + batch.Length + " · " +
                                fact.Type + "：" + fact.Status + " · " + fact.Detail;
                    }));
                }
                _realTest.StepReported += OnStep;
                RealRunResult result;
                try
                {
                    result = shared is not null
                        ? await shared.ExecuteCaseAsync(item.Document, token)
                        : await _realTest.ExecuteAsync(item.Document, item.TestConsent,
                            fixedFlashApproval, _frozenDut, token);
                }
                finally { _realTest.StepReported -= OnStep; }
                BatchResults.Add(index + ". " + item.Entry.DisplayName + " · " +
                    result.Verdict + " · RunId=" + result.Run.Value +
                    (result.EvidencePath is null ? "" : " · HTML=" + result.EvidencePath));
                if (result.Verdict == RealTestVerdict.Failed)
                {
                    // A normal function assertion failure never becomes PASS,
                    // but healthy later cases may collect further diagnosis.
                    // Prepare/Cleanup failures are not safe to continue.
                    if (result.Facts.Any(f => f.Type == "PrepareGate" ||
                        f.Phase is TestPhase.Prepare or TestPhase.Cleanup &&
                        f.Status is not (RealStepStatus.Completed or RealStepStatus.Skipped)))
                        throw new InvalidDataException("用例准备/收尾步骤失败，禁止继续：" +
                            item.Entry.DisplayName);
                    functionalFailure = true;
                    BatchResults.Add("功能 FAIL 已保留；继续后续已冻结的调试用例。");
                }
                else if (result.Verdict is not (RealTestVerdict.Passed or
                    RealTestVerdict.CompletedNoAssertion))
                    throw new InvalidDataException("当前用例数据无效或运行被中止，" +
                        "不能安全继续后续物理动作：" +
                        item.Entry.DisplayName + " · " + result.Verdict);
                if (shared is not null) await shared.Actions.CheckHealthAsync(token);
            }
            shared?.MarkCompleted();
            StatusMessage = functionalFailure
                ? "勾选范围结束：存在真实功能 FAIL，各用例证据已保留；非生产放行。"
                : "勾选范围执行完成：每个用例均有独立记录；动作完成不等于 ECU PASS。";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "批次已取消。已运行的用例报告保留，剩余用例未执行。";
        }
        catch (Exception error)
        {
            BatchResults.Add("中止于第 " + index + " 个用例：" + error.Message);
            StatusMessage = "批次异常；已停止后续用例，请检查最后一个 RunId、设备与报告。";
        }
        finally
        {
            if (shared is not null)
            {
                try { await shared.DisposeAsync(); }
                catch (Exception cleanupError)
                {
                    BatchResults.Add("项目周期发送/物理授权收尾失败：" +
                        cleanupError.Message);
                    StatusMessage = "设备清理或跨用例证据存在异常；状态 UNKNOWN，" +
                        "不能宣称调试/生产通过。";
                }
            }
            _batchCancellation?.Dispose();
            _batchCancellation = null;
            IsBusy = false;
            ResetFrozenSelection();
            StopSelectedCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// Called when TEST assets change or when returning to the project tab.
    /// Never silently rewrites TestProjectEntry.PackageRevision/PackageSha256.
    /// </summary>
    public void RefreshReferences()
    {
        _ = SynchronizeSavedRevisionAsync();
        var project = _workspace.Current;
        foreach (var row in Packages)
        {
            var current = project is null ? null : project.Definition.Assets.FirstOrDefault(asset =>
                asset.Id.Value == row.AssetId &&
                asset.Kind == ProjectWorkspaceService.TestProgramKind);
            row.RefreshAvailableRevision(current?.Revision, current?.Sha256);
        }
        RaisePropertyChanged(nameof(HasStaleReferences));
        _ = RefreshStepCountsAsync();
    }

    public bool HasStaleReferences => Packages.Any(x => x.HasReferenceMismatch);

    public AssetEntry? CurrentAssetFor(TestProjectPackageRow row) =>
        _workspace.Current?.Definition.Assets.FirstOrDefault(x =>
            x.Id.Value == row.AssetId && x.Kind == ProjectWorkspaceService.TestProgramKind);

    /// <summary>Review the actual saved NEW TEST definition before changing a
    /// pinned invocation. The old content may not be retained by the current
    /// project format; never invent a semantic old/new diff.</summary>
    public async Task<(bool CanAdopt, string Summary)> ReviewCurrentRevisionAsync(
        TestProjectPackageRow row, int expectedRevision, string expectedSha)
    {
        if (IsBusy || !Packages.Contains(row) || !row.CanAdoptLatest)
            return (false, "当前调用实例没有可审核的新修订。");
        var asset = CurrentAssetFor(row);
        if (asset is null || asset.Revision != expectedRevision ||
            !string.Equals(asset.Sha256, expectedSha, StringComparison.OrdinalIgnoreCase))
            return (false, "待采用资源已变化；请重新核对工程资产。");
        try
        {
            var latest = await _workspace.OpenTestEditorAsync(AssetId.Create(row.AssetId));
            if (latest.AssetRevision != expectedRevision ||
                !string.Equals(latest.FileHash, expectedSha,
                    StringComparison.OrdinalIgnoreCase))
                return (false, "新版 TEST 内容在审查期间发生变化；禁止采用。");
            var plan = new PlanCompiler().Prepare(latest.Document,
                allowRealOperations: true);
            var steps = TestRunStepScope.EnabledSteps(latest.Document).ToArray();
            var mappings = TestRunStepScope.RequiredMappings(latest.Document).ToArray();
            // Earlier asset bytes are available only for edits recorded after
            // the immutable authoring history was introduced. This comparison
            // never falls back to the mutable current file for the old revision.
            var oldBytes = await ProjectResourceRevisionArchive.TryReadAsync(
                _workspace.Current!.RootDirectory, AssetId.Create(row.AssetId),
                row.Revision, row.Sha256);
            string diffSummary;
            if (oldBytes is null)
            {
                diffSummary = "旧版内容尚未归档，无法作逐步差异比较；只可预检新定义。";
            }
            else
            {
                var oldDocument = TestDocumentCodec.Deserialize(
                    System.Text.Encoding.UTF8.GetString(oldBytes));
                static IEnumerable<StepNode> Walk(StepNode n)
                {
                    yield return n;
                    foreach (var slot in n.Slots.Values)
                        foreach (var child in slot)
                            foreach (var deep in Walk(child)) yield return deep;
                }
                static string StepSignature(StepNode n) =>
                    n.Type + "|" + n.Name + "|" +
                    string.Join(";", n.Properties.OrderBy(x => x.Key)
                        .Select(x => x.Key + "=" + x.Value)) + "|" +
                    string.Join(";", n.Slots.OrderBy(x => x.Key)
                        .Select(x => x.Key + "=" +
                            string.Join(",", x.Value.Select(step => step.Id.ToString()))));
                static Dictionary<string, string> Index(TestDocument doc) =>
                    Enum.GetValues<TestPhase>()
                        .SelectMany(phase => doc.Get(phase).SelectMany(Walk))
                        .ToDictionary(node => node.Id.ToString(), StepSignature);
                var before = Index(oldDocument);
                var after = Index(latest.Document);
                var added = after.Keys.Count(id => !before.ContainsKey(id));
                var removed = before.Keys.Count(id => !after.ContainsKey(id));
                var edited = after.Count(item => before.TryGetValue(item.Key,
                    out var oldValue) && oldValue != item.Value);
                var mappingChanged = !oldDocument.Mappings.SequenceEqual(latest.Document.Mappings);
                diffSummary = "已验证旧版历史资产。步骤新增 " + added +
                    "、移除 " + removed + "、本体或分支编辑 " + edited +
                    "；映射内容变化：" + (mappingChanged ? "是" : "否") +
                    "。此摘要不代替信号物理语义的工程师确认。";
            }
            var signature = string.Join("、", mappings.Take(7)
                .Select(x => x.Name + " (" + x.Kind + ")"));
            var preview = "用例：" + row.Name +
                "\n项目固定：r" + row.Revision + " / " + row.Sha256 +
                "\n当前资源：r" + expectedRevision + " / " + expectedSha +
                "\n当前真实步骤：" + steps.Length + " 个，资源映射：" +
                mappings.Length + " 项" +
                (signature.Length == 0 ? "" : "\n映射摘要：" + signature) +
                "\n\n" + diffSummary +
                "\n采用新版只修改当前调用实例的修订指针，不修复信号语义、其它用例或生产发布。" +
                "\n采用后需要重新完成工程资源检查。";
            if (!plan.Diagnostics.IsEmpty)
                return (false, preview + "\n\n新版 TEST 步骤预检失败，不能采用：\n" +
                    string.Join("\n", plan.Diagnostics.Take(8)));
            return (true, preview);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or
            ArgumentException or InvalidOperationException or UnauthorizedAccessException or
            AutoTest.Projects.Project.ProjectDataException or FormatException or NotSupportedException)
        {
            return (false, "无法验证当前 TEST 资源实际内容：" + error.Message);
        }
    }

    public void AdoptCurrentRevision(TestProjectPackageRow row, int expectedRevision, string expectedSha)
    {
        if (!Packages.Contains(row) || IsBusy) return;
        var current = CurrentAssetFor(row);
        if (current is null || current.Revision != expectedRevision ||
            !current.Sha256.Equals(expectedSha, StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "TEST 修订已经变化，未更新引用；请重新查看最新版本。";
            RefreshReferences();
            return;
        }
        row.RefreshAvailableRevision(current.Revision, current.Sha256);
        row.AdoptCurrentRevision();
        StatusMessage = "已选择采用测试用例最新修订 r" + current.Revision +
            "。该引用将随项目自动保存；历史运行记录不会改变。";
        RefreshCommands();
        _ = RefreshStepCountsAsync();
    }

    private async Task RefreshStepCountsAsync()
    {
        var current = _workspace.Current;
        if (current is null) return;
        foreach (var row in Packages.ToArray())
        {
            if (!Packages.Contains(row)) continue;
            try
            {
                var asset = current.Definition.Assets.FirstOrDefault(x =>
                    x.Id.Value == row.AssetId &&
                    x.Kind == ProjectWorkspaceService.TestProgramKind);
                if (asset is null)
                {
                    row.StepCountLabel = "引用失效";
                    continue;
                }
                var document = await _workspace.OpenTestEditorAsync(AssetId.Create(row.AssetId));
                if (!Packages.Contains(row)) continue;
                row.StepCountLabel = document.Document.Walk().Count().ToString();
            }
            catch (Exception error) when (error is IOException or InvalidDataException or
                                          ArgumentException or UnauthorizedAccessException or
                                          AutoTest.Projects.Project.ProjectDataException)
            {
                row.StepCountLabel = "读取失败";
            }
        }
    }

    private void RefreshAvailable()
    {
        AvailablePackages.Clear();
        var assets = _workspace.Current?.Definition.Assets
            .Where(x => x.Kind == ProjectWorkspaceService.TestProgramKind)
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray() ?? Array.Empty<AssetEntry>();
        foreach (var asset in assets)
            AvailablePackages.Add(new TestProjectPackageChoice(asset));
        SelectedAvailable = AvailablePackages.FirstOrDefault();
    }

    private TestProjectDocument Compose() =>
        new(_projectSetup is not null || _projectTeardown is not null ? 2 :
            _session.Document.SchemaVersion, _session.Document.Id, (Name ?? "").Trim(),
            Packages.Select(x => x.ToEntry()).ToList())
        {
            ProjectSetup = _projectSetup, ProjectTeardown = _projectTeardown
        };

    private async Task CreateProjectStageAsync(bool setup)
    {
        if (IsBusy || _realTest?.IsRunning == true || _workspace.Current is null)
            return;
        if (IsDirty)
        {
            await SaveAsync(silent: true);
            if (IsDirty)
            {
                StatusMessage = "测试项目草稿尚未保存，无法安全创建项目级步骤。";
                return;
            }
        }
        var prefix = setup ? "项目准备" : "项目收尾";
        var name = prefix;
        var suffix = 2;
        while (_workspace.Current!.Definition.Assets.Any(a =>
            a.Kind == ProjectWorkspaceService.TestProgramKind &&
            string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)))
            name = prefix + " " + suffix++;
        IsBusy = true;
        try
        {
            var asset = await _workspace.CreateTestProgramAsync(name);
            RefreshAvailable();
            SelectedAvailable = AvailablePackages.First(x => x.Asset.Id == asset.Id);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or
            InvalidDataException or ArgumentException or
            AutoTest.Projects.Project.ProjectDataException)
        {
            StatusMessage = "新建项目级 TEST 步骤失败：" + error.Message;
            return;
        }
        finally { IsBusy = false; }
        SetProjectStage(setup);
        if (IsDirty) await SaveAsync(silent: true);
        if (IsDirty)
        {
            StatusMessage = "TEST 定义已创建，但项目阶段绑定尚未保存；请修复后重试。";
            return;
        }
        OpenProjectStage(setup);
    }

    private void SetProjectStage(bool setup)
    {
        if (SelectedAvailable is not { } chosen || IsBusy) return;
        var old = setup ? _projectSetup : _projectTeardown;
        if (old is null && _session.Document.SchemaVersion == 1 &&
            !_dialogs.Confirm("启用新版 TEST 项目准备/收尾？历史普通准备用例不会自动迁移。" +
                " 请确认绑定的是整车测试环境步骤，不是夹具、扫码或前置 FLASH。"))
            return;
        var asset = chosen.Asset;
        var entry = new TestProjectEntry(old?.ItemId ?? Guid.NewGuid(),
            asset.Id.Value, asset.Revision, asset.Sha256, asset.Name, true);
        if (setup) _projectSetup = entry; else _projectTeardown = entry;
        ResetFrozenSelection();
        RaisePropertyChanged(setup ? nameof(ProjectSetupLabel) : nameof(ProjectTeardownLabel));
        StatusMessage = (setup ? "项目准备" : "项目收尾") +
            "已绑定 TEST 定义「" + asset.Name + "」，固定 r" + asset.Revision +
            "；使用同一 TEST DSL 和真实执行器，不能作为 ECU 产品 PASS 判定。";
        RefreshCommands();
    }

    private void OpenProjectStage(bool setup)
    {
        var stage = setup ? _projectSetup : _projectTeardown;
        if (stage is not null) OpenPackageRequested?.Invoke(stage.PackageAssetId);
    }

    private void RemoveProjectStage(bool setup)
    {
        if (IsBusy) return;
        if (setup) _projectSetup = null; else _projectTeardown = null;
        ResetFrozenSelection();
        RaisePropertyChanged(setup ? nameof(ProjectSetupLabel) : nameof(ProjectTeardownLabel));
        StatusMessage = "项目级阶段调用已从草稿移除；源 TEST 资产未删除。";
        RefreshCommands();
    }

    private void Add()
    {
        if (SelectedAvailable is not { } choice || IsBusy) return;
        var asset = choice.Asset;
        var row = new TestProjectPackageRow(new TestProjectEntry(
            Guid.NewGuid(), asset.Id.Value, asset.Revision, asset.Sha256, asset.Name, true));
        if (_selection is not null)
            row.RunSelected = _selection.Get(_session.AssetId.Value, row.ItemId, row.Enabled).IsSelected;
        row.PropertyChanged += RowChanged;
        Packages.Add(row);
        Renumber();
        SelectedPackage = row;
        RaisePropertyChanged(nameof(SelectionSummary));
        RaisePropertyChanged(nameof(TotalCount));
        row.RefreshAvailableRevision(asset.Revision, asset.Sha256);
        StatusMessage = "已加入测试用例：" + asset.Name + "。重复引用会形成独立的执行条目。";
        RefreshCommands();
        PackageRowsChanged?.Invoke();
        _ = RefreshStepCountsAsync();
    }
    public void AddNewFormalPackage(AssetEntry asset)
    {
        if (IsBusy || asset.Kind != ProjectWorkspaceService.TestProgramKind)
            throw new InvalidOperationException("无法关联非 TEST 资产或忙碌的项目编辑器。");
        RefreshAvailable();
        SelectedAvailable = AvailablePackages.FirstOrDefault(x => x.Asset.Id == asset.Id)
            ?? throw new InvalidOperationException("新 TEST 资产尚未登记在当前工程。");
        Add();
    }

    private async Task RemoveSelectedPackageAsync()
    {
        if (IsBusy || SelectedPackage is not { } row) return;
        if (!_dialogs.Confirm("从当前测试项目移除用例“" + row.Name + "”？" +
            "\\n此操作不会删除源 TEST 文件，也不会影响其他项目引用。")) return;
        Remove();
        // This is a deliberate destructive edit: persist it immediately rather
        // than waiting for an idle timer or expecting the operator to press Save.
        await SaveAsync();
    }

    private void Remove()
    {
        if (SelectedPackage is not { } row || IsBusy) return;
        row.PropertyChanged -= RowChanged;
        var index = Packages.IndexOf(row);
        Packages.Remove(row);
        Renumber();
        RaisePropertyChanged(nameof(SelectedCount));
        RaisePropertyChanged(nameof(TotalCount));
        RaisePropertyChanged(nameof(SelectionSummary));
        SelectedPackage = Packages.Count == 0 ? null : Packages[Math.Min(index, Packages.Count - 1)];
        RefreshCommands();
        PackageRowsChanged?.Invoke();
    }
    private bool CanMove(int direction) => !IsBusy && SelectedPackage is { } row &&
        Packages.IndexOf(row) + direction >= 0 &&
        Packages.IndexOf(row) + direction < Packages.Count;
    private void Move(int direction)
    {
        if (!CanMove(direction) || SelectedPackage is not { } row) return;
        var index = Packages.IndexOf(row);
        Packages.Move(index, index + direction);
        Renumber();
        SelectedPackage = row;
        RefreshCommands();
        PackageRowsChanged?.Invoke();
    }
    private void RefreshCommands()
    {
        RaisePropertyChanged(nameof(IsDirty));
        RaisePropertyChanged(nameof(HasStaleReferences));
        AddCommand?.RaiseCanExecuteChanged();
        CreateProjectSetupCommand?.RaiseCanExecuteChanged();
        CreateProjectTeardownCommand?.RaiseCanExecuteChanged();
        SetProjectSetupCommand?.RaiseCanExecuteChanged();
        SetProjectTeardownCommand?.RaiseCanExecuteChanged();
        OpenProjectSetupCommand?.RaiseCanExecuteChanged();
        OpenProjectTeardownCommand?.RaiseCanExecuteChanged();
        RemoveProjectSetupCommand?.RaiseCanExecuteChanged();
        RemoveProjectTeardownCommand?.RaiseCanExecuteChanged();
        CreateNewPackageCommand?.RaiseCanExecuteChanged();
        RemoveCommand?.RaiseCanExecuteChanged();
        UpCommand?.RaiseCanExecuteChanged(); DownCommand?.RaiseCanExecuteChanged();
        SaveCommand?.RaiseCanExecuteChanged(); ReloadCommand?.RaiseCanExecuteChanged();
        PreflightSelectedCommand?.RaiseCanExecuteChanged();
        RunSelectedCommand?.RaiseCanExecuteChanged();
        StopSelectedCommand?.RaiseCanExecuteChanged();
        if (!IsBusy && IsDirty)
        {
            _autosaveTimer.Stop();
            _autosaveTimer.Start();
        }
    }
    public bool ConfirmClose() =>
        !IsBusy && (!IsDirty || _dialogs.Confirm("测试项目有未保存的用例顺序修改。确定放弃吗？"));

    public async Task<bool> SaveForCloseAsync()
    {
        if (IsBusy) return false;
        if (!IsDirty) return true;
        var previousRevision = Revision;
        await SaveAsync();
        return !IsBusy && !IsDirty && Revision > previousRevision;
    }

    private async Task SaveAsync(bool silent = false)
    {
        if (!IsDirty || IsBusy) return;
        ResetFrozenSelection();
        IsBusy = true;
        try
        {
            var submittedName = Name;
            var changed = Compose();
            changed.Validate();
            var selectedItem = SelectedPackage;
            var selectedAvailableId = SelectedAvailable?.Asset.Id;
            _session = await _workspace.SaveTestProjectEditorAsync(_session, changed);
            // Normalize only the same name draft that was just persisted. Do not
            // overwrite a fresh keystroke made while asynchronous I/O ran.
            if (string.Equals(Name, submittedName, StringComparison.Ordinal))
                Name = _session.Document.Name;
            // Do not reconstruct bound rows after each autosave: preserves selection,
            // scroll, keyboard focus and transient engineer-only checkboxes.
            RefreshAvailable();
            if (selectedAvailableId is { } targetId)
                SelectedAvailable = AvailablePackages.FirstOrDefault(x => x.Asset.Id == targetId)
                    ?? SelectedAvailable;
            if (selectedItem is not null && Packages.Contains(selectedItem))
                SelectedPackage = selectedItem;
            _ = RefreshStepCountsAsync();
            RaisePropertyChanged(nameof(Revision));
            StatusMessage = "测试项目已保存。用例列表引用当前修订；不会自动运行或连接台架。";
        }
        catch (Exception error) when (error is IOException or InvalidDataException or
                                      InvalidOperationException or ArgumentException or
                                      AutoTest.Projects.Project.ProjectDataException)
        {
            StatusMessage = "保存测试项目失败：" + error.Message;
            if (!silent) _dialogs.ShowError(StatusMessage);
        }
        finally { IsBusy = false; }
    }
    public async Task SynchronizeSavedRevisionAsync()
    {
        var current = _workspace.Current;
        var asset = current?.Definition.Assets.FirstOrDefault(a => a.Id == _session.AssetId);
        if (IsBusy || asset is null || current!.Definition.Id != _session.ProjectId ||
            asset.Revision == _session.AssetRevision) return;
        if (IsDirty)
        {
            StatusMessage = "测试项目修订已随用例更新；未保存的顺序草稿仍保留，请核对后重新加载。";
            return;
        }
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        ResetFrozenSelection();
        if (IsBusy || !ConfirmClose()) return;
        IsBusy = true;
        try
        {
            _session = await _workspace.OpenTestProjectEditorAsync(_session.AssetId);
            Name = _session.Document.Name;
            LoadRows(_session.Document);
            RefreshAvailable();
            _ = RefreshStepCountsAsync();
            RaisePropertyChanged(nameof(Revision));
            StatusMessage = "测试项目已从工程重新读取。";
        }
        catch (Exception error) when (error is IOException or InvalidDataException or
                                      InvalidOperationException or ArgumentException or
                                      AutoTest.Projects.Project.ProjectDataException)
        {
            StatusMessage = "重新加载失败：" + error.Message;
            _dialogs.ShowError(StatusMessage);
        }
        finally { IsBusy = false; }
    }
}
