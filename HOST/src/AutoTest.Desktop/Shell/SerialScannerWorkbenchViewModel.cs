using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows;
using AutoTest.Contracts;
using AutoTest.Desktop.Projects;
using AutoTest.Specialists.Scan;
using Prism.Commands;
using Prism.Mvvm;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// An owned, non-production USB COM scanner console. It never presses START,
/// selects a recipe, commands a clamp, flashes, or changes TEST Enabled flags.
/// The scanner connection is independent of the main-controller USB session.
/// </summary>
public sealed class SerialScannerWorkbenchViewModel : BindableBase, IAsyncDisposable
{
    private readonly ProjectWorkspaceService _workspace;
    private readonly Action<string> _reportToShell;
    private readonly Func<bool> _mayAcceptScan;
    private readonly Func<string, Task<bool>> _switchRegisteredProject;
    private readonly StationModelCatalog _catalog = new();
    private string? _resolvedManifestPath;
    private string _matchedProjectName = "";
    private string _productionPreview = "";
    private readonly BarcodeInputRouter _router = new();
    private readonly SerialBarcodeCapture _capture;
    private readonly CancellationTokenSource _routerStop = new();
    private readonly Task _drainTask;
    private readonly SemaphoreSlim _portGate = new(1, 1);
    private SerialPortBarcodeLineSource? _source;
    private CancellationTokenSource? _readStop;
    private Task? _readTask;
    private int _handling;
    private bool _disposed;
    private bool _isConnected;
    private string _selectedPort = "";
    private int _baudRate = 9600;
    private string _modelCode = "";
    private string _status = "选择真实 USB 串口并连接";
    private string _lastRaw = "";
    private string _lastHex = "";
    private string _lastModel = "";
    private string _lastSerial = "";
    private string _lastDecision = "";
    private string _connectedPort = "";
    private ScanIdentityPolicy? _activeIdentityPolicy;
    private string _identityFormat = ScanIdentityPolicy.ModelSerial;
    private string _identitySeparator = "-";
    private string _fixedModelCode = "";
    private string _minimumSerialLength = "8";
    private string _maximumSerialLength = "20";
    private string _sampleRaw = "";
    private string _sampleResult = "";
    private string _identityPolicyStatus = "条码解析规则尚未保存；扫码数据不会获得身份匹配资格。";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AutoTest", "scanner-com.json");

