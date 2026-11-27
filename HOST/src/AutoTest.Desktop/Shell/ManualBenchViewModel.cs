using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using AutoTest.Contracts;
using AutoTest.Desktop.Hardware;
using AutoTest.Desktop.Dbc;
using AutoTest.Desktop.Projects;
using AutoTest.Devices.Can;
using AutoTest.Devices.Station;
using AutoTest.Projects.ControllerConfig;
using AutoTest.Projects.Dbc;
using Prism.Commands;
using Prism.Mvvm;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// Dedicated, real-device manual station. Uses the same hardware session as the IDE.
/// All physical writes pass the existing WinUSB physical command fence.
/// No Mock, no second CAN receiver, no simulated ACK or inferred electrical state.
/// </summary>
public sealed partial class ManualBenchViewModel : BindableBase, IDisposable
{
    public sealed class KeyChoice(int number) : BindableBase
    {
        private bool _checked;
        public int Number { get; } = number;
        public uint Mask => 1u << (Number - 1);
        public string Label => "K" + Number.ToString("00", CultureInfo.InvariantCulture);
        public bool Checked { get => _checked; set => SetProperty(ref _checked, value); }
    }

    public sealed record ProfileChoice(Guid AssetId, string Name)
    {
        public override string ToString() => Name;
    }

    public sealed record NamedOperation(string Name, uint Mask)
    {
        public string Display => Name + " · 0x" + Mask.ToString("X5", CultureInfo.InvariantCulture);
    }

    public sealed record DbcChoice(Guid AssetId, int Revision, string Sha256,
        string AssetName, DbcMessage Message, DbcDisplayNames DisplayNames,
        IReadOnlyDictionary<string, IReadOnlyDictionary<long, string>>? ValueLabels = null)
    {
        public string Display => AssetName + " / " + DisplayNames.MessageLabel(Message) + " [0x" +
            Message.ArbitrationId.ToString("X") + "]" +
            (Message.IsExtended ? " 扩展" : " 标准");
        public string Purpose => DisplayNames.MessageSummary(Message.Id);
    }
    public sealed class DbcSignalValue : DbcEditableSignal
    {
        public bool IsEditable { get; }
        public DbcSignalValue(DbcSignal signal, string? label = null,
            string? description = null, IReadOnlyDictionary<long, string>? rawLabels = null,
            string value = "", string? summary = null, bool isEditable = true)
            : base(signal, label, description, rawLabels, value, summary)
            => IsEditable = isEditable;
    }

    public sealed record BenchLogEntry(string Time, string Channel, string Direction,
        string Id, string Data, string Detail);

    private readonly ProjectWorkspaceService _projects;
    private readonly HardwareConnectionService _hardware;
    private bool _disposed;
    private bool _busy;
    private bool _connected;
    private bool _extended;
    private string _canIdHex = "701";
    private string _canDataHex = "";
    private string _canConfirmation = "";
    private string _keyConfirmation = "";
    private bool _isKeyEditorExpanded;
    public bool IsKeyEditorExpanded
    {
        get => _isKeyEditorExpanded;
        set => SetProperty(ref _isKeyEditorExpanded, value);
    }
    private string _ignConfirmation = "";
    private string _clampConfirmation = "";
    private string _lastClampStatus = "夹具状态 UNKNOWN：尚未读取本次 MCU GPIO。";
    private byte _selectedClamp = 1;
    private bool _clampPress;
    private string _status = "仅操作真实硬件。请先连接主控板并加载工程配置。";
    private string _lastKeyAck = "未知：未收到本会话的 Key 操作 ACK";
    private string _lastIgnAck = "未知：未在本会话下发 IGN";
    private string _ignGpioLabel = "未读取";
    private string _clamp1GpioLabel = "未读取";
    private string _clamp2GpioLabel = "未读取";
    private ProfileChoice? _selectedProfile;
    private EcuControlProfile? _controller;
    private bool _useDbcMessage;
    private DbcChoice? _selectedDbcMessage;
    private Guid _loadedControllerAsset;
    private string? _loadedProjectManifest;
    private int _loadedControllerRevision;
    private string? _loadedControllerSha;

    public ObservableCollection<KeyChoice> Keys { get; } = new(
        Enumerable.Range(1, 20).Select(i => new KeyChoice(i)));
    public ObservableCollection<ProfileChoice> Profiles { get; } = new();
    public ObservableCollection<NamedOperation> NamedOperations { get; } = new();
    public ObservableCollection<DbcChoice> DbcMessages { get; } = new();
    public ObservableCollection<DbcSignalValue> DbcSignals { get; } = new();
    public ObservableCollection<BenchLogEntry> Logs { get; } = new();
    private BenchLogEntry? _selectedLog;
    public BenchLogEntry? SelectedLog
    {
        get => _selectedLog;
        set => SetProperty(ref _selectedLog, value);
    }
    public ICollectionView FilteredLogs { get; }

    private string _logFilter = "";
    public string LogFilter
    {
        get => _logFilter;
        set
        {
            if (SetProperty(ref _logFilter, value ?? "")) FilteredLogs.Refresh();
        }
    }

