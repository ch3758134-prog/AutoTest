using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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

/// <summary>20 KEY_SIM resistor switches, independent of any named ECU Profile.</summary>
public sealed class BenchSwitchItem : BindableBase
{
    private bool _selected;
    public int Index { get; }
    public string Number => (Index + 1).ToString("00");
    public string BitLabel => "K" + Number;
    public uint Bit => 1u << Index;
    public bool IsSelected
    {
        get => _selected;
        set => SetProperty(ref _selected, value);
    }
    public BenchSwitchItem(int zeroBasedIndex) => Index = zeroBasedIndex;
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
    private string _directResult = "先勾选要闭合的开关，再点击“发送当前组合”。右侧查看 ECU 广播报文。";
    private string _ignStatus = "IGN 实际电平未知 · 尚未收到本次主控命令 ACK";
    private string _solenoidStatus = "夹具机械状态 UNKNOWN · 尚未读取当前固件动作值";
    private uint? _lastDirectAcceptedMask;
    private string? _lastDirectOperationId;
    private bool _updatingSwitches;
    private string _comment = "";
    private string _operatorName = "";
    private BenchEvidenceSource _source = BenchEvidenceSource.Physical;
    private BenchConnectionSnapshot _connection = new(BenchConnection.Disconnected, null, null, null, null, null);
    private BenchConfiguration? _config;
    private BenchControllerDefinition? _selectedController;
    private BenchDevice? _selectedDevice;
    private IReadOnlyList<BenchDevice> _candidates = Array.Empty<BenchDevice>();
    private BenchStateItem? _lastState;
    private string? _lastOperationId;

