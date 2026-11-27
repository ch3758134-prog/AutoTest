using System.Collections.ObjectModel;
using AutoTest.Devices.Usb;
using Prism.Commands;
using Prism.Mvvm;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// Read-only TestCtr external KEY1/2/3 monitor (PTC7/6/5).
/// Does not write CAN KEY_SIM frames, run TEST, operate the fixture or infer mechanical state.
/// </summary>
public sealed partial class ManualBenchViewModel
{
    public sealed class ExternalKeyInputRow(byte index) : BindableBase
    {
        private byte? _rawLevel;
        private string _stateText = "未知";
        private string _levelText = "—";
        public byte Index { get; } = index;
        public string Name => "KEY" + Index;
        public byte? RawLevel => _rawLevel;
        public string StateText { get => _stateText; private set => SetProperty(ref _stateText, value); }
        public string LevelText { get => _levelText; private set => SetProperty(ref _levelText, value); }

        public void ApplyLevel(byte level, byte? baseline)
        {
            _rawLevel = level;
            LevelText = "电平 " + level;
            StateText = baseline.HasValue
                ? (level == baseline.Value ? "初始状态" : "状态变化")
                : "极性待实测";
        }
        public void Reset()
        {
            _rawLevel = null;
            LevelText = "—";
            StateText = "未知";
        }
    }

    public ObservableCollection<ExternalKeyInputRow> ExternalKeyInputs { get; } = new()
    {
        new ExternalKeyInputRow(1), new ExternalKeyInputRow(2), new ExternalKeyInputRow(3)
    };

    private readonly byte?[] _externalKeyIdleBaseline = new byte?[3];
    private long _externalKeyEpoch;
    private ushort? _externalKeySequence;
    private bool _externalKeyRefreshBusy;
    private int _externalKeyRefreshSerial;
    private string _externalKeyMonitorStatus = "USB 未连接";
    private string _lastExternalKeyEvent = "尚未收到实体 KEY 输入变化";
    public string ExternalKeyMonitorStatus
    {
        get => _externalKeyMonitorStatus;
        private set => SetProperty(ref _externalKeyMonitorStatus, value);
    }
    public string LastExternalKeyEvent
    {
        get => _lastExternalKeyEvent;
        private set => SetProperty(ref _lastExternalKeyEvent, value);
    }
    public DelegateCommand RefreshExternalKeyInputsCommand { get; private set; } = null!;

    private void InitializeExternalKeyMonitor()
    {
        _hardware.ExternalKeyObserved += HandleExternalKeyObserved;
        RefreshExternalKeyInputsCommand = new DelegateCommand(
            async () => await RefreshExternalKeyInputsAsync(),
            () => _hardware.IsConnected && !_externalKeyRefreshBusy);
        ResetExternalKeyMonitor(_hardware.CurrentStationEpoch);
    }

    private void ResetExternalKeyMonitor(long epoch)
    {
        _externalKeyEpoch = epoch;
        _externalKeySequence = null;
        _externalKeyRefreshSerial++; // Invalidate any old-connection async read.
        _externalKeyRefreshBusy = false;
        Array.Fill(_externalKeyIdleBaseline, null);
        foreach (var key in ExternalKeyInputs) key.Reset();
        LastExternalKeyEvent = "尚未收到实体 KEY 输入变化";
        ExternalKeyMonitorStatus = epoch == 0
            ? "USB 未连接 · 输入状态未知"
            : "读取主控外部 KEY1～KEY3 的初始稳定电平…";
        RefreshExternalKeyInputsCommand?.RaiseCanExecuteChanged();
        if (epoch != 0) _ = RefreshExternalKeyInputsAsync();
    }