    private bool MatchesLog(object value)
    {
        if (value is not BenchLogEntry entry) return false;
        var term = LogFilter.Trim();
        return term.Length == 0 || entry.Time.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            entry.Channel.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            entry.Direction.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            entry.Id.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            entry.Data.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            entry.Detail.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    public IReadOnlyList<HardwareConnectionService.EcuCanRateChoice> EcuCanRateChoices =>
        HardwareConnectionService.EcuCanRateChoices;
    public int RequestedEcuCanBitrateKbps
    {
        get => _hardware.RequestedEcuCanBitrateKbps;
        set
        {
            if (value == _hardware.RequestedEcuCanBitrateKbps) return;
            try
            {
                _hardware.SelectRequestedEcuCanBitrate(value);
                RaisePropertyChanged(nameof(RequestedEcuCanBitrateKbps));
                RaisePropertyChanged(nameof(EcuCanBitrateStatus));
                Status = "ECU CAN 目标已选择 " + value +
                    "k；" + _hardware.EcuCanBitrateStatus +
                    "。按“应用”后才执行设备设置/读回。";
                RaiseCommands();
            }
            catch (Exception error) when (error is InvalidOperationException or
                ArgumentOutOfRangeException)
            {
                Status = "更改 ECU CAN 期望码率失败：" + error.Message;
                RaisePropertyChanged(nameof(RequestedEcuCanBitrateKbps));
            }
        }
    }
    public string EcuCanBitrateStatus => _hardware.EcuCanBitrateStatus;

    public DelegateCommand ApplyEcuCanBitrateCommand { get; private set; } = null!;
    private async Task ApplyEcuCanBitrateAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        ApplyEcuCanBitrateCommand.RaiseCanExecuteChanged();
        try
        {
            int actual = await _hardware.ApplyRequestedEcuCanBitrateAsync();
            Status = "ECU CAN 设备已读回确认 " + actual +
                " kbit/s；KEY CAN 未改。实际总线波特率仍待现场仪器验证。";
        }
        catch (Exception error)
        {
            Status = "ECU CAN 码率应用失败：" + error.Message;
        }
        finally
        {
            IsBusy = false;
            RaisePropertyChanged(nameof(EcuCanBitrateStatus));
            ApplyEcuCanBitrateCommand.RaiseCanExecuteChanged();
        }
    }
    private void OnSelectedEcuCanRateChanged(int kbps)
    {
        var dispatcher = global::System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || _disposed) return;
        void NotifyRate()
        {
            if (_disposed) return;
            RaisePropertyChanged(nameof(RequestedEcuCanBitrateKbps));
            RaisePropertyChanged(nameof(EcuCanBitrateStatus));
            RaiseCommands();
        }
        if (dispatcher.CheckAccess()) NotifyRate();
        else dispatcher.BeginInvoke(new Action(NotifyRate));
    }

    public bool IsConnected => _hardware.IsConnected;
    public string ConnectionActionLabel => IsConnected ? "断开 USB" : "连接 USB";
    public string ConnectionStatus => IsConnected
        ? "USB 已连接 · G" + _hardware.CurrentStationEpoch + " · " +
          (_hardware.ConnectedDeviceIdentity ?? "设备身份未返回") +
          " · FW " + (_hardware.ConnectedFirmwareVersion ?? "未知") +
          " · ECU CAN：A1/A12 · KEY CAN：B1/B11"
        : "USB 未连接 · 不继承上次连接的实体状态";
    public bool IsBusy { get => _busy; private set { if (SetProperty(ref _busy, value)) RaiseCommands(); } }
    public bool IsRawCanMode => !UseDbcMessage;
    public bool UseDbcMessage
    {
        get => _useDbcMessage;
        set
        {
            if (SetProperty(ref _useDbcMessage, value))
            {
                RaisePropertyChanged(nameof(IsRawCanMode));
                CanFrameEdited();
            }
        }
    }
    public DbcChoice? SelectedDbcMessage
    {
        get => _selectedDbcMessage;
        set
        {
            if (!SetProperty(ref _selectedDbcMessage, value)) return;
            foreach (var signal in DbcSignals) signal.PropertyChanged -= DbcSignalChanged;
            DbcSignals.Clear();
            if (value is not null)
            {
                foreach (var signal in value.Message.Signals)
                {
                    var alias = value.DisplayNames.SignalAlias(value.Message.Id, signal.Name);
                    var rawLabels = value.ValueLabels is not null &&
                        value.ValueLabels.TryGetValue(signal.Name, out var labels) ? labels : null;
                    var signalValue = new DbcSignalValue(signal,
                        string.IsNullOrWhiteSpace(alias) ? signal.Name : alias,
                        value.DisplayNames.SignalDescription(value.Message.Id, signal.Name),
                        rawLabels, summary: value.DisplayNames.SignalSummary(value.Message.Id, signal.Name));
                    signalValue.PropertyChanged += DbcSignalChanged;
                    DbcSignals.Add(signalValue);
                }
            }
            CanFrameEdited();
        }
    }
    private void DbcSignalChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DbcSignalValue.Value)) CanFrameEdited();
    }
    public bool Extended { get => _extended; set { if (SetProperty(ref _extended, value)) CanFrameEdited(); } }
    public string CanIdHex { get => _canIdHex; set { if (SetProperty(ref _canIdHex, value)) CanFrameEdited(); } }
    public string CanDataHex { get => _canDataHex; set { if (SetProperty(ref _canDataHex, value)) CanFrameEdited(); } }
    public string CanConfirmation { get => _canConfirmation; set => SetProperty(ref _canConfirmation, value ?? ""); }
    public string KeyConfirmation { get => _keyConfirmation; set => SetProperty(ref _keyConfirmation, value ?? ""); }
    public string IgnConfirmation { get => _ignConfirmation; set => SetProperty(ref _ignConfirmation, value ?? ""); }
    public string ClampConfirmation { get => _clampConfirmation; set => SetProperty(ref _clampConfirmation, value ?? ""); }
    public string LastClampStatus { get => _lastClampStatus; private set => SetProperty(ref _lastClampStatus, value); }
    public string IgnGpioLabel { get => _ignGpioLabel; private set => SetProperty(ref _ignGpioLabel, value); }
    public string Clamp1GpioLabel { get => _clamp1GpioLabel; private set => SetProperty(ref _clamp1GpioLabel, value); }
    public string Clamp2GpioLabel { get => _clamp2GpioLabel; private set => SetProperty(ref _clamp2GpioLabel, value); }
    private void ShowClampGpio(byte levels)
    {
        Clamp1GpioLabel = (levels & 1) == 0 ? "GPIO 低" : "GPIO 高";
        Clamp2GpioLabel = (levels & 2) == 0 ? "GPIO 低" : "GPIO 高";
    }
    public bool Clamp1Selected { get => _selectedClamp == 1; set { if (value) ChooseClamp(1); } }
    public bool Clamp2Selected { get => _selectedClamp == 2; set { if (value) ChooseClamp(2); } }
    public bool ClampPress { get => _clampPress; set { if (value) ChooseClampState(true); } }
    public bool ClampRelease { get => !_clampPress; set { if (value) ChooseClampState(false); } }
    public string ClampExpectedConsent => "AUTHORIZE CLAMP " + _selectedClamp +
        (_clampPress ? " PRESS" : " RELEASE");
    private void ChooseClamp(byte clamp)
    {
        if (_selectedClamp == clamp) return;
        _selectedClamp = clamp;
        ClampConfirmation = "";
        RaisePropertyChanged(nameof(Clamp1Selected));
        RaisePropertyChanged(nameof(Clamp2Selected));
        RaisePropertyChanged(nameof(ClampExpectedConsent));
    }
    private void ChooseClampState(bool press)
    {
        if (_clampPress == press) return;
        _clampPress = press;
        ClampConfirmation = "";
        RaisePropertyChanged(nameof(ClampPress));
        RaisePropertyChanged(nameof(ClampRelease));
        RaisePropertyChanged(nameof(ClampExpectedConsent));
    }
    public string LastKeyAck { get => _lastKeyAck; private set => SetProperty(ref _lastKeyAck, value); }
    public string LastIgnAck { get => _lastIgnAck; private set => SetProperty(ref _lastIgnAck, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public uint DraftMask => Keys.Aggregate(0u, (mask, key) => key.Checked ? mask | key.Mask : mask);
    public string DraftMaskLabel => "待发送 Key 掩码 0x" + DraftMask.ToString("X5", CultureInfo.InvariantCulture);
    public string KeyRequestStatus => _hardware.KeyRequestStatus;
    public bool HasConfiguredKeys => _controller?.KeyHardwarePolicy is not null;
    public bool CanSendHardware => IsConnected && !IsBusy && !_hardware.IsTestRunApproved;
    public bool CanSendManualEcuCan => CanSendHardware;
    public bool CanOperateGpio => CanSendHardware && _hardware.HasV02GpioContract;
    public bool CanReadGpio => IsConnected && !IsBusy && _hardware.HasV02GpioContract;
    // Interactive controls are enabled without USB; actual writes are still checked by the hardware service.
    public bool CanInvokeManualIo => !IsBusy;
    public bool CanSendClamp => CanInvokeManualIo;
    public string CanExpectedConsent => ExpectedConsent(TryBuildCanFrame);
    public string KeyExpectedConsent => ExpectedConsent(TryBuildKeyFrame);
    public string IgnExpectedConsent => "AUTHORIZE IGN " + (IgnOn ? "ON" : "OFF");
    private bool _ignOn;
    public bool IgnOff
    {
        get => !IgnOn;
        set { if (value) IgnOn = false; }
    }
    public bool IgnOn
    {
        get => _ignOn;
        set { if (SetProperty(ref _ignOn, value))
        {
            IgnConfirmation = "";
            RaisePropertyChanged(nameof(IgnOff));
            RaisePropertyChanged(nameof(IgnExpectedConsent));
        } }
    }
    public ProfileChoice? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetProperty(ref _selectedProfile, value))
                _ = LoadSelectedProfileAsync();
        }
    }
    public string ControllerSummary => _controller is null ? "20 Key 直接操作 · 不依赖旧配置资源" :
        _controller.EcuName + " / " + _controller.HardwareModel + " / " +
        (_controller.KeyHardwarePolicy is null ? "未配置可用 Key" :
        "可用 Key 0x" + _controller.KeyHardwarePolicy.SupportedMask.ToString("X5"));
    public DelegateCommand ConnectCommand { get; }
    public DelegateCommand ReloadProfilesCommand { get; }
    public DelegateCommand SendCanCommand { get; }
    public DelegateCommand SendKeyCommand { get; }
    public DelegateCommand ReleaseKeysCommand { get; }
    public DelegateCommand ReleaseAllKeysCommand { get; }
    public DelegateCommand SendIgnCommand { get; }
    public DelegateCommand ReadIgnCommand { get; }
    public DelegateCommand SendClampCommand { get; }
    public DelegateCommand ReadClampsCommand { get; }
    public DelegateCommand ClearLogsCommand { get; }

    public ManualBenchViewModel(ProjectWorkspaceService projects, HardwareConnectionService hardware)
    {
        _projects = projects;
        _hardware = hardware;
        FilteredLogs = CollectionViewSource.GetDefaultView(Logs);
        FilteredLogs.Filter = MatchesLog;
        InitializeMonitor();
        _connected = hardware.IsConnected;
        foreach (var key in Keys) key.PropertyChanged += KeyChanged;
        _hardware.FrameObserved += FrameObserved;
        _hardware.EvidenceRecorded += EvidenceRecorded;
        _hardware.ConnectionChanged += ConnectionChanged;
        _hardware.KeyRequestStateChanged += OnKeyRequestStateChanged;
        _hardware.EcuCanBitrateSelectionChanged += OnSelectedEcuCanRateChanged;
        _hardware.TestRunAuthorizationChanged += TestRunAuthorizationChanged;
        _projects.CurrentChanged += ProjectChanged;
        ApplyEcuCanBitrateCommand = new DelegateCommand(
            async () => await ApplyEcuCanBitrateAsync(), () => !IsBusy);
        ConnectCommand = new DelegateCommand(async () => await ToggleConnectionAsync(),
            () => !IsBusy && !_hardware.IsTestRunApproved);
        ReloadProfilesCommand = new DelegateCommand(async () => await ReloadResourcesAsync(), () => !IsBusy);
        SendCanCommand = new DelegateCommand(async () => await SendCanAsync(), () => !IsBusy);
        SendKeyCommand = new DelegateCommand(async () => await ApplyKeysAsync(), () => !IsBusy);
        ReleaseKeysCommand = new DelegateCommand(ReleaseDraftKeys, () => !IsBusy);
        ReleaseAllKeysCommand = new DelegateCommand(
            async () => await ReleaseAllKeysAsync(),
            () => _hardware.IsConnected && (!IsBusy || IsKeyActionRunning) &&
                !_hardware.IsTestRunApproved && !_hardware.IsManualCanBatchApproved);
        SendIgnCommand = new DelegateCommand(async () => await ApplyIgnAsync(), () => CanInvokeManualIo);
        ReadIgnCommand = new DelegateCommand(async () => await ReadIgnAsync(), () => CanInvokeManualIo);
        SendClampCommand = new DelegateCommand(async () => await ApplyClampAsync(), () => CanInvokeManualIo);
        ReadClampsCommand = new DelegateCommand(async () => await ReadClampsAsync(), () => CanInvokeManualIo);
        ClearLogsCommand = new DelegateCommand(() => Logs.Clear());
        InitializeCanRows();
        InitializeKeyActions();
        InitializeExternalKeyMonitor();
        _ = ReloadResourcesAsync();
    }

    private void CanFrameEdited()
    {
        CanConfirmation = "";
        RaisePropertyChanged(nameof(CanExpectedConsent));
    }

    private void KeyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(KeyChoice.Checked)) return;
        KeyConfirmation = "";
        RaisePropertyChanged(nameof(DraftMask));
        RaisePropertyChanged(nameof(DraftMaskLabel));
        RaisePropertyChanged(nameof(KeyExpectedConsent));
    }

    private void OnKeyRequestStateChanged()
    {
        var dispatcher = global::System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        dispatcher.BeginInvoke(new Action(() =>
        {
            if (_disposed) return;
            RaisePropertyChanged(nameof(KeyRequestStatus));
            ReleaseAllKeysCommand?.RaiseCanExecuteChanged();
        }));
    }

    private void TestRunAuthorizationChanged(bool approved) =>
        global::System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_disposed) return;
            if (approved) { _ = StopCanRowsAsync(); _keyActionStop?.Cancel(); Status = "自动测试占用 ECU：手动发送已停止。"; }
            else Status = "自动测试授权已撤销；手动操作前请重新确认硬件实际状态。";
            RaiseCommands();
        }));

    private void ConnectionChanged(long epoch)
    {
        global::System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_disposed) return;
            _connected = epoch != 0;
            ResetExternalKeyMonitor(epoch);
            if (epoch == 0) { _ = StopCanRowsAsync(); _keyActionStop?.Cancel(); }
            CanConfirmation = "";
            KeyConfirmation = "";
            IgnConfirmation = "";
            ClampConfirmation = "";
            LastClampStatus = "连接世代已改变；夹具实体状态 UNKNOWN，需重新核对现场。";
            IgnGpioLabel = "未读取";
            Clamp1GpioLabel = "未读取";
            Clamp2GpioLabel = "未读取";
            LastKeyAck = "未知：连接已改变，原状态不可继承";
            LastIgnAck = "未知：连接已改变，原状态不可继承";
            RaisePropertyChanged(nameof(IsConnected));
            RaisePropertyChanged(nameof(ConnectionStatus));
            RaisePropertyChanged(nameof(ConnectionActionLabel));
            RaisePropertyChanged(nameof(CanSendHardware));
            RaisePropertyChanged(nameof(KeyRequestStatus));
            RaiseCommands();
        }));
    }

    private void ProjectChanged(object? sender, EventArgs e) =>
        global::System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(async () =>
        {
            if (_disposed) return;
            // Manifest revisions also change during ordinary autosave.
            // Do not stop a live exact-frame batch for unrelated edits.
            if (IsCanBatchRunning && !ActiveCanProjectStillValid())
            {
                await StopCanRowsAsync();
                Status = "ECU CAN 已停止：工程或发送所用 DBC 已变化。";
            }
            if (IsKeyActionRunning)
            {
                var current = _projects.Current;
                var session = _keyCatalogSession;
                if (current is null || session is null ||
                    current.Definition.Id != session.ProjectId ||
                    !string.Equals(current.ManifestPath, session.ManifestPath, StringComparison.OrdinalIgnoreCase) ||
                    !current.Definition.Assets.Any(asset => asset.Id == session.AssetId &&
                        asset.Revision == session.AssetRevision &&
                        string.Equals(asset.Sha256, session.FileSha256, StringComparison.OrdinalIgnoreCase)))
                    await StopKeyActionAsync();
            }
            if (!_disposed && !IsCanBatchRunning && !IsKeyActionRunning)
                await ReloadResourcesAsync();
        }));

    private void FrameObserved(CanObservation observation)
    {
        if (!_disposed) QueueMonitorObservation(observation);
    }

    private void EvidenceRecorded(string text) =>
        global::System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_disposed) Append(new BenchLogEntry(DateTime.Now.ToString("HH:mm:ss.fff"),
                "USB", "事件", "", "", text));
        }));

    private void Append(BenchLogEntry record)
    {
        Logs.Add(record);
        if (SelectedLog is null) SelectedLog = record;
        while (Logs.Count > 700) Logs.RemoveAt(0);
    }

    private void RaiseCommands()
    {
        NotifyKeyCommands();
        NotifyCanRows();
        RaisePropertyChanged(nameof(CanSendHardware));
        RaisePropertyChanged(nameof(CanSendManualEcuCan));
        RaisePropertyChanged(nameof(CanOperateGpio));
        RaisePropertyChanged(nameof(CanReadGpio));
        RaisePropertyChanged(nameof(CanSendClamp));
        RaisePropertyChanged(nameof(CanInvokeManualIo));
        ConnectCommand?.RaiseCanExecuteChanged();
        ReloadProfilesCommand?.RaiseCanExecuteChanged();
        SendCanCommand?.RaiseCanExecuteChanged();
        SendKeyCommand?.RaiseCanExecuteChanged();
        SendIgnCommand?.RaiseCanExecuteChanged();
        ReadIgnCommand?.RaiseCanExecuteChanged();
        SendClampCommand?.RaiseCanExecuteChanged();
        ReadClampsCommand?.RaiseCanExecuteChanged();
        ReleaseKeysCommand?.RaiseCanExecuteChanged();
        ReleaseAllKeysCommand?.RaiseCanExecuteChanged();
    }

    private async Task Execute(string operation, Func<Task> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var originalStatus = Status;
            await action();
            if (Status == originalStatus)
                Status = operation + "：操作已完成（主控 ACK 不等于 ECU/机械响应）。";
        }
        catch (Exception e)
        {
            Status = operation + "失败：" + e.Message;
            Append(new BenchLogEntry(DateTime.Now.ToString("HH:mm:ss.fff"), "工位", "ERROR",
                "", "", Status));
        }
        finally { IsBusy = false; }
    }

    private async Task ToggleConnectionAsync() => await Execute("USB", async () =>
    {
        if (IsCanBatchRunning) await StopCanRowsAsync();
        if (_hardware.IsConnected) await _hardware.DisconnectAsync();
        else
        {
            // Faulted still owns a USB session, even though it is not Connected.
            if (_hardware.HasSession) await _hardware.DisconnectAsync();
            await _hardware.ConnectAsync();
        }
        RaisePropertyChanged(nameof(ConnectionStatus));
        RaisePropertyChanged(nameof(ConnectionActionLabel));
    });

    public async Task ReloadResourcesAsync()
    {
        if (IsCanBatchRunning || IsKeyActionRunning) return;
        await ReloadProfilesAsync();
        await ReloadDbcAsync();
        try { await RestoreCanRowsForProjectAsync(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                                       InvalidDataException)
        {
            // An unsaved old-project draft must not silently become the new
            // project's CAN send table after a failed disk write.
            Status = "CAN 工作配置切换被阻止：" + error.Message;
        }
        await ReloadKeyActionsAsync();
    }

    private async Task ReloadDbcAsync()
    {
        DbcMessages.Clear();
        SelectedDbcMessage = null;
        var project = _projects.Current;
        if (project is null) { UseDbcMessage = false; return; }
        try
        {
            foreach (var asset in project.Definition.Assets.Where(a =>
                a.Kind == ProjectWorkspaceService.DbcKind))
            {
                var loaded = await _projects.OpenDbcEditorAsync(asset.Id);
                if (!string.Equals(project.ManifestPath, _projects.Current?.ManifestPath,
                    StringComparison.OrdinalIgnoreCase)) return;
                foreach (var message in loaded.Document.Model.Messages.OrderBy(x => x.Name))
                {
                    var labels = loaded.Document.ValueTables.Where(x => x.MessageId == message.Id)
                        .ToDictionary(x => x.SignalName,
                            x => (IReadOnlyDictionary<long, string>)x.Labels,
                            StringComparer.Ordinal);
                    DbcMessages.Add(new DbcChoice(asset.Id.Value, asset.Revision,
                        asset.Sha256, asset.Name, message, loaded.Document.DisplayNames, labels));
                }
            }
            SelectedDbcMessage = DbcMessages.FirstOrDefault();
            if (SelectedDbcMessage is not null) UseDbcMessage = true;
        }
        catch (Exception ex)
        {
            Status = "DBC 资源读取失败：" + ex.Message;
            DbcMessages.Clear();
            SelectedDbcMessage = null;
            UseDbcMessage = false;
        }
    }

    public async Task ReloadProfilesAsync()
    {
        if (_disposed || IsBusy) return;
        try
        {
            var old = SelectedProfile?.AssetId;
            Profiles.Clear();
            _controller = null;
            _loadedControllerAsset = Guid.Empty;
            _loadedControllerSha = null;
            _loadedProjectManifest = null;
            NamedOperations.Clear();
            foreach (var asset in _projects.Current?.Definition.Assets.Where(
                a => a.Kind == ProjectWorkspaceService.ControllerKind) ?? [])
                Profiles.Add(new ProfileChoice(asset.Id.Value, asset.Name));
            _selectedProfile = Profiles.FirstOrDefault(p => p.AssetId == old) ?? Profiles.FirstOrDefault();
            RaisePropertyChanged(nameof(SelectedProfile));
            await LoadSelectedProfileAsync();
        }
        catch (Exception ex) { Status = "加载 Controller 配置失败：" + ex.Message; }
    }

    private async Task LoadSelectedProfileAsync()
    {
        if (_disposed) return;
        _controller = null;
        _loadedControllerAsset = Guid.Empty;
        _loadedControllerSha = null;
        _loadedProjectManifest = null;
        NamedOperations.Clear();
        try
        {
            if (SelectedProfile is { } selected)
            {
                var asset = _projects.Current?.Definition.Assets.SingleOrDefault(
                    a => a.Id.Value == selected.AssetId && a.Kind == ProjectWorkspaceService.ControllerKind);
                if (asset is null) throw new InvalidOperationException("Controller 资源不属于当前工程。");
                var session = await _projects.OpenConfigurationAsync(asset.Id);
                if (SelectedProfile?.AssetId != selected.AssetId) return;
                _controller = session.Document as EcuControlProfile
                    ?? throw new InvalidDataException("当前资源不是 Controller 配置。");
                _controller.Validate();
                _loadedControllerAsset = session.AssetId.Value;
                _loadedControllerRevision = session.AssetRevision;
                _loadedControllerSha = session.FileSha256;
                _loadedProjectManifest = session.ManifestPath;
                foreach (var controller in _controller.Controllers.OrderBy(c => c.Order))
                foreach (var state in controller.States.OrderBy(s => s.Order).Where(s => s.KeyMask != 0))
                    NamedOperations.Add(new NamedOperation(controller.Name + " / " + state.Name, state.KeyMask));
            }
        }
        catch (Exception ex) { Status = "Controller 配置不可用：" + ex.Message; }
        RaisePropertyChanged(nameof(ControllerSummary));
        RaisePropertyChanged(nameof(HasConfiguredKeys));
        KeyConfirmation = "";
        RaisePropertyChanged(nameof(KeyExpectedConsent));
        RaiseCommands();
    }

    public void SelectNamedOperation(NamedOperation named)
    {
        if (_controller is null) return;
        foreach (var key in Keys) key.Checked = (named.Mask & key.Mask) != 0;
        IsKeyEditorExpanded = true;
        Status = "已载入 " + named.Name + " 的本次硬件组合草稿；不会自动修改工程资源。";
    }

    private static string ExpectedConsent(Func<CanFrame> create)
    {
        try
        {
            var frame = create();
            var usb = CanFrameCodec.ToUsbPayload(frame,
                StationCanWiring.Current);
            return "AUTHORIZE CAN TX " + Convert.ToHexString(usb);
        }
        catch (Exception error) { return "未就绪：" + error.Message; }
    }

    private CanFrame TryBuildCanFrame()
    {
        if (UseDbcMessage)
        {
            var choice = SelectedDbcMessage ??
                throw new InvalidOperationException("工程中尚未选择 DBC 报文。");
            var specification = string.Join(";", DbcSignals.Select(field =>
                field.Signal.Name + "=" + field.Value.Trim()));
            var data = DbcCanMessageEncoder.Encode(choice.Message, specification);
            return CanFrame.Create(BusRole.DutCan, choice.Message.IsExtended,
                choice.Message.ArbitrationId, data);
        }
        var idText = CanIdHex.Trim();
        if (idText.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) idText = idText[2..];
        var id = uint.Parse(idText, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var hex = new string(CanDataHex.Where(c => !char.IsWhiteSpace(c) && c is not '-' and not ',').ToArray());
        var bytes = hex.Length == 0 ? Array.Empty<byte>() : Convert.FromHexString(hex);
        return CanFrame.Create(BusRole.DutCan, Extended, id, bytes);
    }

    private async Task VerifyControllerFreshAsync()
    {
        var project = _projects.Current ?? throw new InvalidOperationException("工程已关闭。");
        if (SelectedProfile is null ||
            _loadedControllerAsset == Guid.Empty || _loadedControllerSha is null ||
            !string.Equals(project.ManifestPath, _loadedProjectManifest,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Controller 配置未加载或所属工程已切换。");
        var asset = project.Definition.Assets.SingleOrDefault(a =>
            a.Id.Value == _loadedControllerAsset &&
            a.Kind == ProjectWorkspaceService.ControllerKind)
            ?? throw new InvalidDataException("原 Controller 配置资源不再存在。");
        if (asset.Revision != _loadedControllerRevision ||
            !string.Equals(asset.Sha256, _loadedControllerSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Controller 配置已修订，请重新加载后再下发 Key。");
        var fresh = await _projects.OpenConfigurationAsync(asset.Id);
        if (!string.Equals(fresh.FileSha256, _loadedControllerSha,
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Controller 配置文件已改变，物理操作已阻止。");
    }

    private CanFrame TryBuildKeyFrame()
    {
        // Every physical command is an explicit, 20-bit KEY_SIM switch mask.
        // Existing project restrictions stay effective; a separate Controller
        // editor is no longer required merely to operate these switches.
        var mask = DraftMask;
        if (mask > 0xFFFFFu)
            throw new InvalidOperationException("KEY 组合必须在 20 位范围内。");
        if (_controller?.KeyHardwarePolicy is { } policy)
        {
            policy.Validate();
            if ((mask & ~policy.SupportedMask) != 0)
                throw new InvalidOperationException("所选 Key 超出工程已确认的硬件可用范围。");
            if (policy.ForbiddenPairs.Any(p => (mask & p.A) == p.A && (mask & p.B) == p.B))
                throw new InvalidOperationException("所选 Key 包含已禁止的组合。");
        }
        return mask == 0 ? KeypadCommandCodec.EncodeReleaseAll() : KeypadCommandCodec.EncodeMask(mask);
    }

    private async Task VerifyDbcFreshAsync()
    {
        if (!UseDbcMessage) return;
        var choice = SelectedDbcMessage ??
            throw new InvalidOperationException("没有选择 DBC 报文。");
        var project = _projects.Current ??
            throw new InvalidOperationException("工程已关闭。");
        var asset = project.Definition.Assets.SingleOrDefault(a =>
            a.Id.Value == choice.AssetId && a.Kind == ProjectWorkspaceService.DbcKind)
            ?? throw new InvalidDataException("DBC 资产不属于当前工程。");
        if (asset.Revision != choice.Revision ||
            !string.Equals(asset.Sha256, choice.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("DBC 已修订，请刷新台架的 DBC 列表。");
        var loaded = await _projects.OpenDbcEditorAsync(asset.Id);
        if (!loaded.Document.Model.Messages.Any(m => m.Id == choice.Message.Id &&
            m.Name == choice.Message.Name))
            throw new InvalidDataException("所选 DBC 报文已被改变，禁止发送。");
    }

    private async Task SendCanAsync() => await Execute("ECU CAN 单次发送", async () =>
    {
        if (!_hardware.IsConnected) throw new IOException("USB 未连接");
        await VerifyDbcFreshAsync();
        var frame = TryBuildCanFrame();
        await _hardware.SendManualBenchCanOnceAsync(frame);
        CanConfirmation = "";
        Append(new BenchLogEntry(DateTime.Now.ToString("HH:mm:ss.fff"),
            "ECU CAN", "TX ACK", "0x" + frame.Id.ToString("X"),
            Convert.ToHexString(frame.Data.AsSpan()), "主控 ACK，不代表 DUT 接收"));
    });

    public async Task ApplyNamedOperationAsync(NamedOperation action)
    {
        SelectNamedOperation(action);
        await ApplyKeysAsync();
    }

    private async Task ApplyKeysAsync() => await Execute("KEY_SIM 电阻组合", async () =>
    {
        if (!_hardware.IsConnected) throw new IOException("USB 未连接");
        if (_loadedControllerAsset != Guid.Empty)
            await VerifyControllerFreshAsync();
        var frame = TryBuildKeyFrame();
        await _hardware.SendCanOnceAsync(frame, ExpectedConsent(() => frame));
        LastKeyAck = "KEY CAN ACK · 请求掩码 0x" + DraftMask.ToString("X5") +
            "（实际五路电压未测量）";
        KeyConfirmation = "";
        Append(new BenchLogEntry(DateTime.Now.ToString("HH:mm:ss.fff"),
            "KEY CAN", "TX ACK", "0x" + frame.Id.ToString("X"),
            Convert.ToHexString(frame.Data.AsSpan()), LastKeyAck));
    });

    private void ReleaseDraftKeys()
    {
        foreach (var key in Keys) key.Checked = false;
        Status = "仅清空编辑勾选；尚未发送 KEY。要真正断开请点击“实际全部断开 KEY”。";
    }

    private async Task ReleaseAllKeysAsync()
    {
        // Explicit release is a real operator action. First cancel all
        // remaining segments, then await the previous command fence cleanup.
        // No hidden release is sent on ordinary Stop.
        if (IsKeyActionRunning || _keyActionTask is { IsCompleted: false })
            await StopKeyActionAsync();
        await Execute("明确全部断开 KEY", async () =>
        {
        if (!_hardware.IsConnected)
            throw new IOException("主控 USB 未连接；未下发全部断开请求。");
        if (IsKeyActionRunning || _hardware.IsTestRunApproved ||
            _hardware.IsManualCanBatchApproved)
            throw new InvalidOperationException(
                "仍有实体操作占用工位，无法交错下发 KEY 全断开。");
        var frame = KeypadCommandCodec.EncodeReleaseAll();
        await _hardware.SendCanOnceAsync(frame, ExpectedConsent(() => frame));
        LastKeyAck = "已明确发送全部断开 KEY，并收到主控 ACK；实际五路电压未测量。";
        Append(new BenchLogEntry(DateTime.Now.ToString("HH:mm:ss.fff"),
            "KEY CAN", "TX ACK", "0x730",
            Convert.ToHexString(frame.Data.AsSpan()), LastKeyAck));
        Status = LastKeyAck;
        });
    }

    public async Task ApplyIgnSelectionAsync(bool on)
    {
        IgnOn = on;
        await ApplyIgnAsync();
    }

    private async Task ApplyIgnAsync() => await Execute("IGN", async () =>
    {
        if (!_hardware.IsConnected)
            throw new IOException("USB 未连接，请先点击顶部“连接”。");
        IgnGpioLabel = "待回读";
        await _hardware.SendManualIgnAsync(IgnOn, IgnExpectedConsent);
        LastIgnAck = "主控 ACK · IGN 请求 " + (IgnOn ? "ON" : "OFF") + "，实际 IGN 电平未测量";
        IgnConfirmation = "";
    });

    private async Task ReadIgnAsync() => await Execute("读取 IGN GPIO", async () =>
    {
        if (!_hardware.IsConnected)
            throw new IOException("USB 未连接，请先点击顶部“连接”。");
        bool gpio = await _hardware.ReadIgnLevelAsync();
        IgnGpioLabel = gpio ? "GPIO 高" : "GPIO 低";
        LastIgnAck = "MCU GPIO 读回 " + (gpio ? "HIGH" : "LOW") +
            "（不是 J1 C10 电压或 ECU IGN 反馈）";
        Append(new BenchLogEntry(DateTime.Now.ToString("HH:mm:ss.fff"),
            "IO IGN", "READ", "0x13", gpio ? "01" : "00", LastIgnAck));
    });

    private async Task ReadClampsAsync() => await Execute("读取两夹具 GPIO", async () =>
    {
        if (!_hardware.IsConnected)
            throw new IOException("USB 未连接，请先点击顶部“连接”。");
        byte levels = await _hardware.ReadClampLevelsAsync();
        ShowClampGpio(levels);
        LastClampStatus = "MCU GPIO · 夹具1=" + ((levels & 1) != 0 ? "HIGH" : "LOW") +
            "，夹具2=" + ((levels & 2) != 0 ? "HIGH" : "LOW") +
            "（不是阀位或实际 J1 电压）";
        Append(new BenchLogEntry(DateTime.Now.ToString("HH:mm:ss.fff"),
            "IO 夹具", "READ", "0x19", levels.ToString("X2"), LastClampStatus));
    });

    public async Task ApplyClampSelectionAsync(byte clamp, bool press)
    {
        if (clamp is not (1 or 2)) throw new ArgumentOutOfRangeException(nameof(clamp));
        ChooseClamp(clamp);
        ChooseClampState(press);
        await ApplyClampAsync();
    }

    private async Task ApplyClampAsync() => await Execute("夹具" + _selectedClamp, async () =>
    {
        byte clamp = _selectedClamp;
        bool press = _clampPress;
        if (!_hardware.IsConnected)
            throw new IOException("USB 未连接，请先点击顶部“连接”。");
        try
        {
            if (clamp == 1) Clamp1GpioLabel = "待回读";
            else Clamp2GpioLabel = "待回读";
            await _hardware.SendManualClampAsync(clamp, press, ClampExpectedConsent);
            ClampConfirmation = "";
            LastClampStatus = "夹具" + clamp + " " + (press ? "压紧" : "释放") +
                " 请求主控 ACK；实际机械到位未测量。";
            Append(new BenchLogEntry(DateTime.Now.ToString("HH:mm:ss.fff"),
                "IO 夹具", "SET ACK", "0x18",
                clamp.ToString("X2") + (press ? "01" : "00"), LastClampStatus));
            try
            {
                byte levels = await _hardware.ReadClampLevelsAsync();
                ShowClampGpio(levels);
                bool gpio = (levels & (1 << (clamp - 1))) != 0;
                LastClampStatus += " GPIO读回 0x" + levels.ToString("X2") +
                    (gpio == press ? "（MCU电平匹配请求）。" : "（MCU电平与请求不一致！）。");
                Append(new BenchLogEntry(DateTime.Now.ToString("HH:mm:ss.fff"),
                    "IO 夹具", "READ", "0x19", levels.ToString("X2"), LastClampStatus));
            }
            catch (Exception readError)
            {
                Clamp1GpioLabel = "未知";
                Clamp2GpioLabel = "未知";
                LastClampStatus += " GPIO读回失败：" + readError.Message +
                    "；实体状态不可推断。";
            }
        }
        catch
        {
            if (clamp == 1) Clamp1GpioLabel = "未知";
            else Clamp2GpioLabel = "未知";
            LastClampStatus = "夹具" + clamp + " 操作失败或未确认；实体状态 UNKNOWN。";
            throw;
        }
    });

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopMonitorDispatch();
        DisposeExternalKeyMonitor();
        _ = StopCanRowsAsync();
        DisposeKeyActions();
        _hardware.FrameObserved -= FrameObserved;
        _hardware.EvidenceRecorded -= EvidenceRecorded;
        _hardware.ConnectionChanged -= ConnectionChanged;
        _hardware.KeyRequestStateChanged -= OnKeyRequestStateChanged;
        _hardware.EcuCanBitrateSelectionChanged -= OnSelectedEcuCanRateChanged;
        _hardware.TestRunAuthorizationChanged -= TestRunAuthorizationChanged;
        _projects.CurrentChanged -= ProjectChanged;
        foreach (var key in Keys) key.PropertyChanged -= KeyChanged;
        foreach (var signal in DbcSignals) signal.PropertyChanged -= DbcSignalChanged;
    }
}
