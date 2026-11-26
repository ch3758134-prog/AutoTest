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
/// Real USB virtual-COM barcode device only. Raw capture has no dependency on
/// ECU projects, model catalogs, FLASH, or published production rules.
/// The authoritative station interpreter is elsewhere and is run only after
/// receiving an accepted physical barcode observation.
/// </summary>
public sealed class SerialScannerWorkbenchViewModel : BindableBase, IAsyncDisposable
{
    private readonly Action<string> _reportToShell;
    private readonly Func<bool> _mayAcceptScan;
    private readonly BarcodeInputRouter _router = new();
    private readonly SerialBarcodeCapture _capture;
    private readonly CancellationTokenSource _routerStop = new();
    private readonly Task _drainTask;
    private readonly SemaphoreSlim _portGate = new(1, 1);
    private readonly System.Windows.Threading.DispatcherTimer _deviceWatch =
        new() { Interval = TimeSpan.FromSeconds(4) };
    private SerialPortBarcodeLineSource? _source;
    private CancellationTokenSource? _readStop;
    private Task? _readTask;
    private StationScannerDeviceBinding.Saved? _boundDevice;
    private int _handling, _autoConnecting;
    private bool _disposed;
    private bool _isConnected;
    private string _selectedPort = "";
    private string _connectedPort = "";
    private int _baudRate = 9600;
    private string _lastRaw = "";
    private string _lastHex = "";
    private string _status = "正在自动发现扫码设备";
    private string _deviceStatus = "自动寻找唯一 USB 串口扫码设备";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AutoTest", "scanner-serial-v2.json");

    public SerialScannerWorkbenchViewModel(Action<string> reportToShell,
        Func<bool> mayAcceptScan)
    {
        _reportToShell = reportToShell;
        _mayAcceptScan = mayAcceptScan;
        _router.SetAdmissionGate(() =>
            _mayAcceptScan() && Volatile.Read(ref _handling) == 0);
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
        BindScannerDeviceCommand = new DelegateCommand(BindDevice);
        ReplaceScannerDeviceCommand = new DelegateCommand(UnbindDevice);
        try
        {
            if (File.Exists(SettingsPath))
            {
                var settings = JsonSerializer.Deserialize<SerialScannerPortSettings>(
                    File.ReadAllText(SettingsPath), Json);
                settings?.Validate();
                if (settings is not null)
                {
                    _selectedPort = settings.PortName;
                    _baudRate = settings.BaudRate;
                }
            }
            _boundDevice = StationScannerDeviceBinding.Load();
        }
        catch (Exception error) when (error is IOException or
            JsonException or ArgumentException or UnauthorizedAccessException)
        {
            DeviceStatus = "扫码枪设备设置需要重新确认：" + error.Message;
        }
        RefreshPorts();
        _deviceWatch.Tick += async (_, _) =>
        {
            if (!_disposed && !IsConnected) await TryAutoConnectAsync();
        };
        _deviceWatch.Start();
        _ = TryAutoConnectAsync();
    }

    // Captured frames, including rejected frames, are observations only.
    public event Action<string>? RawBarcodeObserved;
    public event Action<string>? IncompleteBarcodeObserved;
    // This event alone does not grant a DUT identity, production START or PASS.
    public event Action<string>? CompleteBarcodeReceived;

    public ObservableCollection<string> AvailablePorts { get; } = new();
    public IReadOnlyList<int> SupportedBaudRates { get; } =
        [9600, 19200, 38400, 57600, 115200];
    public DelegateCommand RefreshPortsCommand { get; }
    public DelegateCommand ConnectCommand { get; }
    public DelegateCommand DisconnectCommand { get; }
    public DelegateCommand BindScannerDeviceCommand { get; }
    public DelegateCommand ReplaceScannerDeviceCommand { get; }

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
    public string DeviceStatus
    {
        get => _deviceStatus;
        private set => SetProperty(ref _deviceStatus, value);
    }
    public string Status
    {
        get => _status;
        private set
        {
            if (SetProperty(ref _status, value))
                _reportToShell(value);
        }
    }

