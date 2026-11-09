using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using AutoTest.Desktop.Workbench;
using Prism.Commands;
using Prism.Mvvm;

namespace AutoTest.Desktop.Bench;

public sealed class BenchStateItem : BindableBase
{
    public BenchStateItem(BenchControllerDefinition controller, BenchStateDefinition state)
    { Controller = controller; State = state; }
    public BenchControllerDefinition Controller { get; }
    public BenchStateDefinition State { get; }
    public string Label => State.Name;
    public string Subtitle => $"Mask 0x{State.KeyMask:X5} · {State.Interaction}";
}

public sealed class ManualVoltageInput : BindableBase
{
    private string _value = "";
    public ManualVoltageInput(int channel) { Channel = channel; }
    public int Channel { get; }
    public string Title => $"CH{Channel}";
    public string Value { get => _value; set => SetProperty(ref _value, value); }
}

public sealed class BenchWorkbenchViewModel : BindableBase
{
    private readonly IBenchWorkbenchPort _port;
    private bool _busy;
    private bool _initialized;
    private string _project = "尚未加载工程";
    private string _ecu = "未选择 ECU Profile";
    private string _revision = "—";
    private string _status = "未连接";
    private string _detail = "未连接；真实输出默认禁止";
    private string _deviceIdentity = "—";
    private string _lastOperation = "尚无操作";
    private string _lastEvidence = "ACK：未知 · 实际电压：未测量";
    private string _comment = "";
    private string _operatorName = "";
    private string _evidenceSearch = "";
    private string _evidencePhaseFilter = "全部";
    private string _requestedFact = "UNKNOWN · 尚无请求";
    private string _acceptedFact = "UNKNOWN · 尚无后端接受证据";
    private string _physicalFact = "UNKNOWN · 未采集物理观测";
    private string _ecuFact = "UNKNOWN · 尚无 ECU 反馈";
    private BenchEvidenceSource _source = BenchEvidenceSource.Mock;
    private BenchConnectionSnapshot _connection = new(BenchConnection.Disconnected, null, null, null, null, null);
    private BenchConfiguration? _config;
    private BenchControllerDefinition? _selectedController;
    private BenchDevice? _selectedDevice;
    private IReadOnlyList<BenchDevice> _candidates = Array.Empty<BenchDevice>();
    private BenchStateItem? _lastState;
    private string? _lastOperationId;

    public BenchWorkbenchViewModel(IBenchWorkbenchPort port)
    {
        _port = port ?? throw new ArgumentNullException(nameof(port));
        ConnectCommand = new DelegateCommand(async () => await ConnectOrDisconnectAsync(), () => !Busy)
            .ObservesProperty(() => Busy);
        RefreshCommand = new DelegateCommand(async () => await ReloadAsync(), () => !Busy)
            .ObservesProperty(() => Busy);
        ApplyStateCommand = new DelegateCommand<BenchStateItem>(async state => await ApplyAsync(state),
            state => state is not null && !Busy && SelectedController is not null)
            .ObservesProperty(() => Busy);
        SaveMeasurementCommand = new DelegateCommand(async () => await SaveMeasurementAsync(),
            () => !Busy && _lastState is not null).ObservesProperty(() => Busy);
        foreach (var n in Enumerable.Range(1, 5)) Voltages.Add(new ManualVoltageInput(n));
        Append(WorkbenchSeverity.Neutral, "台架就绪", "尚未读取工程或硬件事实；模式显示为 Mock。");
    }

