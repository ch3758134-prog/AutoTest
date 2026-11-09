using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using AutoTest.Projects.ControllerConfig;
using AutoTest.Projects.Resources;
using AutoTest.Projects.Project;
using AutoTest.Core;
using System.Windows;

namespace AutoTest.Desktop.Tools.ControllerConfig;

public sealed class ToolCommand(Action action) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => action();
    public void Requery() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class ControllerConfigToolViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? key = null) => PropertyChanged?.Invoke(this, new(key));
    private ControllerEditSession? _session;
    private string? _lastSha;
    private ControllerDefinition? _selectedController;
    private ControllerState? _selectedState;
    private string _status = "尚未打开配置 / Unknown";
    private string _lastError = "";
    private R0ProjectAssetWorkspace? _boundWorkspace;
    private AssetId _boundAssetId;
    private int _boundRevision;
    private string _boundSha = "";
    private string _traceFilter = "";
    private readonly R0ProjectDiagnosticJournal _journal = new();

    public ObservableCollection<ControllerDefinition> Controllers { get; } = [];
    public ObservableCollection<ControllerState> States { get; } = [];
    public ObservableCollection<VoltageWindow> VoltageWindows { get; } = [];
    public ObservableCollection<VoltageMeasurement> Measurements { get; } = [];
    public string FilePath { get; set; } = "";
    public string ProjectFilePath { get; set; } = "";
    public string BoundAssetId { get; set; } = "";
    public string TraceFilePath { get; set; } = "";
    public bool IncludeRawTraceHex { get; set; } = true;
    public string TraceFilter { get => _traceFilter; set { _traceFilter = value ?? ""; Changed(); Changed(nameof(VisibleTraceEvents)); } }
    public ObservableCollection<R0ProjectDiagnosticEvent> TraceEvents { get; } = [];
    public IEnumerable<R0ProjectDiagnosticEvent> VisibleTraceEvents => TraceEvents.Where(e =>
        string.IsNullOrWhiteSpace(TraceFilter) ||
        (e.Stage + " " + e.ErrorCode + " " + e.Detail + " " + e.Actual).Contains(TraceFilter, StringComparison.OrdinalIgnoreCase));
    public string TraceQuality => $"Trace {TraceEvents.Count} / Dropped {_journal.DroppedCount} / SOFTWARE_ONLY";
    public string ProjectBindingSummary => _boundWorkspace is null ? "工程未绑定 / Revision UNKNOWN" :
        $"Project {_boundWorkspace.Definition.Id.Value:D} / Asset {_boundAssetId.Value:D} / R{_boundRevision} / SHA256 {_boundSha} / SOFTWARE_ONLY";

    public string EcuName { get; set; } = "";
    public string BoardModel { get; set; } = "";
    public string HardwareProfileId { get; set; } = "";
    public string ControllerName { get; set; } = "";
    public string StateName { get; set; } = "";
    public string StateMaskHex { get; set; } = "0x0";
    public string VoltageChannel { get; set; } = "1";
    public string TargetVolts { get; set; } = "";
    public string MinVolts { get; set; } = "";
    public string MaxVolts { get; set; } = "";
    public string MeasuredVolts { get; set; } = "";
    public string OperatorId { get; set; } = "";
    public string EvidenceRef { get; set; } = "";
    public string InteractionChoice { get; set; } = "Momentary";
    public string? ReturnStateId { get; set; }
    public string ExclusiveGroup { get; set; } = "";
    public IReadOnlyList<string> Interactions { get; } = Enum.GetNames<ControlInteraction>();
    public EcuControlProfile? CurrentProfile => _session?.Current;
    public int CurrentRevision => CurrentProfile?.Revision ?? 0;
    public string Status { get => _status; private set { _status = value; Changed(); } }
    public string Error { get => _lastError; private set { _lastError = value; Changed(); } }
    public ControllerDefinition? SelectedController
    {
        get => _selectedController;
        set { _selectedController = value; Changed(); LoadController(); }
    }
    public ControllerState? SelectedState
    {
        get => _selectedState;
        set { _selectedState = value; Changed(); LoadState(); }
    }

    public ICommand BindProjectCommand { get; }
    public ICommand ExportTraceCommand { get; }
    public ICommand CreateCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand AddControllerCommand { get; }
    public ICommand RenameControllerCommand { get; }
    public ICommand RemoveControllerCommand { get; }
    public ICommand AddStateCommand { get; }
    public ICommand UpdateStateCommand { get; }
    public ICommand RemoveStateCommand { get; }
    public ICommand SetDefaultCommand { get; }
    public ICommand SetWindowCommand { get; }
    public ICommand RecordVoltageCommand { get; }
    public ICommand UndoCommand { get; }

    public ControllerConfigToolViewModel()
    {
        BindProjectCommand = new ToolCommand(() => _ = BindProjectSafeAsync());
        ExportTraceCommand = new ToolCommand(() => _ = ExportTraceSafeAsync());
        CreateCommand = new ToolCommand(() => Guard(Create));
        OpenCommand = new ToolCommand(() => _ = OpenGuardedAsync());
        SaveCommand = new ToolCommand(() => _ = SaveGuardedAsync());
        AddControllerCommand = new ToolCommand(() => Guard(() => { Session.AddController(ControllerName); Refresh(); }));
        RenameControllerCommand = new ToolCommand(() => Guard(() => { Session.RenameController(NeedController(), ControllerName); Refresh(); }));
        RemoveControllerCommand = new ToolCommand(() => Guard(() => { Session.RemoveController(NeedController()); Refresh(); }));
        AddStateCommand = new ToolCommand(() => Guard(() => { Session.AddState(NeedController(), StateName, ParseMask()); Refresh(); }));
        UpdateStateCommand = new ToolCommand(() => Guard(UpdateState));
        RemoveStateCommand = new ToolCommand(() => Guard(() => { Session.RemoveState(NeedController(), NeedState()); Refresh(); }));
        SetDefaultCommand = new ToolCommand(() => Guard(() => { Session.SetDefault(NeedController(), NeedState()); Refresh(); }));
        SetWindowCommand = new ToolCommand(() => Guard(SetVoltageWindow));
        RecordVoltageCommand = new ToolCommand(() => Guard(RecordVoltage));
        UndoCommand = new ToolCommand(() => Guard(() => { Session.Undo(); Refresh(); }));
    }

    private void Log(string stage, string outcome, string errorCode = "",
        string expected = "", string actual = "", string detail = "")
    {
        var item = _journal.Record("PROJECT/CONTROLLER", stage, outcome, errorCode,
            project: ProjectFilePath, assetId: _boundWorkspace is null ? "" : _boundAssetId.Value.ToString("D"),
            revision: _boundRevision, sha256: _boundSha, expected: expected, actual: actual, detail: detail);
        TraceEvents.Add(item);
        if (TraceEvents.Count > 4096) TraceEvents.RemoveAt(0);
        Changed(nameof(VisibleTraceEvents)); Changed(nameof(TraceQuality));
    }

    private async Task BindProjectSafeAsync()
    {
        try
        {
            Error = "";
            var workspace = await R0ProjectAssetWorkspace.OpenAsync(ProjectFilePath);
            if (!AssetId.TryParse(BoundAssetId, out var id))
                throw new ProjectDataException("ASSET_ID", "请输入工程资源清单中的 Controller/Profile AssetId。");
            var check = workspace.Find(id);
            if (!(check.Asset.Kind.Equals("Controller", StringComparison.OrdinalIgnoreCase) ||
                  check.Asset.Kind.Equals("Profile", StringComparison.OrdinalIgnoreCase)) || !check.IsValid)
                throw new ProjectDataException("ASSET_STALE", "Controller/Profile 资源类型或哈希不匹配。");
            var loaded = await ControllerRepository.OpenAsync(check.FullPath);
            if (!string.Equals(loaded.Receipt.Sha256, check.Asset.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new ProjectDataException("ASSET_STALE", "Controller 源码文件与工程哈希不匹配。");
            _boundWorkspace = workspace; _boundAssetId = id;
            _boundRevision = check.Asset.Revision; _boundSha = check.Asset.Sha256;
            _session = new ControllerEditSession(loaded.Profile);
            _lastSha = loaded.Receipt.Sha256;
            FilePath = check.FullPath; EcuName = loaded.Profile.EcuName;
            BoardModel = loaded.Profile.HardwareModel;
            HardwareProfileId = loaded.Profile.HardwareProfileId.ToString("D");
            Changed(nameof(FilePath)); Changed(nameof(EcuName)); Changed(nameof(BoardModel));
            Changed(nameof(HardwareProfileId)); Changed(nameof(ProjectBindingSummary));
            Refresh();
            Status = "已绑定工程控制器配置（只有配置事实，不代表硬件输出）";
            Log("BIND", "ACCEPTED", expected: check.Asset.Sha256, actual: loaded.Receipt.Sha256);
        }
        catch (Exception e)
        {
            Error = e is ProjectDataException p ? $"{p.Code}: {p.Message}" : e.Message;
            Status = "Controller 绑定失败";
            Log("BIND", "REJECTED", e is ProjectDataException x ? x.Code : e.GetType().Name, detail: e.Message);
        }
    }

    private async Task ExportTraceSafeAsync()
    {
        try
        {
            Error = "";
            await _journal.ExportJsonLinesAsync(TraceFilePath, true, IncludeRawTraceHex);
            Status = "Controller 诊断已导出到本地 JSONL";
        }
        catch (Exception e)
        {
            Error = e is ProjectDataException p ? $"{p.Code}: {p.Message}" : e.Message;
            Status = "Trace 导出失败";
            Log("TRACE_EXPORT", "REJECTED", e is ProjectDataException x ? x.Code : e.GetType().Name, detail: e.Message);
        }
    }

    private ControllerEditSession Session => _session ?? throw new InvalidOperationException("请先创建或打开 ECU 配置。");
    private Guid NeedController() => SelectedController?.Id ?? throw new InvalidOperationException("请先选择控制器。");
    private Guid NeedState() => SelectedState?.Id ?? throw new InvalidOperationException("请先选择状态。");
    private uint ParseMask()
    {
        var raw = StateMaskHex.Trim();
        if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) raw = raw[2..];
        if (!uint.TryParse(raw, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            throw new FormatException("Mask 必须是十六进制数，例如 0x00001。");
        return value;
    }
    private static double? Optional(string value) => string.IsNullOrWhiteSpace(value) ? null :
        double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
    private void Create()
    {
        var profile = EcuControlProfile.Create(EcuName, BoardModel, Guid.Parse(HardwareProfileId));
        _session = new ControllerEditSession(profile);
        _lastSha = null;
        _boundWorkspace = null; _boundRevision = 0; _boundSha = ""; _boundAssetId = default;
        Changed(nameof(ProjectBindingSummary));
        Refresh();
    }
    private async Task OpenGuardedAsync()
    {
        try
        {
            Error = "";
            var loaded = await ControllerRepository.OpenAsync(FilePath);
            _session = new ControllerEditSession(loaded.Profile);
            _boundWorkspace = null; _boundRevision = 0; _boundSha = ""; _boundAssetId = default;
            Changed(nameof(ProjectBindingSummary));
            _lastSha = loaded.Receipt.Sha256;
            EcuName = loaded.Profile.EcuName;
            BoardModel = loaded.Profile.HardwareModel;
            HardwareProfileId = loaded.Profile.HardwareProfileId.ToString("D");
            Changed(nameof(EcuName)); Changed(nameof(BoardModel)); Changed(nameof(HardwareProfileId));
            Refresh(); Status = "配置已打开（仅配置事实；没有连接硬件）";
        }
        catch (Exception e) { Error = e.Message; Status = "打开失败"; }
    }
    private async Task SaveGuardedAsync()
    {
        try
        {
            Error = "";
            ControllerFileReceipt receipt;
            if (_boundWorkspace is null)
            {
                receipt = await ControllerRepository.SaveAsync(Session.Current, FilePath, _lastSha);
            }
            else
            {
                await _boundWorkspace.VerifyPinnedAssetAsync(_boundAssetId, _boundRevision, _boundSha);
                var temp = Path.Combine(Path.GetTempPath(), "R0_CTRL_" + Guid.NewGuid().ToString("N") + ".json");
                try
                {
                    await ControllerRepository.SaveAsync(Session.Current, temp);
                    var published = await _boundWorkspace.PublishRevisionAsync(_boundAssetId,
                        await File.ReadAllBytesAsync(temp));
                    FilePath = published.FullPath;
                    _boundRevision = published.Asset.Revision;
                    _boundSha = published.Asset.Sha256;
                    receipt = new ControllerFileReceipt(FilePath, _boundSha, _boundRevision);
                    Changed(nameof(FilePath)); Changed(nameof(ProjectBindingSummary));
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
            _lastSha = receipt.Sha256;
            Log("SAVE", "ACCEPTED", expected: "Validated controller revision", actual: receipt.Sha256);
            Status = $"已保存 Revision {receipt.Revision} / SHA256 {receipt.Sha256[..12]}…";
        }
        catch (Exception e) { Error = e.Message; Status = "保存失败（当前编辑未丢失）"; }
    }
    private void UpdateState()
    {
        var old = SelectedState ?? throw new InvalidOperationException("请先选择状态。");
        var choice = Enum.Parse<ControlInteraction>(InteractionChoice);
        Guid? target = string.IsNullOrWhiteSpace(ReturnStateId) ? null : Guid.Parse(ReturnStateId);
        Session.EditState(NeedController(), old with { Name = StateName, KeyMask = ParseMask(),
            Interaction = choice, ReturnStateId = target, ExclusiveGroup =
                string.IsNullOrWhiteSpace(ExclusiveGroup) ? null : ExclusiveGroup });
        Refresh();
    }
    private void SetVoltageWindow()
    {
        Session.SetWindow(NeedController(), NeedState(), new VoltageWindow(int.Parse(VoltageChannel),
            Optional(TargetVolts), Optional(MinVolts), Optional(MaxVolts)));
        Refresh();
    }
    private void RecordVoltage()
    {
        var fact = Session.RecordManualVoltage(NeedController(), NeedState(), int.Parse(VoltageChannel),
            double.Parse(MeasuredVolts, CultureInfo.InvariantCulture), OperatorId, EvidenceRef, DateTimeOffset.UtcNow);
        Refresh();
        Status = $"人工录入 {fact.ValueVolts} V：{fact.Status}（不是硬件自动读数）";
        Log("MANUAL_MEASUREMENT", "RECORDED", expected: "manual origin", actual: $"{fact.ValueVolts} V / {fact.Status}",
            detail: "Measurement is a manually entered value; no physical sampling occurred.");
    }
    private void Guard(Action action)
    {
        try { Error = ""; action(); Log("EDIT", "ACCEPTED", actual: $"Revision {CurrentRevision}"); }
        catch (Exception e) { Error = e is ProjectDataException p ? $"{p.Code}: {p.Message}" : e.Message; Status = "配置校验失败"; Log("EDIT", "REJECTED", e is ProjectDataException x ? x.Code : e.GetType().Name, detail: e.Message); }
    }
    private void Refresh()
    {
        var controllerId = SelectedController?.Id;
        var stateId = SelectedState?.Id;
        Controllers.Clear();
        foreach (var controller in Session.Current.Controllers.OrderBy(c => c.Order)) Controllers.Add(controller);
        SelectedController = Controllers.FirstOrDefault(c => c.Id == controllerId) ?? Controllers.FirstOrDefault();
        SelectedState = States.FirstOrDefault(s => s.Id == stateId) ?? States.FirstOrDefault();
        Changed(nameof(CurrentProfile)); Changed(nameof(CurrentRevision));
        Status = $"Revision {CurrentRevision} / 配置编辑（Physical 未执行）";
    }
    private void LoadController()
    {
        if (SelectedController is not null)
        {
            ControllerName = SelectedController.Name;
            Changed(nameof(ControllerName));
        }
        States.Clear();
        foreach (var state in SelectedController is null ? Enumerable.Empty<ControllerState>() : SelectedController.States.OrderBy(s => s.Order)) States.Add(state);
        SelectedState = States.FirstOrDefault();
    }
    private void LoadState()
    {
        StateName = SelectedState?.Name ?? "";
        StateMaskHex = $"0x{SelectedState?.KeyMask ?? 0:X5}";
        InteractionChoice = (SelectedState?.Interaction ?? ControlInteraction.Momentary).ToString();
        ReturnStateId = SelectedState?.ReturnStateId?.ToString("D");
        ExclusiveGroup = SelectedState?.ExclusiveGroup ?? "";
        Changed(nameof(StateName)); Changed(nameof(StateMaskHex)); Changed(nameof(InteractionChoice));
        Changed(nameof(ReturnStateId)); Changed(nameof(ExclusiveGroup));
        VoltageWindows.Clear(); Measurements.Clear();
        foreach (var window in SelectedState?.Windows ?? []) VoltageWindows.Add(window);
        foreach (var reading in SelectedState?.Measurements ?? []) Measurements.Add(reading);
    }
}
