using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
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
    public string Label => Asset.Name + " · 修订 " + Asset.Revision;
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
    public bool CanAdoptLatest => _latestRevision is { } latest &&
        (latest != Revision || !string.Equals(_latestSha256, Sha256, StringComparison.OrdinalIgnoreCase));
    public string PinStatus => _latestRevision is not { } latest ? "资源已移除" :
        CanAdoptLatest ? "绑定 r" + Revision + " → 当前 r" + latest :
        "已绑定 r" + Revision;
    public string PinDetail => "项目固定 SHA: " + Sha256 +
        "\n工程当前 SHA: " + (_latestSha256 ?? "资源缺失");

    /// <summary>
    /// Refresh source metadata without changing the persisted reference. Adopting
    /// current Revision/SHA requires a separate engineer-confirmed action.
    /// </summary>
    public void RefreshAvailableRevision(int? revision, string? sha256)
    {
        _latestRevision = revision;
        _latestSha256 = sha256;
        RaisePropertyChanged(nameof(LatestRevision));
        RaisePropertyChanged(nameof(LatestSha256));
        RaisePropertyChanged(nameof(CanAdoptLatest));
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

    public TestProjectWorkbenchViewModel(ProjectWorkspaceService workspace,
        TestEditorDialogs dialogs, TestProjectEditorSession session,
        ProjectRunSelectionState? selection = null,
        RealTestWorkbenchService? realTest = null, HardwareConnectionService? hardware = null)
    {
        _workspace = workspace; _dialogs = dialogs; _session = session;
        _selection = selection; _realTest = realTest; _hardware = hardware;
        if (_selection is not null) _selection.Changed += OnNavigationSelectionChanged;
        _name = session.Document.Name;
        AddCommand = new DelegateCommand(Add, () => SelectedAvailable is not null && !IsBusy);
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
        !Packages.Select(x => x.ToEntry()).SequenceEqual(_session.Document.Packages);
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
            StatusMessage = "请先保存测试项目的顺序、默认配置和修订，再确认本次运行。";
            return;
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
                if (asset is null || asset.Revision != row.Revision ||
                    !asset.Sha256.Equals(row.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("引用失效：" + row.Name +
                        "。需采用修订后保存测试项目。");

                var opened = await _workspace.OpenTestEditorAsync(AssetId.Create(row.AssetId));
                if (opened.AssetRevision != row.Revision ||
                    !opened.FileHash.Equals(row.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("用例文件发生变化：" + row.Name);
                var plan = _realTest.Prepare(opened.Document);
                if (!plan.Diagnostics.IsEmpty)
                    throw new InvalidDataException(row.Name + " 步骤预检：" +
                        string.Join("; ", plan.Diagnostics));
                var mappings = TestRunStepScope.RequiredMappings(opened.Document);
                var actionCheck = new ConfiguredStationActionPort(_hardware, _workspace);
                await actionCheck.PrepareAsync(mappings, CancellationToken.None);
                actionCheck.ValidateRunSteps(opened.Document);

                var jobs = ConfiguredRealTestFlashPort.FreezeJobs(opened.Document, context.RootDirectory);
                var caseFlash = jobs.Count == 0 ? "" : jobs.Values.Single().Consent;
                if (caseFlash.Length > 0)
                {
                    if (flashConsent.Length > 0)
                        throw new NotSupportedException(
                            "R1 首版一个批次只允许一个 FLASH；请拆为独立批次分别授权。");
                    flashConsent = caseFlash;
                }
                prepared.Add(new FrozenSelectedCase(row.ToEntry(), plan.RestoreDocument(),
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

    /// <summary>Run each frozen package through the existing physical TEST/RECORD
    /// service. Each package has its own durable RunId and artifact report.</summary>
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
        _batchCancellation = new CancellationTokenSource();
        var token = _batchCancellation.Token;
        IsBusy = true;
        StopSelectedCommand.RaiseCanExecuteChanged();
        BatchResults.Clear();
        var index = 0;
        try
        {
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
                    result = await _realTest.ExecuteAsync(item.Document, item.TestConsent,
                        item.FlashConsent.Length > 0 ? fixedFlashApproval : "",
                        _frozenDut, token);
                }
                finally { _realTest.StepReported -= OnStep; }
                BatchResults.Add(index + ". " + item.Entry.DisplayName + " · " +
                    result.Verdict + " · RunId=" + result.Run.Value +
                    (result.EvidencePath is null ? "" : " · HTML=" + result.EvidencePath));
                if (result.Verdict != RealTestVerdict.Passed)
                {
                    StatusMessage = "批次停止：" + item.Entry.DisplayName + " 的正式记录为 " +
                        result.Verdict + "，后续用例未执行。";
                    return;
                }
            }
            StatusMessage = "批次结束：" + batch.Length +
                " 个用例均已生成各自的正式开发记录；未计入生产数量。";
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

    public bool HasStaleReferences => Packages.Any(x => x.CanAdoptLatest);

    public AssetEntry? CurrentAssetFor(TestProjectPackageRow row) =>
        _workspace.Current?.Definition.Assets.FirstOrDefault(x =>
            x.Id.Value == row.AssetId && x.Kind == ProjectWorkspaceService.TestProgramKind);

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
            "。保存测试项目后才会正式更新固定引用；历史运行记录不会改变。";
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
                if (asset is null ||
                    asset.Revision != row.Revision ||
                    !asset.Sha256.Equals(row.Sha256, StringComparison.OrdinalIgnoreCase))
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
        new(_session.Document.SchemaVersion, _session.Document.Id, (Name ?? "").Trim(),
            Packages.Select(x => x.ToEntry()).ToList());

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
        AddCommand?.RaiseCanExecuteChanged(); CreateNewPackageCommand?.RaiseCanExecuteChanged();
        RemoveCommand?.RaiseCanExecuteChanged();
        UpCommand?.RaiseCanExecuteChanged(); DownCommand?.RaiseCanExecuteChanged();
        SaveCommand?.RaiseCanExecuteChanged(); ReloadCommand?.RaiseCanExecuteChanged();
        PreflightSelectedCommand?.RaiseCanExecuteChanged();
        RunSelectedCommand?.RaiseCanExecuteChanged();
        StopSelectedCommand?.RaiseCanExecuteChanged();
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

    private async Task SaveAsync()
    {
        ResetFrozenSelection();
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var changed = Compose();
            changed.Validate();
            var selection = Packages.ToDictionary(x => x.ItemId, x => x.RunSelected);
            _session = await _workspace.SaveTestProjectEditorAsync(_session, changed);
            Name = _session.Document.Name;
            LoadRows(_session.Document);
            foreach (var row in Packages)
                if (selection.TryGetValue(row.ItemId, out var selected)) row.RunSelected = selected;
            RefreshAvailable();
            _ = RefreshStepCountsAsync();
            RaisePropertyChanged(nameof(Revision));
            StatusMessage = "测试项目已保存。用例列表引用当前修订；不会自动运行或连接台架。";
        }
        catch (Exception error) when (error is IOException or InvalidDataException or
                                      InvalidOperationException or ArgumentException or
                                      AutoTest.Projects.Project.ProjectDataException)
        {
            StatusMessage = "保存测试项目失败：" + error.Message;
            _dialogs.ShowError(StatusMessage);
        }
        finally { IsBusy = false; }
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
