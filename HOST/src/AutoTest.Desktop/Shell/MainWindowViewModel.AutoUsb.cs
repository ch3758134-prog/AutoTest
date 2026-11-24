using System.IO;
using System.Windows.Threading;
using AutoTest.FlashLab.Programming;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// One USB owner per Windows process. Automatic test and engineering TEST
/// share a continuous host-owned connection; standalone physical tools acquire
/// and release an explicit, exclusive manual-use lease. Scanner COM is separate.
/// </summary>
public sealed partial class MainWindowViewModel
{
    private readonly SemaphoreSlim _usbOwnershipGate = new(1, 1);
    private DispatcherTimer? _automaticUsbTimer;
    private bool _automaticUsbEnabled;
    private string? _exclusiveUsbTool;
    private int _autoUsbFailures;
    private DateTimeOffset _nextAutoUsbAttempt;
    private bool _applicationClosing;

    public string UsbConnectionOwner => _exclusiveUsbTool is null
        ? "AutoTest 主工位（自动连接）"
        : _exclusiveUsbTool + "（工具手动连接）";

    public void StartAutomaticUsbConnection()
    {
        if (_applicationClosing || _automaticUsbEnabled) return;
        _automaticUsbEnabled = true;
        _automaticTestHome.PublishUsbAutoStatus("正在自动发现并连接主控 USB…");
        _automaticUsbTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _automaticUsbTimer.Tick += async (_, _) => await MaintainAutomaticUsbAsync();
        _automaticUsbTimer.Start();
        _ = MaintainAutomaticUsbAsync();
    }

    private async Task MaintainAutomaticUsbAsync()
    {
        if (!_automaticUsbEnabled || _applicationClosing || _exclusiveUsbTool is not null ||
            _hardwareBusy || _openingFlashLab || _realTest.IsRunning ||
            _automaticTestHome.IsOperatorRunning ||
            (_hardware.IsConnected && _hardware.IsEcuCanBitrateApplied) ||
            DateTimeOffset.UtcNow < _nextAutoUsbAttempt)
            return;
        if (!await _usbOwnershipGate.WaitAsync(0)) return;
        try
        {
            if (_exclusiveUsbTool is not null || _hardwareBusy || _applicationClosing ||
                (_hardware.IsConnected && _hardware.IsEcuCanBitrateApplied)) return;
            if (_hardware.HasSession && !_hardware.IsConnected)
                await _hardware.DisconnectAsync();

            // A saved native ECU profile is the authoritative project target.
            // Development and production share the same setting and USB session.
            var project = _automaticTestPlans.Current?.Project ?? _workspace.Current;
            if (project is not null)
            {
                try
                {
                    var native = await NativeFlashProfileStore.LoadAsync(project.RootDirectory);
                    if (native is not null)
                        _hardware.SelectRequestedEcuCanBitrate(native.Profile.EcuCanKbps);
                }
                catch (Exception error) when (error is IOException or InvalidDataException or
                    UnauthorizedAccessException or ArgumentException)
                {
                    StatusMessage = "工程 CAN 目标配置读取失败：" + error.Message;
                }
            }
            if (!_hardware.IsConnected) await _hardware.ConnectAsync();
            // USB PING confirms controller protocol, not ECU bus traffic.
            if (!_hardware.IsEcuCanBitrateApplied)
                await _hardware.ApplyRequestedEcuCanBitrateAsync();
            _autoUsbFailures = 0;
            _nextAutoUsbAttempt = DateTimeOffset.MinValue;
            HardwareStatus = "主控自动连接成功 · " + _hardware.EcuCanBitrateStatus;
            StatusMessage = HardwareStatus;
            _automaticTestHome.PublishUsbAutoStatus(HardwareStatus);
            RaisePropertyChanged(nameof(RequestedEcuCanBitrateKbps));
            RaisePropertyChanged(nameof(EcuCanRateStatus));
        }
        catch (Exception error)
        {
            _autoUsbFailures++;
            var retrySeconds = Math.Min(30, 3 * (1 << Math.Min(_autoUsbFailures, 3)));
            _nextAutoUsbAttempt = DateTimeOffset.UtcNow.AddSeconds(retrySeconds);
            HardwareStatus = "主控自动连接未完成：" + error.Message;
            StatusMessage = HardwareStatus + "；" + retrySeconds + " 秒后再次尝试。";
            _automaticTestHome.PublishUsbAutoStatus(StatusMessage);
        }
        finally
        {
            _usbOwnershipGate.Release();
            RaisePropertyChanged(nameof(UsbConnectionOwner));
            RaisePropertyChanged(nameof(ConnectionButtonText));
            ConnectHardwareCommand.RaiseCanExecuteChanged();
            ApplyEcuCanBitrateCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task<bool> TakeUsbForToolAsync(string tool)
    {
        await _usbOwnershipGate.WaitAsync();
        try
        {
            if (_applicationClosing || _exclusiveUsbTool is not null || _hardwareBusy ||
                _ecuCanBaudApplying)
            {
                StatusMessage = "USB 已由 " + (_exclusiveUsbTool ?? "程序") + " 使用。";
                return false;
            }
            if (_realTest.IsRunning || _hardware.IsTestRunApproved ||
                _hardware.IsManualCanBatchApproved || _hardware.IsManualKeyActionApproved ||
                _hardware.IsFlashSecuritySensitive ||
                _automaticTestHome.IsOperatorRunning)
            {
                StatusMessage = "测试或实体操作正在运行；不能切换主控连接所有权。";
                return false;
            }
            // Reserve ownership before disconnect so the automatic retry never
            // races with a tool opening its own (possibly native WinUSB) handle.
            _exclusiveUsbTool = tool;
            RaisePropertyChanged(nameof(UsbConnectionOwner));
            try
            {
                if (_hardware.HasSession) await _hardware.DisconnectAsync();
            }
            catch
            {
                _exclusiveUsbTool = null;
                RaisePropertyChanged(nameof(UsbConnectionOwner));
                throw;
            }
            HardwareStatus = "USB 已移交 " + tool + "，由该工具手动连接";
            _automaticTestHome.PublishUsbAutoStatus(HardwareStatus);
            return true;
        }
        finally { _usbOwnershipGate.Release(); }
    }

    private async Task ReturnUsbFromToolAsync(string tool)
    {
        await _usbOwnershipGate.WaitAsync();
        try
        {
            if (!string.Equals(_exclusiveUsbTool, tool, StringComparison.Ordinal))
                return;
            try
            {
                if (_hardware.HasSession) await _hardware.DisconnectAsync();
            }
            catch (Exception error)
            {
                HardwareStatus = "工具 " + tool + " 结束，但主控断开失败：" + error.Message;
            }
            finally
            {
                _exclusiveUsbTool = null;
                _nextAutoUsbAttempt = DateTimeOffset.MinValue;
                _automaticTestHome.PublishUsbAutoStatus("工具已释放 USB，主工位正在恢复自动连接…");
                RaisePropertyChanged(nameof(UsbConnectionOwner));
            }
        }
        finally { _usbOwnershipGate.Release(); }
        if (!_applicationClosing) _ = MaintainAutomaticUsbAsync();
    }

    private void StopAutomaticUsbConnection()
    {
        _applicationClosing = true;
        _automaticUsbEnabled = false;
        _automaticUsbTimer?.Stop();
        _automaticUsbTimer = null;
    }
}
