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
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AutoTest", "scanner-com.json");

    public SerialScannerWorkbenchViewModel(ProjectWorkspaceService workspace,
        Action<string> reportToShell, Func<bool> mayAcceptScan)
    {
        _workspace = workspace;
        _reportToShell = reportToShell;
        _mayAcceptScan = mayAcceptScan;
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

    public DelegateCommand RefreshPortsCommand { get; }
    public DelegateCommand ConnectCommand { get; }
    public DelegateCommand DisconnectCommand { get; }
    public DelegateCommand SaveModelCodeCommand { get; }

    private void OnProjectChanged(object? sender, EventArgs e) => OnUi(() =>
    {
        ModelCode = _workspace.Current?.Definition.EcuModelCode ?? "";
        RaisePropertyChanged(nameof(ProjectName));
        LastDecision = "";
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
            OnUi(() =>
            {
                try
                {
                    LastRaw = captured.Scan.Text;
                    LastHex = capturedHex;
                    var preview = ProductionBarcodePreflight.Inspect(
                        captured.Scan.Text, _workspace.Current?.Definition);
                    LastModel = preview.ModelCode ?? "";
                    LastSerial = preview.DutSerial ?? "";
                    LastDecision = preview.Code;
                    Status = preview.Explanation;
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