    public BenchWorkbenchViewModel(IBenchWorkbenchPort port,
        Func<AutoTest.Devices.Diagnostics.HardwareBenchController?>? hardware = null)
    {
        _port = port ?? throw new ArgumentNullException(nameof(port));
        KeyActions = new BenchKeyActionEditorViewModel(_port,
            note => Append(WorkbenchSeverity.Progress, "按键动作", note));
        CanMonitor = new BenchCanMonitorViewModel(hardware ?? (() => null));
        CanMessages = new BenchCanEditorViewModel(_port,
            note => Append(WorkbenchSeverity.Progress, "CAN1 发送", note));
        ConnectCommand = new DelegateCommand(async () => await ConnectOrDisconnectAsync(), () => !Busy)
            .ObservesProperty(() => Busy);
        RefreshCommand = new DelegateCommand(async () => await ReloadAsync(), () => !Busy)
            .ObservesProperty(() => Busy);
        ApplyStateCommand = new DelegateCommand<BenchStateItem>(async state => await ApplyAsync(state),
            state => state is not null && !Busy && SelectedController is not null)
            .ObservesProperty(() => Busy);
        SaveMeasurementCommand = new DelegateCommand(async () => await SaveMeasurementAsync(),
            () => !Busy && (_lastState is not null || _lastDirectAcceptedMask.HasValue))
            .ObservesProperty(() => Busy);
        ReleaseAllKeysCommand = new DelegateCommand(async () => await ReleaseAllKeysAsync(),
            () => !Busy).ObservesProperty(() => Busy);
        ApplyDirectMaskCommand = new DelegateCommand(async () => await ApplyDirectMaskAsync(),
            () => !Busy && SelectedKeyMask != 0 && _connection.State == BenchConnection.Connected)
            .ObservesProperty(() => Busy);
        ClearSwitchesCommand = new DelegateCommand(ClearSelectedSwitches,
            () => !Busy).ObservesProperty(() => Busy);
        IgnOnCommand = new DelegateCommand(async () => await SetIgnAsync(true),
            () => !Busy && _connection.State == BenchConnection.Connected)
            .ObservesProperty(() => Busy);
        IgnOffCommand = new DelegateCommand(async () => await SetIgnAsync(false),
            () => !Busy && _connection.State == BenchConnection.Connected)
            .ObservesProperty(() => Busy);
        SolenoidRaiseCommand = new DelegateCommand(async () => await SetSolenoidAsync(0),
            () => !Busy && _connection.State == BenchConnection.Connected)
            .ObservesProperty(() => Busy);
        SolenoidLowerCommand = new DelegateCommand(async () => await SetSolenoidAsync(1),
            () => !Busy && _connection.State == BenchConnection.Connected)
            .ObservesProperty(() => Busy);
        SolenoidReadCommand = new DelegateCommand(async () => await ReadSolenoidAsync(),
            () => !Busy && _connection.State == BenchConnection.Connected)
            .ObservesProperty(() => Busy);
        foreach (var i in Enumerable.Range(0, 20))
        {
            var item = new BenchSwitchItem(i);
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(BenchSwitchItem.IsSelected) && !_updatingSwitches)
                    UpdateSwitchSelection();
            };
            Switches.Add(item);
        }
        foreach (var n in Enumerable.Range(1, 5)) Voltages.Add(new ManualVoltageInput(n));
        Append(WorkbenchSeverity.Neutral, "台架就绪", "尚未读取工程或硬件事实；仅允许经 DEVICE 验证的实际动作。");
    }

    public BenchKeyActionEditorViewModel KeyActions { get; }
    public BenchCanEditorViewModel CanMessages { get; }
    public BenchCanMonitorViewModel CanMonitor { get; }
    public ObservableCollection<BenchControllerDefinition> Controllers { get; } = new();
    public ObservableCollection<BenchStateItem> States { get; } = new();
    public ObservableCollection<BenchDevice> Devices { get; } = new();
    public ObservableCollection<ManualVoltageInput> Voltages { get; } = new();
    public ObservableCollection<BenchSwitchItem> Switches { get; } = new();
    public uint SelectedKeyMask => Switches.Aggregate(0u,
        (mask, item) => item.IsSelected ? mask | item.Bit : mask);
    public int SelectedKeyCount => Switches.Count(x => x.IsSelected);
    public string SelectedKeyMaskText => $"已闭合 {SelectedKeyCount} / 20 路";
    public string SelectedKeyTechnicalMask => $"开关编码 0x{SelectedKeyMask:X5}";
    public string DirectResult
    {
        get => _directResult;
        private set => SetProperty(ref _directResult, value);
    }
    private void UpdateSwitchSelection()
    {
        RaisePropertyChanged(nameof(SelectedKeyMask));
        RaisePropertyChanged(nameof(SelectedKeyCount));
        RaisePropertyChanged(nameof(SelectedKeyMaskText));
        RaisePropertyChanged(nameof(SelectedKeyTechnicalMask));
        ((DelegateCommand)ApplyDirectMaskCommand).RaiseCanExecuteChanged();
        DirectResult = "已修改待发送组合；尚未发出 CAN0 指令。";
    }
    private void ClearSelectedSwitches()
    {
        _updatingSwitches = true;
        try { foreach (var sw in Switches) sw.IsSelected = false; }
        finally { _updatingSwitches = false; }
        UpdateSwitchSelection();
        DirectResult = "仅清除了界面勾选；未发送任何释放帧。若需释放真实按键，请执行「释放全部按键」。";
    }
    public ObservableCollection<WorkbenchMessage> Messages { get; } = new();

    public ICommand ConnectCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ApplyStateCommand { get; }
    public ICommand SaveMeasurementCommand { get; }
    public ICommand ReleaseAllKeysCommand { get; }
    public ICommand ApplyDirectMaskCommand { get; }
    public ICommand ClearSwitchesCommand { get; }
    public ICommand IgnOnCommand { get; }
    public ICommand IgnOffCommand { get; }
    public ICommand SolenoidRaiseCommand { get; }
    public ICommand SolenoidLowerCommand { get; }
    public ICommand SolenoidReadCommand { get; }
    public string SolenoidStatus
    {
        get => _solenoidStatus;
        private set => SetProperty(ref _solenoidStatus, value);
    }
    public string IgnStatus
    {
        get => _ignStatus;
        private set => SetProperty(ref _ignStatus, value);
    }
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
        _ => "Unknown · 不允许作为实测证据"
    };
    public BenchEvidenceSource Source
    {
        get => _source;
        set { if (SetProperty(ref _source, value)) { _lastState = null; _lastOperationId = null;
            _lastDirectAcceptedMask = null; _lastDirectOperationId = null;
            ((DelegateCommand)SaveMeasurementCommand).RaiseCanExecuteChanged();
            RaisePropertyChanged(nameof(SourceDescription)); RaisePropertyChanged(nameof(IsPhysical)); } }
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
        if (_initialized)
        {
            // ViewModels are reused when the independent tool window reopens.
            // Refresh the actual PROJECT so old ECU actions cannot appear current.
            await ReloadAsync();
            return;
        }
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
            await KeyActions.LoadAsync();
            await CanMessages.LoadAsync();
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
            _lastDirectAcceptedMask = null; _lastDirectOperationId = null;
            ((DelegateCommand)SaveMeasurementCommand).RaiseCanExecuteChanged();
            LastOperation = r.Accepted ? $"已接受 · {summary}" : $"已拒绝 · {summary}";
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

    /// <summary>
    /// One explicitly authorized physical CAN0 operation for the selected
    /// combination. The mask is frozen before the await; editing checkboxes
    /// can never change the payload already in flight.
    /// </summary>
    public async Task ApplyDirectMaskAsync()
    {
        if (Busy || SelectedKeyMask == 0) return;
        var mask = SelectedKeyMask;
        Busy = true;
        DirectResult = $"请求真实 CAN0 / 0x730 · MASK 0x{mask:X5}";
        Append(WorkbenchSeverity.Progress, "发出按键组合请求",
            $"KEY_SIM K01～K20 · 0x{mask:X5} / Session {_connection.SessionGeneration?.ToString() ?? "UNKNOWN"}");
        try
        {
            var result = await _port.ApplyPhysicalKeyMaskAsync(mask, CancellationToken.None);
            var accepted = result.Accepted &&
                result.Source == BenchEvidenceSource.Physical;
            _lastState = null;
            _lastOperationId = null;
            _lastDirectAcceptedMask = accepted ? mask : null;
            _lastDirectOperationId = accepted ? result.OperationId : null;
            ((DelegateCommand)SaveMeasurementCommand).RaiseCanExecuteChanged();
            DirectResult = accepted
                ? $"主控已接收按键组合指令（{System.Numerics.BitOperations.PopCount(mask)} 路闭合）。请看右侧 CAN 接收；实际输出电压尚未测量"
                : $"未确认组合生效：{result.ErrorCode ?? "UNKNOWN"} · {result.ErrorMessage ?? result.AckDescription ?? "无有效 ACK"}";
            LastOperation = $"KEY_SIM 组合：0x{mask:X5} · " + (accepted ? "主控 ACK" : "失败/不确定");
            LastEvidence = result.AckDescription ?? "主控无有效 ACK，实际开关状态未知";
            Append(accepted ? WorkbenchSeverity.Success : WorkbenchSeverity.Error,
                accepted ? "主控接受组合命令（非硬件状态反馈）" : "组合命令失败或状态不确定",
                $"MASK 0x{mask:X5} | REQ {result.RawRequest ?? "—"} | RESP {result.RawResponse ?? "—"} | " +
                $"Generation {result.SessionGeneration?.ToString() ?? "—"} | {result.ErrorCode ?? "—"} {result.ErrorMessage ?? ""}",
                result.OperationId, result.TraceReference);
        }
        catch (Exception ex)
        {
            DirectResult = "请求失败；实际开关状态 UNKNOWN：" + ex.Message;
            Failure("应用选中按键组合", ex);
        }
        finally { Busy = false; ((DelegateCommand)ApplyDirectMaskCommand).RaiseCanExecuteChanged(); }
    }

    /// <summary>
    /// IGN ON/OFF commands are explicitly authorized through the shared
    /// WinUSB physical fence. USB ACK is never shown as measured IGN voltage.
    /// </summary>
    public async Task SetIgnAsync(bool on)
    {
        if (Busy) return;
        Busy = true;
        // An IGN output command changes the electrical context. Invalidate
        // any pending manual voltage measurement from a prior key operation.
        _lastState = null; _lastOperationId = null;
        _lastDirectAcceptedMask = null; _lastDirectOperationId = null;
        ((DelegateCommand)SaveMeasurementCommand).RaiseCanExecuteChanged();
        var label = on ? "ON" : "OFF";
        IgnStatus = $"请求 IGN {label} · 等待真实主控 ACK / 电压状态 UNKNOWN";
        Append(WorkbenchSeverity.Progress, "请求真实 IGN " + label,
            "SET_IGN USB CMD 0x12 · " + (on ? "01" : "00"));
        try
        {
            var result = await _port.SetIgnAsync(on, CancellationToken.None);
            IgnStatus = result.Accepted
                ? $"主控 ACK · 已请求 IGN {label}；IGN 实际电压仍未读取"
                : $"IGN {label} 操作失败/不确定：{result.ErrorCode ?? "UNKNOWN"} · 实际电平 UNKNOWN";
            LastOperation = "IGN " + label + " / " + (result.Accepted ? "主控 ACK" : "UNKNOWN / DENIED");
            LastEvidence = result.AckDescription ?? "IGN 无可靠 ACK，实际电平未知";
            Append(result.Accepted ? WorkbenchSeverity.Success : WorkbenchSeverity.Error,
                "IGN " + label + (result.Accepted ? " · 主控已接受（非物理回读）" : " · 失败/拒绝"),
                $"REQ {result.RawRequest ?? "—"} | RESP {result.RawResponse ?? "—"} | " +
                $"Generation {result.SessionGeneration?.ToString() ?? "—"} | " +
                $"{result.ErrorCode ?? "—"} {result.ErrorMessage ?? ""}",
                result.OperationId, result.TraceReference);
        }
        catch (Exception ex)
        {
            IgnStatus = $"IGN {label} 输出状态 UNKNOWN：" + ex.Message;
            Failure("切换 IGN " + label, ex);
        }
        finally { Busy = false; }
    }

    public async Task SetSolenoidAsync(byte action)
    {
        if (Busy || action > 1) return;
        Busy = true;
        // Physical fixture movement invalidates any prior voltage observation
        // context. The request may still fail/timeout, so mechanical state
        // remains UNKNOWN until the operator separately observes it.
        _lastState = null; _lastOperationId = null;
        _lastDirectAcceptedMask = null; _lastDirectOperationId = null;
        ((DelegateCommand)SaveMeasurementCommand).RaiseCanExecuteChanged();
        SolenoidStatus = action == 0
            ? "请求电磁阀升起 · 等待主控 ACK；机械位置未知"
            : "请求电磁阀下落 · 等待主控 ACK；机械压紧未确认";
        Append(WorkbenchSeverity.Progress, "SET_SOL 请求",
            $"真实 USB 0x16 / action {action}");
        try
        {
            var result = await _port.SetSolenoidAsync(action, CancellationToken.None);
            SolenoidStatus = result.Accepted
                ? $"主控已接受 SET_SOL={action}；实际夹具机械状态仍 UNKNOWN"
                : $"SET_SOL={action} 未确认：{result.ErrorCode ?? "UNKNOWN"}；实际机械位置 UNKNOWN";
            LastOperation = $"SET_SOL {action} / " + (result.Accepted ? "主控 ACK" : "UNKNOWN / DENIED");
            LastEvidence = result.AckDescription ?? "主控命令无有效 ACK";
            Append(result.Accepted ? WorkbenchSeverity.Success : WorkbenchSeverity.Error,
                result.Accepted ? "主控接受夹具动作（非到位反馈）" : "夹具动作失败/不确定",
                $"REQ {result.RawRequest ?? "—"} | RESP {result.RawResponse ?? "—"} | " +
                $"Generation {result.SessionGeneration?.ToString() ?? "—"} | " +
                $"{result.ErrorCode ?? "—"} {result.ErrorMessage ?? ""}",
                result.OperationId, result.TraceReference);
        }
        catch (Exception ex)
        {
            SolenoidStatus = "SET_SOL 请求失败，机械状态 UNKNOWN：" + ex.Message;
            Failure("夹具电磁阀操作", ex);
        }
        finally { Busy = false; }
    }

    public async Task ReadSolenoidAsync()
    {
        if (Busy) return;
        Busy = true;
        try
        {
            var result = await _port.ReadSolenoidAsync(CancellationToken.None);
            SolenoidStatus = result.Accepted
                ? $"GET_SOL 固件状态：{result.RawResponse ?? "UNKNOWN"}；机械到位仍未验证"
                : $"GET_SOL 读取失败：{result.ErrorCode ?? "UNKNOWN"}；机械状态 UNKNOWN";
            Append(result.Accepted ? WorkbenchSeverity.Success : WorkbenchSeverity.Error,
                "GET_SOL / GPIO 动作编码", result.AckDescription ?? result.ErrorMessage ??
                "固件状态未知，不代表夹具机械到位",
                result.OperationId, result.TraceReference);
        }
        catch (Exception ex)
        {
            SolenoidStatus = "GET_SOL 读取异常；机械状态 UNKNOWN";
            Failure("读取夹具固件状态", ex);
        }
        finally { Busy = false; }
    }

    public async Task RefreshConnectionAsync()
    {
        if (Busy) return;
        try { UpdateConnection(await _port.GetConnectionAsync(CancellationToken.None)); }
        catch (Exception ex) { Failure("刷新共享主控连接状态", ex); }
    }

    public async Task ReleaseAllKeysAsync()
    {
        if (Busy) return;
        Busy = true;
        try
        {
            var result = await _port.ReleaseAllPhysicalKeysAsync(CancellationToken.None);
            _lastState = null;
            _lastOperationId = null;
            _lastDirectAcceptedMask = null;
            _lastDirectOperationId = null;
            ((DelegateCommand)SaveMeasurementCommand).RaiseCanExecuteChanged();
            LastOperation = result.Accepted ? "已向 CAN0 发送全部释放" :
                "释放未获确认 / 已拒绝";
            LastEvidence = result.AckDescription ?? "主控无有效 ACK，实际开关状态未知";
            DirectResult = result.Accepted
                ? "主控确认已发送全部释放帧；实际 KEY_SIM 开关状态没有回读"
                : "全部释放未确认；实际 KEY_SIM 开关状态 UNKNOWN";
            if (result.Accepted)
            {
                _updatingSwitches = true;
                try { foreach (var sw in Switches) sw.IsSelected = false; }
                finally { _updatingSwitches = false; }
                UpdateSwitchSelection();
                DirectResult = "主控确认已发送全部释放帧；实际 KEY_SIM 开关状态没有回读";
            }
            Append(result.Accepted ? WorkbenchSeverity.Success : WorkbenchSeverity.Error,
                result.Accepted ? "真实全部释放请求已被主控接受" : "真实全部释放未确认",
                "KEY_SIM CAN0 0x730 / 0x80 · " +
                (result.ErrorCode ?? result.AckDescription ?? "无板级开关状态测量"),
                result.OperationId, result.TraceReference);
        }
        catch (Exception ex)
        {
            LastEvidence = "释放结果 UNKNOWN；检查当前设备连接和台架状态";
            Failure("真实全部释放", ex);
        }
        finally { Busy = false; }
    }

    public async Task SaveMeasurementAsync()
    {
        if (Busy || (_lastState is null && !_lastDirectAcceptedMask.HasValue)) return;
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
            // A direct KEY_SIM command has no ECU Profile by design. Bind the
            // manual meter values to the last acknowledged physical mask/OpId,
            // never to currently checked UI bits or an invented analog reading.
            var direct = _lastDirectAcceptedMask.HasValue && _lastState is null;
            var measurement = direct
                ? new BenchManualMeasurement("RAW_KEYSIM", "KEYSIM_CAN0",
                    $"MASK_0x{_lastDirectAcceptedMask!.Value:X5}", _lastDirectOperationId,
                    BenchEvidenceSource.Physical, values,
                    string.IsNullOrWhiteSpace(Comment) ? null : Comment.Trim())
                : new BenchManualMeasurement(_config!.Revision,
                    _lastState!.Controller.ControllerId, _lastState.State.StateId,
                    _lastOperationId, Source, values,
                    string.IsNullOrWhiteSpace(Comment) ? null : Comment.Trim());
            await _port.SaveManualMeasurementAsync(measurement, CancellationToken.None);
            LastEvidence = $"命令记录：{measurement.OperationId ?? "未提供"} · 人工测量：已保存，非自动采样";
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
        Append(state.State == BenchConnection.Faulted ? WorkbenchSeverity.Error : WorkbenchSeverity.Neutral,
            "主控板连接状态", $"{ConnectionStatus} · {ConnectionDetail}");
        if (state.State != BenchConnection.Connected)
        {
            _lastState = null; _lastOperationId = null;
            _lastDirectAcceptedMask = null; _lastDirectOperationId = null;
            IgnStatus = "USB 已断连；IGN 实际电平未知，不能推断已关闭";
            SolenoidStatus = "USB 已断连；夹具机械状态 UNKNOWN，不能推断已升起或已压紧";
            // Any subsequent connection starts from fresh discovery.
            SelectedDevice = null;
            Devices.Clear();
            _candidates = Array.Empty<BenchDevice>();
            RaisePropertyChanged(nameof(ShowDeviceChoice));
        }
        ((DelegateCommand)SaveMeasurementCommand).RaiseCanExecuteChanged();
        ((DelegateCommand)ApplyDirectMaskCommand).RaiseCanExecuteChanged();
        ((DelegateCommand)IgnOnCommand).RaiseCanExecuteChanged();
        ((DelegateCommand)IgnOffCommand).RaiseCanExecuteChanged();
        ((DelegateCommand)SolenoidRaiseCommand).RaiseCanExecuteChanged();
        ((DelegateCommand)SolenoidLowerCommand).RaiseCanExecuteChanged();
        ((DelegateCommand)SolenoidReadCommand).RaiseCanExecuteChanged();
    }
    private void Failure(string stage, Exception ex)
    {
        ConnectionDetail = $"{stage}：{ex.Message}";
        Append(WorkbenchSeverity.Error, $"{stage}失败", ex.ToString());
    }
    private void Append(WorkbenchSeverity severity, string heading, string details,
        string? operationId = null, string? traceRef = null)
    {
        Messages.Insert(0, new WorkbenchMessage(DateTimeOffset.Now, severity, heading, details, operationId, traceRef));
        if (Messages.Count > 200) Messages.RemoveAt(Messages.Count - 1);
    }
}