    public ObservableCollection<BenchControllerDefinition> Controllers { get; } = new();
    public ObservableCollection<BenchStateItem> States { get; } = new();
    public ObservableCollection<BenchDevice> Devices { get; } = new();
    public ObservableCollection<ManualVoltageInput> Voltages { get; } = new();
    public ObservableCollection<WorkbenchMessage> Messages { get; } = new();
    public ObservableCollection<BenchEvidenceEntry> Evidence { get; } = new();
    public ObservableCollection<BenchEvidenceEntry> VisibleEvidence { get; } = new();
    public IReadOnlyList<string> EvidencePhases { get; } = new[] { "全部", "请求", "接受", "拒绝", "连接", "测量", "错误" };
    public string EvidenceSearch { get => _evidenceSearch; set { if (SetProperty(ref _evidenceSearch, value)) RefreshEvidenceFilter(); } }
    public string EvidencePhaseFilter { get => _evidencePhaseFilter; set { if (SetProperty(ref _evidencePhaseFilter, value)) RefreshEvidenceFilter(); } }
    public string RequestedFact { get => _requestedFact; private set => SetProperty(ref _requestedFact, value); }
    public string AcceptedFact { get => _acceptedFact; private set => SetProperty(ref _acceptedFact, value); }
    public string PhysicalFact { get => _physicalFact; private set => SetProperty(ref _physicalFact, value); }
    public string EcuFact { get => _ecuFact; private set => SetProperty(ref _ecuFact, value); }
    public string ExportEvidenceJson() => BenchEvidenceExport.ToJson(Evidence.Reverse());
    public string ExportEvidenceTsv() => BenchEvidenceExport.ToTsv(Evidence.Reverse());