    private async Task RefreshExternalKeyInputsAsync()
    {
        if (_disposed || _externalKeyRefreshBusy || !_hardware.IsConnected)
            return;
        var epoch = _hardware.CurrentStationEpoch;
        if (epoch == 0) return;
        _externalKeyRefreshBusy = true;
        int requestSerial = ++_externalKeyRefreshSerial;
        RefreshExternalKeyInputsCommand?.RaiseCanExecuteChanged();
        try
        {
            var snapshot = await _hardware.ReadExternalKeyInputsAsync();
            if (_disposed || epoch != _externalKeyEpoch || epoch != _hardware.CurrentStationEpoch)
                return;
            if (_externalKeySequence is ushort last)
            {
                ushort distance = unchecked((ushort)(snapshot.Sequence - last));
                if (distance > 0x8000) return; // Delayed snapshot must never rewind newer IRQ data.
                if (distance > 1)
                    ExternalKeyMonitorStatus = "发现事件计数跳跃；以设备 0x45 当前电平为准。";
            }
            _externalKeySequence = snapshot.Sequence;
            for (var i = 0; i < 3; i++)
            {
                var level = (byte)((snapshot.Levels >> i) & 1);
                _externalKeyIdleBaseline[i] ??= level; // Initial level is a reference, NOT proved released.
                ExternalKeyInputs[i].ApplyLevel(level, _externalKeyIdleBaseline[i]);
            }
            if (!ExternalKeyMonitorStatus.StartsWith("发现事件计数跳跃", StringComparison.Ordinal))
                ExternalKeyMonitorStatus = "真实 GPIO 已读回 · 对照首次电平判断变化；按下极性待实体确认";
        }
        catch (UsbDeviceStatusException error) when (error.Command == MainControllerCodec.GetExternalKeyInputs &&
                                                     error.Status == 0x03)
        {
            if (!_disposed && epoch == _externalKeyEpoch)
                ExternalKeyMonitorStatus = "当前主控固件不支持外部 KEY（0x45）；请先烧录更新固件。";
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (!_disposed && epoch == _externalKeyEpoch)
                ExternalKeyMonitorStatus = "实体 KEY 输入读取失败：" + error.Message;
        }
        finally
        {
            if (requestSerial == _externalKeyRefreshSerial)
            {
                _externalKeyRefreshBusy = false;
                RefreshExternalKeyInputsCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    private void HandleExternalKeyObserved(ExternalKeyInputEvent evt)
    {
        var epoch = _hardware.CurrentStationEpoch;
        var dispatcher = global::System.Windows.Application.Current?.Dispatcher;
        if (epoch == 0 || dispatcher is null) return;
        dispatcher.BeginInvoke(new Action(() =>
        {
            if (_disposed || epoch != _externalKeyEpoch ||
                epoch != _hardware.CurrentStationEpoch) return;
            if (_externalKeySequence is ushort previous)
            {
                ushort delta = unchecked((ushort)(evt.Sequence - previous));
                if (delta == 0 || delta > 0x8000) return;
                if (delta > 1)
                    ExternalKeyMonitorStatus = "检测到实体 KEY 事件序号跳跃，可能有 USB 事件丢失；请点刷新输入。";
            }
            _externalKeySequence = evt.Sequence;
            var row = ExternalKeyInputs[evt.Key - 1];
            var oldLevel = row.RawLevel;
            for (var i = 0; i < 3; i++)
                ExternalKeyInputs[i].ApplyLevel((byte)((evt.Levels >> i) & 1),
                    _externalKeyIdleBaseline[i]);

            string classification = _externalKeyIdleBaseline[evt.Key - 1] is byte baseline
                ? (evt.Level == baseline ? "回到首次电平（疑似松开）" : "偏离首次电平（疑似按下）")
                : "尚无初始基线（按下极性待实测）";
            var clock = evt.ReceivedUtc.ToLocalTime().ToString("HH:mm:ss.fff");
            var edge = (oldLevel.HasValue ? oldLevel.Value.ToString() : "?") +
                       " → " + evt.Level;
            LastExternalKeyEvent = clock + " · 已接收到 KEY" + evt.Key +
                                   " · 电平 " + edge + " · " + classification;
            Append(new BenchLogEntry(clock, "实体 KEY", "INPUT", "KEY" + evt.Key,
                edge, classification + "；固件消抖事件 #" + evt.Sequence));
        }));
    }

    private void DisposeExternalKeyMonitor()
    {
        _hardware.ExternalKeyObserved -= HandleExternalKeyObserved;
    }
}