    private static void OnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }
    private void Say(string message) => OnUi(() => Status = message);

    public void RefreshPorts()
    {
        try
        {
            var ports = SerialPortBarcodeLineSource.GetAvailablePortNames();
            AvailablePorts.Clear();
            foreach (var port in ports) AvailablePorts.Add(port);
            if (_boundDevice is not null)
            {
                var current = StationScannerDeviceBinding.Match(_boundDevice);
                if (current is not null)
                {
                    SelectedPort = current.PortName;
                    DeviceStatus = "已识别绑定的 USB 扫码枪：" + current.PortName;
                }
                else DeviceStatus = "已绑定扫码枪未连接；不会猜测其它 COM。";
            }
            else DeviceStatus = ports.Length == 0
                ? "没有可用串口，请检查扫码枪或驱动。"
                : "尚未选择本工位扫码枪；连接并扫码后可保存设备。";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or InvalidOperationException or System.ComponentModel.Win32Exception
            or System.Security.SecurityException)
        { Say("扫码设备枚举失败：" + error.Message); }
    }

    private void BindDevice()
    {
        if (!IsConnected || string.IsNullOrWhiteSpace(LastRaw))
        {
            DeviceStatus = "先连接扫码枪并扫码一次，再确认实体设备。";
            return;
        }
        try
        {
            _boundDevice = StationScannerDeviceBinding.Bind(ConnectedPort);
            DeviceStatus = "已绑定本台 USB 扫码枪，重插后自动识别实际 COM。";
        }
        catch (Exception error) when (error is IOException or InvalidDataException or
            UnauthorizedAccessException or ArgumentException or
            System.ComponentModel.Win32Exception or System.Security.SecurityException)
        { DeviceStatus = "绑定失败：" + error.Message; }
    }

    private void UnbindDevice()
    {
        if (IsConnected)
        {
            DeviceStatus = "更换扫码枪前请先断开当前设备。";
            return;
        }
        try
        {
            StationScannerDeviceBinding.Unbind();
            _boundDevice = null;
            SelectedPort = "";
            DeviceStatus = "已解除实体绑定；首次扫码将重新确认设备。";
            RefreshPorts();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { DeviceStatus = "无法解除绑定：" + error.Message; }
    }

    public async Task RetryAutoConnectAsync()
    {
        if (_disposed) return;
        if (IsConnected) { DeviceStatus = "扫码枪已连接：" + ConnectedPort; return; }
        RefreshPorts();
        await TryAutoConnectAsync();
    }

    private async Task TryAutoConnectAsync()
    {
        if (Interlocked.Exchange(ref _autoConnecting, 1) != 0) return;
        try
        {
            await Task.Yield();
            if (_disposed || IsConnected) return;
            if (_boundDevice is null)
            {
                // Never seize the main controller or open every COM.
                var candidates = StationScannerDeviceBinding.DetectUsbSerialDevices();
                if (candidates.Count != 1)
                {
                    DeviceStatus = candidates.Count == 0
                        ? "没有发现 USB 串口扫码设备。"
                        : "发现多个 USB 串口设备，首次需在设备窗口确认实际扫码枪。";
                    return;
                }
                SelectedPort = candidates[0].PortName;
                await ConnectAsync();
                return;
            }
            var matched = StationScannerDeviceBinding.Match(_boundDevice);
            if (matched is null)
            {
                DeviceStatus = "绑定的 USB 扫码枪未连接。";
                return;
            }
            SelectedPort = matched.PortName;
            await ConnectAsync();
        }
        catch (Exception error) when (error is IOException or InvalidDataException
            or UnauthorizedAccessException or ArgumentException or
            System.Security.SecurityException)
        { DeviceStatus = "自动连接失败：" + error.Message; }
        finally { Interlocked.Exchange(ref _autoConnecting, 0); }
    }

    private async Task ConnectAsync()
    {
        await _portGate.WaitAsync();
        try
        {
            if (_disposed) return;
            if (_source is not null) await StopInsideGateAsync();
            if (_boundDevice is not null)
            {
                var matched = StationScannerDeviceBinding.Match(_boundDevice)
                    ?? throw new IOException("已绑定扫码枪未找到，拒绝使用其它 COM。");
                if (!string.Equals(matched.PortName, SelectedPort,
                        StringComparison.OrdinalIgnoreCase))
                    throw new IOException("选择的 COM 与已绑定实体设备不一致。");
            }
            var settings = new SerialScannerPortSettings(SelectedPort, BaudRate);
            settings.Validate();
            _source = SerialPortBarcodeLineSource.Open(settings);
            _source.IncompleteFrameObserved += OnPartialFrame;
            if (!await _capture.OpenAsync(_source, settings.PortName, CancellationToken.None))
                throw new IOException("串口源未通过设备确认。");
            _readStop = new CancellationTokenSource();
            ConnectedPort = settings.PortName;
            IsConnected = true;
            _readTask = Task.Run(() => ReadLoopAsync(_readStop.Token));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                var temp = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(settings, Json));
                File.Move(temp, SettingsPath, overwrite: true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { Say("扫码已连接；保存端口偏好失败：" + error.Message); }
            Say("已连接 " + settings.PortName + "，等待扫码。");
        }
        catch (Exception error) when (error is ArgumentException or IOException or
            InvalidOperationException or UnauthorizedAccessException or
            System.ComponentModel.Win32Exception)
        {
            Say("连接失败：" + error.Message);
            if (_source is not null) await StopInsideGateAsync();
        }
        finally { _portGate.Release(); }
    }

    private void OnPartialFrame(string preview, string hex) => OnUi(() =>
    {
        if (!IsConnected) return;
        LastRaw = preview;
        LastHex = hex;
        Status = "收到串口数据，但尚无 CR/LF 结束符；原文仅供诊断。";
        IncompleteBarcodeObserved?.Invoke(preview);
    });

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            ScanIngressResult result;
            try { result = await _capture.ReadAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (InvalidDataException error)
            {
                var invalidRaw = _source?.LastFrameText ?? "";
                var invalidHex = _source?.LastFrameHex ?? "";
                OnUi(() =>
                {
                    LastHex = invalidHex;
                    if (invalidRaw.Length > 0)
                    {
                        LastRaw = invalidRaw;
                        try { RawBarcodeObserved?.Invoke(invalidRaw); } catch { }
                    }
                    Status = "已显示被串口解析拒绝的原文：" + error.Message;
                });
                continue;
            }
            catch (Exception error) when (error is IOException or InvalidOperationException or
                UnauthorizedAccessException or ObjectDisposedException)
            {
                if (!ct.IsCancellationRequested) Say("扫码设备连接中断：" + error.Message);
                break;
            }

            // Observe physical serial text BEFORE the station's admission gate.
            var raw = _capture.LastObservedRaw;
            var hex = _source?.LastFrameHex ?? "";
            if (raw is not null)
                OnUi(() =>
                {
                    LastRaw = raw;
                    LastHex = hex;
                    try { RawBarcodeObserved?.Invoke(raw); } catch { }
                });
            if (!result.Accepted || result.Event is null)
            {
                Say("扫码原文已接收，但本次未被生产入口接受：" + result.Code);
                continue;
            }
            if (Interlocked.Exchange(ref _handling, 1) != 0)
            {
                Say("正在核对前一条扫码；本次原文已显示，不启动新检测。");
                continue;
            }
            var complete = result.Event.Scan.Text;
            OnUi(() =>
            {
                try
                {
                    LastRaw = complete;
                    LastHex = hex;
                    if (_boundDevice is null && !_disposed)
                        BindDevice();
                    Status = "已收到扫码：" + complete;
                    // Only the station's authoritative interpreter may decide
                    // which frozen ProjectId the operator is permitted to use.
                    try { CompleteBarcodeReceived?.Invoke(complete); } catch { }
                }
                finally { Interlocked.Exchange(ref _handling, 0); }
            });
        }
        OnUi(() => IsConnected = false);
    }

    private async Task DisconnectAsync()
    {
        await _portGate.WaitAsync();
        try
        {
            await StopInsideGateAsync();
            Say("扫码设备已断开。");
        }
        finally { _portGate.Release(); }
    }

    private async Task StopInsideGateAsync()
    {
        _readStop?.Cancel();
        if (_readTask is not null)
        {
            try { await _readTask.WaitAsync(TimeSpan.FromSeconds(3)); }
            catch (OperationCanceledException) { }
            catch (TimeoutException) { Say("扫码串口停止超时，需检查设备。"); }
        }
        if (_source is not null)
        {
            _source.IncompleteFrameObserved -= OnPartialFrame;
            await _source.DisposeAsync();
        }
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
            _deviceWatch.Stop();
            await StopInsideGateAsync();
            _routerStop.Cancel();
            try { await _drainTask; }
            catch (OperationCanceledException) { }
            _routerStop.Dispose();
        }
        finally { _portGate.Release(); }
    }
}