    public SerialScannerWorkbenchViewModel(ProjectWorkspaceService workspace,
        Action<string> reportToShell, Func<bool> mayAcceptScan,
        Func<string, Task<bool>> switchRegisteredProject)
    {
        _workspace = workspace;
        _reportToShell = reportToShell;
        _mayAcceptScan = mayAcceptScan;
        _switchRegisteredProject = switchRegisteredProject;
        _router.SetAdmissionGate(() => _mayAcceptScan() && Volatile.Read(ref _handling) == 0);
        _capture = new SerialBarcodeCapture(_router);
        _drainTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var _ in _router.ObserveAsync(
                    new InputPolicy(false, 512, "serial"), _routerStop.Token)) { }
            }
            catch (OperationCanceledException) when (_routerStop.IsCancellationRequested) { }
        });
        RefreshPortsCommand = new DelegateCommand(RefreshPorts);
        ConnectCommand = new DelegateCommand(async () => await ConnectAsync());
        DisconnectCommand = new DelegateCommand(async () => await DisconnectAsync());
        SaveModelCodeCommand = new DelegateCommand(async () => await SaveModelCodeAsync());
        RegisterModelCommand = new DelegateCommand(async () => await RegisterModelAsync());
        SwitchProjectCommand = new DelegateCommand(async () => await SwitchProjectAsync(),
            () => CanSwitchProject);
        PrepareProductionCommand = new DelegateCommand(async () => await PrepareProductionAsync());
        SaveIdentityPolicyCommand = new DelegateCommand(async () => await SaveIdentityPolicyAsync());
        TestIdentitySampleCommand = new DelegateCommand(TestIdentitySample);
        _ = LoadIdentityPolicyAsync();
        _workspace.CurrentChanged += OnProjectChanged;
        OnProjectChanged(this, EventArgs.Empty);
        try
        {
            if (File.Exists(SettingsPath))
            {
                var stored = JsonSerializer.Deserialize<SerialScannerPortSettings>(
                    File.ReadAllText(SettingsPath));
                stored?.Validate();
                if (stored is not null)
                {
                    _selectedPort = stored.PortName;
                    _baudRate = stored.BaudRate;
                }
            }
        }
        catch (Exception error) when (error is IOException or JsonException or ArgumentException or
                                      UnauthorizedAccessException)
        { SetStatus("历史串口设置不可用，请重新选择：" + error.Message); }
        RefreshPorts();
    }

    public sealed record ScanFormatChoice(string Mode, string Label);
    public IReadOnlyList<ScanFormatChoice> IdentityFormats { get; } = new[]
    {
        new ScanFormatChoice(ScanIdentityPolicy.Ut1, "UT1 · 型号/硬件版/SN/CRC"),
        new ScanFormatChoice(ScanIdentityPolicy.ModelSerial, "型号 + 分隔符 + 编号"),
        new ScanFormatChoice(ScanIdentityPolicy.SerialOnly, "纯编号 · 固定单型号工位")
    };
    public string IdentityFormat
    {
        get => _identityFormat;
        set
        {
            if (SetProperty(ref _identityFormat, value))
                RaisePropertyChanged(nameof(IsFixedModelMode));
        }
    }
    public bool IsFixedModelMode => IdentityFormat == ScanIdentityPolicy.SerialOnly;
    public string IdentitySeparator
    {
        get => _identitySeparator;
        set => SetProperty(ref _identitySeparator, value);
    }
    public string FixedModelCode
    {
        get => _fixedModelCode;
        set => SetProperty(ref _fixedModelCode, value);
    }
    public string MinimumSerialLength
    {
        get => _minimumSerialLength;
        set => SetProperty(ref _minimumSerialLength, value);
    }
    public string MaximumSerialLength
    {
        get => _maximumSerialLength;
        set => SetProperty(ref _maximumSerialLength, value);
    }
    public string SampleRaw
    {
        get => _sampleRaw;
        set => SetProperty(ref _sampleRaw, value);
    }
    public string SampleResult
    {
        get => _sampleResult;
        private set => SetProperty(ref _sampleResult, value);
    }
    public string IdentityPolicyStatus
    {
        get => _identityPolicyStatus;
        private set => SetProperty(ref _identityPolicyStatus, value);
    }

    public ObservableCollection<string> AvailablePorts { get; } = new();
    public IReadOnlyList<int> SupportedBaudRates { get; } =
        new[] { 1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200 };
    public string SelectedPort
    {
        get => _selectedPort;
        set => SetProperty(ref _selectedPort, value);
    }
    public int BaudRate
    {
        get => _baudRate;
        set => SetProperty(ref _baudRate, value);
    }
    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (SetProperty(ref _isConnected, value))
                RaisePropertyChanged(nameof(CanEditConnection));
        }
    }
    public bool CanEditConnection => !IsConnected;
    public string ConnectedPort
    {
        get => _connectedPort;
        private set => SetProperty(ref _connectedPort, value);
    }
    public string ModelCode
    {
        get => _modelCode;
        set => SetProperty(ref _modelCode, value);
    }
    public string Status
    {
        get => _status;
        private set
        {
            SetProperty(ref _status, value);
            _reportToShell(value);
        }
    }
    public string LastRaw
    {
        get => _lastRaw;
        private set => SetProperty(ref _lastRaw, value);
    }
    public string LastHex
    {
        get => _lastHex;
        private set => SetProperty(ref _lastHex, value);
    }
    public string LastModel
    {
        get => _lastModel;
        private set => SetProperty(ref _lastModel, value);
    }
    public string LastSerial
    {
        get => _lastSerial;
        private set => SetProperty(ref _lastSerial, value);
    }
    public string LastDecision
    {
        get => _lastDecision;
        private set => SetProperty(ref _lastDecision, value);
    }
    public string ProjectName => _workspace.Current?.Definition.Name ?? "未打开工程";
    public string MatchedProjectName
    {
        get => _matchedProjectName;
        private set => SetProperty(ref _matchedProjectName, value);
    }
    public string ProductionPreview
    {
        get => _productionPreview;
        private set => SetProperty(ref _productionPreview, value);
    }
    public bool CanSwitchProject =>
        !string.IsNullOrWhiteSpace(_resolvedManifestPath) &&
        !string.Equals(_resolvedManifestPath, _workspace.Current?.ManifestPath,
            StringComparison.OrdinalIgnoreCase);


    public DelegateCommand RefreshPortsCommand { get; }
    public DelegateCommand ConnectCommand { get; }
    public DelegateCommand DisconnectCommand { get; }
    public DelegateCommand SaveModelCodeCommand { get; }
    public DelegateCommand RegisterModelCommand { get; }
    public DelegateCommand SwitchProjectCommand { get; }
    public DelegateCommand PrepareProductionCommand { get; }
    public DelegateCommand SaveIdentityPolicyCommand { get; }
    public DelegateCommand TestIdentitySampleCommand { get; }

    private ScanIdentityPolicy DraftIdentityPolicy()
    {
        if (!int.TryParse(MinimumSerialLength, out var minimum) ||
            !int.TryParse(MaximumSerialLength, out var maximum))
            throw new FormatException("请输入合法的最小/最大编号长度。");
        var policy = new ScanIdentityPolicy(ScanIdentityPolicy.CurrentSchema,
            IdentityFormat, IdentitySeparator, string.IsNullOrEmpty(FixedModelCode)
                ? null : FixedModelCode, minimum, maximum);
        policy.Validate();
        return policy;
    }

    private async Task LoadIdentityPolicyAsync()
    {
        try
        {
            var loaded = await StationScanIdentityPolicyStore.LoadAsync();
            if (loaded is null) return;
            OnUi(() =>
            {
                _activeIdentityPolicy = loaded;
                IdentityFormat = loaded.Format;
                IdentitySeparator = loaded.Separator;
                FixedModelCode = loaded.FixedModelCode ?? "";
                MinimumSerialLength = loaded.MinimumSerialLength.ToString();
                MaximumSerialLength = loaded.MaximumSerialLength.ToString();
                IdentityPolicyStatus = "工位扫码格式已保存：" + loaded.Format +
                    "。扫码只做身份识别，不执行夹具、START 或烧录。";
            });
        }
        catch (Exception error) when (error is IOException or FormatException or
                                       InvalidDataException or JsonException or UnauthorizedAccessException)
        {
            OnUi(() =>
            {
                _activeIdentityPolicy = null;
                IdentityPolicyStatus = "原有扫码配置无法读取，拒绝沿用：" + error.Message;
            });
        }
    }

    private async Task SaveIdentityPolicyAsync()
    {
        if (!_mayAcceptScan() || IsConnected || Volatile.Read(ref _handling) != 0)
        {
            IdentityPolicyStatus = "请先断开扫码枪并停止正在进行的任务，再更改条码解析规则。";
            return;
        }
        try
        {
            var candidate = DraftIdentityPolicy();
            if (candidate.Format == ScanIdentityPolicy.SerialOnly)
            {
                var binding = await _catalog.ResolveAsync(candidate.FixedModelCode!);
                if (!binding.Matched)
                    throw new InvalidDataException(
                        "纯编号模式绑定的型号尚未在工位登记唯一工程：" + binding.Explanation);
            }
            await StationScanIdentityPolicyStore.SaveAsync(candidate);
            _activeIdentityPolicy = candidate;
            IdentityPolicyStatus = "已保存工位扫码格式：" + candidate.Format +
                "。配置变更后需重新扫码；旧识别结果已失效。";
            InvalidateScanIdentity();
        }
        catch (Exception error) when (error is IOException or FormatException or
                                       InvalidDataException or ArgumentException or
                                       UnauthorizedAccessException or JsonException)
        { IdentityPolicyStatus = "条码格式未保存：" + error.Message; }
    }

    private void TestIdentitySample()
    {
        try
        {
            var draft = DraftIdentityPolicy();
            var outcome = draft.Decode(SampleRaw);
            SampleResult = outcome.Parsed
                ? "解析成功｜型号 " + outcome.ModelCode + "｜编号 " + outcome.Serial +
                  (outcome.HardwareCode is null ? "" : "｜硬件版 " + outcome.HardwareCode) +
                  "。这是离线样例检查，不是实物扫描、不能启动设备。"
                : "拒绝 " + outcome.Code + "｜" + outcome.Explanation;
        }
        catch (FormatException error)
        { SampleResult = "配置不合法：" + error.Message; }
    }

    private void InvalidateScanIdentity()
    {
        LastModel = "";
        LastSerial = "";
        LastDecision = "SCAN_CONFIG_CHANGED";
        ProductionPreview = "";
        _resolvedManifestPath = null;
        MatchedProjectName = "";
        SwitchProjectCommand.RaiseCanExecuteChanged();
    }

    private void OnProjectChanged(object? sender, EventArgs e) => OnUi(() =>
    {
        ModelCode = _workspace.Current?.Definition.EcuModelCode ?? "";
        RaisePropertyChanged(nameof(ProjectName));
        // A project switch or version update invalidates a previously scanned
        // identity; old labels may not authorize a different ECU model.
        InvalidateScanIdentity();
    });

    private static void OnUi(Action action)
    {
        var dispatcher = global::System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }

    private void SetStatus(string text) => OnUi(() => Status = text);

    private void RefreshPorts()
    {
        try
        {
            var ports = SerialPortBarcodeLineSource.GetAvailablePortNames();
            AvailablePorts.Clear();
            foreach (var port in ports) AvailablePorts.Add(port);
            if (string.IsNullOrWhiteSpace(SelectedPort) && ports.Length > 0)
                SelectedPort = ports[0];
            if (ports.Length == 0) SetStatus("未发现 COM 端口；请检查扫码枪 USB 串口驱动");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                                      InvalidOperationException or System.ComponentModel.Win32Exception)
        { SetStatus("枚举串口失败：" + error.Message); }
    }

    private async Task ConnectAsync()
    {
        await _portGate.WaitAsync();
        try
        {
            if (_disposed) return;
            if (_source is not null) await StopPortInsideGateAsync();
            var settings = new SerialScannerPortSettings(SelectedPort, BaudRate);
            settings.Validate();
            _source = SerialPortBarcodeLineSource.Open(settings);
            if (!await _capture.OpenAsync(_source, settings.PortName,
                    CancellationToken.None))
                throw new IOException("未确认串口设备身份。");
            _readStop = new CancellationTokenSource();
            ConnectedPort = settings.PortName;
            IsConnected = true;
            var readerToken = _readStop.Token;
            _readTask = Task.Run(() => ReadLoopAsync(readerToken));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                var tmp = SettingsPath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOptions));
                File.Move(tmp, SettingsPath, true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { SetStatus("COM 已连接，但端口偏好未保存：" + error.Message); }
            SetStatus("USB 串口 " + settings.PortName + " 已打开，等待完整 CR/LF 条码");
        }
        catch (Exception error) when (error is ArgumentException or IOException or
                                      InvalidOperationException or UnauthorizedAccessException or
                                      System.ComponentModel.Win32Exception)
        {
            SetStatus("连接扫码枪失败：" + error.Message);
            if (_source is not null) await StopPortInsideGateAsync();
        }
        finally { _portGate.Release(); }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            ScanIngressResult result;
            try { result = await _capture.ReadAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (InvalidDataException error)
            {
                OnUi(() => LastHex = _source?.LastFrameHex ?? "");
                SetStatus(error.Message + "；已丢弃该帧");
                continue;
            }
            catch (Exception error) when (error is IOException or InvalidOperationException or
                                          UnauthorizedAccessException or ObjectDisposedException)
            {
                if (!ct.IsCancellationRequested) SetStatus("扫码枪通信中断：" + error.Message);
                break;
            }

            if (!result.Accepted || result.Event is null)
            {
                SetStatus("扫码未接受：" + result.Code);
                continue;
            }
            if (Interlocked.Exchange(ref _handling, 1) != 0)
            {
                SetStatus("上一条扫码仍在预检，拒绝本条输入");
                continue;
            }
            var captured = result.Event;
            var capturedHex = _source?.LastFrameHex ?? "";
            // Cross-project lookup is read-only, from an explicitly registered
            // model index. It does not switch the live project on scan arrival.
            StationModelCatalog.ResolveResult? station = null;
            var policy = _activeIdentityPolicy;
            ScanIdentityResult? identity = policy?.Decode(captured.Scan.Text);
            try
            {
                if (identity?.Parsed == true && identity.ModelCode is { } code)
                    station = await _catalog.ResolveAsync(code, ct);
            }
            catch (Exception error) when (error is IOException or InvalidDataException or
                                          UnauthorizedAccessException or JsonException)
            { SetStatus("工位型号索引不可用：" + error.Message); }
            var resolvedStation = station;
            OnUi(() =>
            {
                try
                {
                    LastRaw = captured.Scan.Text;
                    LastHex = capturedHex;
                    // A real raw sample is useful even before the station has
                    // a published decoder. Do not infer a format from it.
                    if (string.IsNullOrWhiteSpace(SampleRaw))
                        SampleRaw = captured.Scan.Text;
                    var preview = policy is null
                        ? new ProductionBarcodePreflightResult(
                            "SCAN_POLICY_NOT_CONFIGURED", null, null, null,
                            "未保存工位条码格式；请先选择格式并用样例验证，扫码不会自动匹配工程。")
                        : ProductionBarcodePreflight.Inspect(
                            captured.Scan.Text, _workspace.Current?.Definition, policy);
                    var sameIdentityAsPrevious = !string.IsNullOrEmpty(preview.DutSerial) &&
                        string.Equals(LastModel, preview.ModelCode, StringComparison.Ordinal) &&
                        string.Equals(LastSerial, preview.DutSerial, StringComparison.Ordinal);
                    LastModel = preview.ModelCode ?? "";
                    LastSerial = preview.DutSerial ?? "";
                    LastDecision = sameIdentityAsPrevious
                        ? "SCAN_REPEAT_IDENTITY" : preview.Code;
                    ProductionPreview = "";
                    _resolvedManifestPath = identity?.Parsed == true &&
                        resolvedStation?.Matched == true
                        ? resolvedStation.Entry?.ManifestPath : null;
                    MatchedProjectName = resolvedStation?.Matched == true
                        ? resolvedStation.Definition?.Name ?? "" : "";
                    SwitchProjectCommand.RaiseCanExecuteChanged();
                    var repeatNote = sameIdentityAsPrevious
                        ? "重复识别同一编号；仅更新本次扫码观察，不算新增 DUT，也不会启动复检。"
                        : "";
                    Status = repeatNote + (resolvedStation?.Matched == true
                        ? preview.Explanation + " · 已登记工程：" + MatchedProjectName +
                            "（不自动切换、夹紧或启动）"
                        : preview.Explanation + (resolvedStation is null ? "" :
                            " · " + resolvedStation.Explanation));
                }
                finally { Interlocked.Exchange(ref _handling, 0); }
            });
        }
        OnUi(() => IsConnected = false);
    }

    private async Task SaveModelCodeAsync()
    {
        if (!_mayAcceptScan() || Volatile.Read(ref _handling) != 0)
        {
            SetStatus("当前工位正在运行或处理扫描，不能修改工程型号。");
            return;
        }
        try
        {
            await _workspace.SetEcuModelCodeAsync(ModelCode);
            SetStatus("工程型号代码已保存：" + ModelCode);
        }
        catch (Exception error) when (error is ArgumentException or IOException or
                                      InvalidOperationException or UnauthorizedAccessException or
                                      AutoTest.Projects.Project.ProjectDataException)
        { SetStatus("工程型号未保存：" + error.Message); }
    }

    private async Task RegisterModelAsync()
    {
        var current = _workspace.Current;
        if (current is null || !_mayAcceptScan())
        {
            SetStatus("请先打开并配置当前工程，确保工位空闲。");
            return;
        }
        try
        {
            await _catalog.RegisterAsync(current);
            SetStatus("当前 ECU 型号已明确登记到本工位：" + current.Definition.EcuModelCode);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or
                                      ArgumentException or UnauthorizedAccessException)
        { SetStatus("型号登记失败：" + error.Message); }
    }

    private async Task SwitchProjectAsync()
    {
        var path = _resolvedManifestPath;
        if (string.IsNullOrWhiteSpace(path) || !_mayAcceptScan())
        {
            SetStatus("扫码型号未匹配可切换工程，或工位仍在运行。");
            return;
        }
        var confirm = MessageBox.Show(
            "将切换至扫码型号 " + LastModel + " 的工程：" +
            "\\n" + MatchedProjectName +
            "\\n\\n将保存当前编辑内容，检查设备状态后切换。不启动 TEST 或烧录。继续？",
            "切换 ECU 工程", MessageBoxButton.YesNo,
            MessageBoxImage.Question, MessageBoxResult.No);
        if (confirm != MessageBoxResult.Yes) return;
        var resolved = await _catalog.ResolveAsync(LastModel);
        if (!resolved.Matched || !string.Equals(path, resolved.Entry?.ManifestPath,
            StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("型号映射已变化，拒绝使用旧扫码结果。");
            return;
        }
        if (await _switchRegisteredProject(path))
            SetStatus("工程已切换：" + MatchedProjectName +
                "；请在生产预检中重新确认当前资源。");
    }

    private async Task PrepareProductionAsync()
    {
        var current = _workspace.Current;
        if (!_mayAcceptScan() || _activeIdentityPolicy is null ||
            current is null || string.IsNullOrEmpty(LastSerial) ||
            !string.Equals(current.Definition.EcuModelCode, LastModel, StringComparison.Ordinal))
        {
            SetStatus("尚未打开扫码型号对应工程，或当前运行状态不允许预检。");
            return;
        }
        var snapshotModel = LastModel;
        var snapshotSerial = LastSerial;
        var resolved = await _catalog.ResolveAsync(snapshotModel);
        if (!resolved.Matched || !string.Equals(resolved.Entry?.ManifestPath,
            current.ManifestPath, StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("生产准备拒绝：扫码型号尚未匹配当前工位唯一登记的工程。");
            ProductionPreview = "需在当前工程登记型号，并重新扫码。";
            return;
        }
        var result = await ProductionPreparationService.PrepareAsync(
            current, snapshotModel, snapshotSerial);
        ProductionPreview = result.ConfigurationReady
            ? "本次配置预检完成｜生产前置必烧录｜" +
              result.Preparation!.OrderedEnabledCases.Count + " 个 Enabled 用例｜" +
              result.Preparation.EcuCanKbps + " kbit/s｜" +
              "原生 Job " + result.Preparation.JobFingerprint[..12] +
              "…；等待真实夹具及实体 START，尚未执行"
            : result.Explanation;
        SetStatus(result.Explanation);
    }

    private async Task DisconnectAsync()
    {
        await _portGate.WaitAsync();
        try
        {
            await StopPortInsideGateAsync();
            SetStatus("USB 扫码枪已断开");
        }
        finally { _portGate.Release(); }
    }

    private async Task StopPortInsideGateAsync()
    {
        _readStop?.Cancel();
        if (_readTask is not null)
        {
            try { await _readTask.WaitAsync(TimeSpan.FromSeconds(3)); }
            catch (OperationCanceledException) { }
            catch (TimeoutException)
            {
                SetStatus("串口读取停止超时，设备需要人工检查。");
            }
        }
        if (_source is not null) await _source.DisposeAsync();
        _source = null;
        _readStop?.Dispose();
        _readStop = null;
        _readTask = null;
        ConnectedPort = "";
        IsConnected = false;
    }

    public async ValueTask DisposeAsync()
    {
        await _portGate.WaitAsync();
        try
        {
            if (_disposed) return;
            _disposed = true;
            _workspace.CurrentChanged -= OnProjectChanged;
            await StopPortInsideGateAsync();
            _routerStop.Cancel();
            try { await _drainTask; }
            catch (OperationCanceledException) { }
            _routerStop.Dispose();
        }
        finally { _portGate.Release(); }
    }
}