    public ICommand ConnectCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ApplyStateCommand { get; }
    public ICommand SaveMeasurementCommand { get; }
    public string ProjectName { get => _project; private set => SetProperty(ref _project, value); }
    public string EcuProfileName { get => _ecu; private set => SetProperty(ref _ecu, value); }
    public string Revision { get => _revision; private set => SetProperty(ref _revision, value); }
    public string ConnectionStatus { get => _status; private set => SetProperty(ref _status, value); }
    public string ConnectionDetail { get => _detail; private set => SetProperty(ref _detail, value); }
    public string DeviceIdentity { get => _deviceIdentity; private set => SetProperty(ref _deviceIdentity, value); }
    public string LastOperation { get => _lastOperation; private set => SetProperty(ref _lastOperation, value); }
    public string LastEvidence { get => _lastEvidence; private set => SetProperty(ref _lastEvidence, value); }
    public string Comment { get => _comment; set => SetProperty(ref _comment, value); }
    public string OperatorName { get => _operatorName; set => SetProperty(ref _operatorName, value); }
    public bool Busy { get => _busy; private set { if (SetProperty(ref _busy, value)) RaisePropertyChanged(nameof(ConnectionButtonLabel)); } }
    public string ConnectionButtonLabel => _connection.State == BenchConnection.Connected ? "断开主控板" : "连接主控板";
    public bool ShowDeviceChoice => Devices.Count > 1;
    public bool IsPhysical => Source == BenchEvidenceSource.Physical;
    public string SourceDescription => Source switch
    {
        BenchEvidenceSource.Physical => "Physical · 真实指令默认拒绝，须由 DEVICE 安全门禁单项授权",
        BenchEvidenceSource.Replay => "Replay · 回放数据不是实物响应",
        BenchEvidenceSource.Mock => "Mock · 模拟操作，不产生真实电压",
        _ => "Unknown · 不允许作为实测证据"
    };
    public BenchEvidenceSource Source
    {
        get => _source;
        set { if (SetProperty(ref _source, value)) { _lastState = null; _lastOperationId = null; ((DelegateCommand)SaveMeasurementCommand).RaiseCanExecuteChanged(); RaisePropertyChanged(nameof(SourceDescription)); RaisePropertyChanged(nameof(IsPhysical)); } }
    }
    public BenchControllerDefinition? SelectedController
    {
        get => _selectedController;
        set
        {
            if (!SetProperty(ref _selectedController, value)) return;
            _lastState = null; _lastOperationId = null;
            ((DelegateCommand)SaveMeasurementCommand).RaiseCanExecuteChanged();
            States.Clear();
            if (value is not null) foreach (var state in value.States) States.Add(new BenchStateItem(value, state));
            ((DelegateCommand<BenchStateItem>)ApplyStateCommand).RaiseCanExecuteChanged();
        }
    }
    public BenchDevice? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            // Reject stale selections from an earlier discovery result.
            if (value is not null && !Devices.Any(d => ReferenceEquals(d, value))) return;
            SetProperty(ref _selectedDevice, value);
        }
    }

    // Called by the shared DEVICE session notifier (through INTEGRATION); no USB polling here.
    public void AcceptConnectionSnapshot(BenchConnectionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        UpdateConnection(snapshot);
    }


    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        await ReloadAsync();
    }

    public async Task ReloadAsync()
    {
        if (Busy) return;
        Busy = true;
        try
        {
            var config = await _port.LoadConfigurationAsync(CancellationToken.None);
            _config = config;
            Controllers.Clear();
            SelectedController = null;
            _lastState = null; _lastOperationId = null;
            ((DelegateCommand)SaveMeasurementCommand).RaiseCanExecuteChanged();
            if (config is null)
            {
                ProjectName = "未选择工程"; EcuProfileName = "未选择 ECU Profile"; Revision = "—";
                Append(WorkbenchSeverity.Warning, "没有控制器配置", "请先在 PROJECT 工具中创建控制器、状态及映射。");
            }
            else
            {
                ProjectName = config.ProjectName; EcuProfileName = config.EcuProfileName;
                Revision = config.Revision;
                foreach (var controller in config.Controllers) Controllers.Add(controller);
                SelectedController = Controllers.FirstOrDefault();
                Append(WorkbenchSeverity.Success, "配置已读取", $"{ProjectName} · {EcuProfileName} · revision {Revision}");
            }
            UpdateConnection(await _port.GetConnectionAsync(CancellationToken.None));
        }
        catch (Exception ex) { Failure("读取台架信息", ex); }
        finally { Busy = false; }
    }

    public async Task ConnectOrDisconnectAsync()
    {
        if (Busy) return;
        Busy = true;
        try
        {
            if (_connection.State == BenchConnection.Connected)
            {
                Append(WorkbenchSeverity.Progress, "用户请求断开", DeviceIdentity);
                UpdateConnection(await _port.DisconnectAsync(CancellationToken.None));
                return;
            }
            // Never reuse a previous enumeration for a new connection attempt.
            // The only allowed reuse is the explicit selection immediately following
            // a multiple-device discovery.
            var hasFreshUserChoice = Devices.Count > 1 && SelectedDevice is not null &&
                Devices.Any(d => ReferenceEquals(d, SelectedDevice));
            if (!hasFreshUserChoice)
            {
                ConnectionStatus = "发现设备中";
                Append(WorkbenchSeverity.Progress, "发现兼容主控板", "设备匹配由 DEVICE 完成，不扫描普通扫码枪。");
                SelectedDevice = null;
                Devices.Clear();
                _candidates = await _port.DiscoverCompatibleDevicesAsync(CancellationToken.None);
                foreach (var device in _candidates) Devices.Add(device);
                RaisePropertyChanged(nameof(ShowDeviceChoice));
                SelectedDevice = Devices.Count == 1 ? Devices[0] : null;
                if (Devices.Count == 0)
                {
                    ConnectionStatus = "未找到设备";
                    ConnectionDetail = "DEVICE 未返回匹配主控板；未尝试建立 USB 会话";
                    Append(WorkbenchSeverity.Warning, "未找到兼容设备", ConnectionDetail);
                    return;
                }
                if (Devices.Count > 1)
                {
                    ConnectionStatus = "请选择主控板";
                    ConnectionDetail = $"找到 {Devices.Count} 个匹配设备，请选择后再次点击连接";
                    Append(WorkbenchSeverity.Warning, "检测到多个主控板", ConnectionDetail);
                    return;
                }
            }
            if (SelectedDevice is null) return;
            var chosen = SelectedDevice;
            // Clear the selection before attempting connection. After failure the
            // next click must enumerate again, not reconnect an obsolete path.
            SelectedDevice = null;
            Devices.Clear();
            _candidates = Array.Empty<BenchDevice>();
            RaisePropertyChanged(nameof(ShowDeviceChoice));
            ConnectionStatus = "正在连接";
            Append(WorkbenchSeverity.Progress, "连接主控板", chosen.DisplayName);
            UpdateConnection(await _port.ConnectAsync(chosen.Identity, CancellationToken.None));
        }
        catch (Exception ex) { Failure("连接或断开主控板", ex); }
        finally { Busy = false; }
    }

    public async Task ApplyAsync(BenchStateItem? item)
    {
        if (item is null || Busy) return;
        if (_config is null || SelectedController is null ||
            !ReferenceEquals(SelectedController, item.Controller) ||
            !States.Any(s => ReferenceEquals(s, item)) ||
            !_config.Controllers.Any(c => ReferenceEquals(c, item.Controller) &&
                c.States.Any(s => ReferenceEquals(s, item.State))))
        { Append(WorkbenchSeverity.Error, "无法执行状态", "工程或控制状态已失效；请重新读取配置。"); return; }
        if (item.State.KeyMask > 0xFFFFF)
        { Append(WorkbenchSeverity.Error, "位掩码不合法", "只允许 20 Key 映射范围内的位掩码。"); return; }
        if (Source == BenchEvidenceSource.Unknown)
        { Append(WorkbenchSeverity.Error, "来源未知", "未指定执行来源，拒绝发送。"); return; }
        Busy = true;
        var requestedSource = Source;
        var requestedRevision = _config.Revision;
        var summary = $"{item.Controller.Name} / {item.State.Name} · 0x{item.State.KeyMask:X5} · {requestedSource}";
        RequestedFact = $"REQUESTED · {summary}";
        AcceptedFact = "UNKNOWN · 等待后端结果";
        PhysicalFact = "UNKNOWN · 未采集物理观测";
        EcuFact = "UNKNOWN · 尚无 ECU 反馈";
        RecordEvidence(BenchEvidencePhase.Requested, summary, item.Controller.ControllerId, item.State.StateId, source: requestedSource);
        Append(WorkbenchSeverity.Progress, "请求控制状态", summary);
        LastOperation = $"处理中：{summary}";
        LastEvidence = "ACK：等待后端返回 · 物理电压：未测量";
        try
        {
            // Physical execution is enforced by DEVICE/INTEGRATION, never authorized by UI state.
            var r = await _port.ApplyStateAsync(requestedRevision, item.Controller.ControllerId,
                item.State.StateId, requestedSource, CancellationToken.None);
            // Never pair a late ACK with a changed source or a newer project revision.
            var contextValid = Source == requestedSource && _config?.Revision == requestedRevision &&
                States.Any(s => ReferenceEquals(s, item));
            _lastState = r.Accepted && contextValid ? item : null;
            _lastOperationId = r.Accepted && contextValid ? r.OperationId : null;
            ((DelegateCommand)SaveMeasurementCommand).RaiseCanExecuteChanged();
            LastOperation = r.Accepted ? $"已接受 · {summary}" : $"已拒绝 · {summary}";
            AcceptedFact = r.Accepted
                ? $"ACCEPTED · {r.AckDescription ?? "后端确认；不代表物理动作"}"
                : $"REJECTED · {r.ErrorCode ?? "未提供错误码"} {r.ErrorMessage ?? ""}";
            RecordEvidence(r.Accepted ? BenchEvidencePhase.TransportAccepted : BenchEvidencePhase.Rejected,
                $"{summary} · {r.AckDescription ?? r.ErrorMessage ?? (r.Accepted ? "后端已接受" : "后端拒绝")}",
                item.Controller.ControllerId, item.State.StateId, r.OperationId, r.CorrelationId,
                r.RawRequest, r.RawResponse, r.TraceReference, errorCode: r.ErrorCode,
                source: r.Source, sessionGeneration: r.SessionGeneration);
            LastEvidence = $"ACK：{r.AckDescription ?? "未提供"} · 实际电压：未测量";
            Append(r.Accepted ? WorkbenchSeverity.Success : WorkbenchSeverity.Error,
                r.Accepted ? "命令接受（非电压测量）" : "命令拒绝/失败",
                $"{summary} | OP {r.OperationId ?? "—"} | Corr {r.CorrelationId ?? "—"} | " +
                $"Generation {r.SessionGeneration?.ToString() ?? "—"} | 来源 {r.Source} | " +
                $"REQ {r.RawRequest ?? "—"} | RESP {r.RawResponse ?? "—"} | " +
                $"Error {r.ErrorCode ?? "—"} {r.ErrorMessage ?? ""}", r.OperationId, r.TraceReference);
        }
        catch (Exception ex) { _lastState = null; _lastOperationId = null; Failure("切换控制状态", ex); }
        finally { Busy = false; }
    }

    public async Task SaveMeasurementAsync()
    {
        if (Busy || _config is null || _lastState is null) return;
        var values = new List<BenchManualVoltage>();
        foreach (var input in Voltages)
        {
            decimal? volts = null;
            if (!string.IsNullOrWhiteSpace(input.Value))
            {
                if (!decimal.TryParse(input.Value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.CurrentCulture, out var parsed))
                { Append(WorkbenchSeverity.Error, "测量值无效", $"CH{input.Channel} 不是有效数值"); return; }
                volts = parsed;
            }
            values.Add(new BenchManualVoltage(input.Channel, volts,
                string.IsNullOrWhiteSpace(OperatorName) ? null : OperatorName.Trim(),
                volts.HasValue ? DateTimeOffset.UtcNow : null, null));
        }
        if (!values.Any(v => v.Volts.HasValue))
        { Append(WorkbenchSeverity.Warning, "没有实测数据", "至少填写一路万用表测量值；空白不可记作 0 V。"); return; }
        Busy = true;
        try
        {
            var measurement = new BenchManualMeasurement(_config.Revision,
                _lastState.Controller.ControllerId, _lastState.State.StateId, _lastOperationId, Source,
                values, string.IsNullOrWhiteSpace(Comment) ? null : Comment.Trim());
            await _port.SaveManualMeasurementAsync(measurement, CancellationToken.None);
            LastEvidence = $"命令记录：{_lastOperationId ?? "未提供"} · 人工测量：已保存，非自动采样";
            RecordEvidence(BenchEvidencePhase.ManualMeasurement,
                "人工万用表记录（非自动采样）："+string.Join(", ", values.Where(v => v.Volts.HasValue)
                    .Select(v => $"CH{v.Channel}={v.Volts}V")),
                measurement.ControllerId, measurement.StateId, measurement.OperationId);
            Append(WorkbenchSeverity.Success, "已保存人工万用表记录",
                $"配置 {measurement.ProjectRevision} · {measurement.ControllerId}/{measurement.StateId} · " +
                string.Join(", ", values.Where(v => v.Volts.HasValue).Select(v => $"CH{v.Channel}={v.Volts}V")),
                _lastOperationId);
        }
        catch (Exception ex) { Failure("保存人工测量", ex); }
        finally { Busy = false; }
    }

    private void UpdateConnection(BenchConnectionSnapshot state)
    {
        _connection = state;
        ConnectionStatus = state.State switch
        {
            BenchConnection.Connected => "已连接", BenchConnection.Connecting => "连接中",
            BenchConnection.Discovering => "发现中", BenchConnection.Faulted => "连接失败",
            _ => "未连接"
        };
        DeviceIdentity = state.DeviceIdentity ?? "—";
        ConnectionDetail = state.State == BenchConnection.Faulted
            ? $"阶段：{state.FailureStage ?? "未知"}；Win32：{WorkbenchDisplay.Win32(state.NativeErrorCode)}；设备：{DeviceIdentity}；{state.Message}"
            : $"{state.Message ?? ""} · SessionGeneration {state.SessionGeneration?.ToString() ?? "—"}";
        RaisePropertyChanged(nameof(ConnectionButtonLabel));
        RecordEvidence(state.State == BenchConnection.Faulted ? BenchEvidencePhase.Error : BenchEvidencePhase.Connection,
            $"{ConnectionStatus} · {ConnectionDetail}", failureStage: state.FailureStage,
            errorCode: state.NativeErrorCode.HasValue ? $"Win32:{state.NativeErrorCode.Value}" : null,
            sessionGeneration: state.SessionGeneration, deviceIdentity: state.DeviceIdentity);
        Append(state.State == BenchConnection.Faulted ? WorkbenchSeverity.Error : WorkbenchSeverity.Neutral,
            "主控板连接状态", $"{ConnectionStatus} · {ConnectionDetail}");
        if (state.State != BenchConnection.Connected)
        {
            _lastState = null; _lastOperationId = null;
            // Any subsequent connection starts from fresh discovery.
            SelectedDevice = null;
            Devices.Clear();
            _candidates = Array.Empty<BenchDevice>();
            RaisePropertyChanged(nameof(ShowDeviceChoice));
        }
        ((DelegateCommand)SaveMeasurementCommand).RaiseCanExecuteChanged();
    }
    private void Failure(string stage, Exception ex)
    {
        ConnectionDetail = $"{stage}：{ex.Message}";
        RecordEvidence(BenchEvidencePhase.Error, $"{stage}: {ex}", failureStage: stage,
            errorCode: ex.HResult != 0 ? $"HRESULT:0x{ex.HResult:X8}" : null);
        Append(WorkbenchSeverity.Error, $"{stage}失败", ex.ToString());
    }
    private void RecordEvidence(BenchEvidencePhase phase, string summary,
        string? controllerId = null, string? stateId = null, string? operationId = null,
        string? correlationId = null, string? requestHex = null, string? responseHex = null,
        string? traceReference = null, string? failureStage = null, string? errorCode = null,
        BenchEvidenceSource? source = null, long? sessionGeneration = null, string? deviceIdentity = null)
    {
        var entry = new BenchEvidenceEntry(DateTimeOffset.UtcNow, phase, source ?? Source,
            _config?.Revision ?? "UNKNOWN", controllerId, stateId,
            deviceIdentity ?? _connection.DeviceIdentity, sessionGeneration ?? _connection.SessionGeneration,
            operationId, correlationId, requestHex, responseHex, traceReference,
            failureStage, errorCode, summary);
        Evidence.Insert(0, entry);
        RefreshEvidenceFilter();
    }

    private void RefreshEvidenceFilter()
    {
        bool Matches(BenchEvidenceEntry e)
        {
            var phaseMatches = EvidencePhaseFilter switch
            {
                "请求" => e.Phase == BenchEvidencePhase.Requested,
                "接受" => e.Phase == BenchEvidencePhase.TransportAccepted,
                "拒绝" => e.Phase == BenchEvidencePhase.Rejected,
                "连接" => e.Phase == BenchEvidencePhase.Connection,
                "测量" => e.Phase == BenchEvidencePhase.ManualMeasurement,
                "错误" => e.Phase == BenchEvidencePhase.Error,
                _ => true
            };
            if (!phaseMatches) return false;
            if (string.IsNullOrWhiteSpace(EvidenceSearch)) return true;
            var q = EvidenceSearch.Trim();
            return new[] { e.Summary, e.OperationId, e.CorrelationId, e.TraceReference,
                e.RequestHex, e.ResponseHex, e.FailureStage, e.ErrorCode, e.DeviceIdentity,
                e.ControllerId, e.StateId }.Any(x => x?.Contains(q, StringComparison.OrdinalIgnoreCase) == true);
        }
        VisibleEvidence.Clear();
        foreach (var entry in Evidence.Where(Matches)) VisibleEvidence.Add(entry);
    }

    private void Append(WorkbenchSeverity severity, string heading, string details,
        string? operationId = null, string? traceRef = null)
    {
        Messages.Insert(0, new WorkbenchMessage(DateTimeOffset.Now, severity, heading, details, operationId, traceRef));
        if (Messages.Count > 200) Messages.RemoveAt(Messages.Count - 1);
    }
}
