using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Threading;
using AutoTest.Contracts;
using AutoTest.Desktop.Diagnostics;
using AutoTest.Devices.Can;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// View-only collection. A batch emits one WPF Reset, not a notification per
/// physical frame. This is never the TEST, FLASH or RECORD evidence store.
/// </summary>
public sealed class MonitorDisplayCollection<T> : ObservableCollection<T>
{
    public void AppendBatch(IReadOnlyList<T> rows, int limit)
    {
        if (rows.Count == 0) return;
        foreach (var row in rows) Items.Add(row);
        while (Items.Count > limit) Items.RemoveAt(0);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Reset));
    }
}

public sealed partial class MainWindowViewModel
{
    // Display pressure NEVER changes USB reception, TEST sampling, CAN analysis,
    // physical frame-loss facts, or durable evidence/RECORD authorization.
    private const int PendingCanLimit = 768;
    private const int PendingDecodedLimit = 384;
    private const int CanPerTick = 48;
    private const int DecodedPerTick = 24;
    private readonly object _monitorDisplayGate = new();
    private readonly Queue<CanMonitorRow> _pendingCanUi = new();
    private readonly Queue<DecodedCanRow> _pendingDecodedUi = new();
    private DispatcherTimer? _canUiTimer;
    private bool _canUiStopped;
    private long _displaySkippedCan;
    private long _displaySkippedDecoded;
    private DateTimeOffset _lastMonitorStatusUpdate;
    private string _canMonitorDisplayStatus = "RX / TX-ACK · 显示最多每秒刷新 8 次";

    public string CanMonitorDisplayStatus
    {
        get => _canMonitorDisplayStatus;
        private set => SetProperty(ref _canMonitorDisplayStatus, value);
    }

    private void InitializeCanMonitorUi()
    {
        _hardware.FrameObserved += observation =>
        {
            var frame = observation.Frame;
            QueueCanUi(new CanMonitorRow(
                observation.Time.Utc.ToLocalTime().ToString("HH:mm:ss.fff"),
                frame.Role == BusRole.DutCan
                    ? StationCanWiring.DutDisplayName
                    : StationCanWiring.KeypadDisplayName,
                "0x" + frame.Id.ToString("X"),
                frame.ExtendedId ? "扩展" : "标准",
                frame.Dlc, Convert.ToHexString(frame.Data.ToArray()),
                observation.LostFrames));
        };
        _hardware.FrameTransmitAcknowledged += frame =>
            QueueCanUi(new CanMonitorRow(
                DateTimeOffset.Now.ToString("HH:mm:ss.fff"),
                frame.Role == BusRole.DutCan
                    ? StationCanWiring.DutDisplayName
                    : StationCanWiring.KeypadDisplayName,
                "0x" + frame.Id.ToString("X"),
                frame.ExtendedId ? "扩展" : "标准",
                frame.Dlc, Convert.ToHexString(frame.Data.ToArray()), 0,
                "TX-ACK"));
        _liveSignals.SignalReceived += (name, value) =>
        {
            var row = new DecodedCanRow(
                value.Observation?.Time.Utc.ToLocalTime().ToString("HH:mm:ss.fff") ?? "",
                name,
                value.Observation?.Value?.ToString("R", CultureInfo.InvariantCulture) ?? "",
                value.Observation?.Unit ?? "",
                value.Status.ToString());
            lock (_monitorDisplayGate)
            {
                if (_canUiStopped) return;
                if (_pendingDecodedUi.Count == PendingDecodedLimit)
                {
                    _pendingDecodedUi.Dequeue();
                    _displaySkippedDecoded++;
                }
                _pendingDecodedUi.Enqueue(row);
            }
        };
        _canUiTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(125)
        };
        _canUiTimer.Tick += (_, _) => FlushCanMonitorUi();
        _canUiTimer.Start();
    }

    private void QueueCanUi(CanMonitorRow row)
    {
        lock (_monitorDisplayGate)
        {
            if (_canUiStopped) return;
            if (_pendingCanUi.Count == PendingCanLimit)
            {
                _pendingCanUi.Dequeue();
                _displaySkippedCan++;
            }
            _pendingCanUi.Enqueue(row);
        }
    }

    private void FlushCanMonitorUi()
    {
        try
        {
            CanMonitorRow[] can;
            DecodedCanRow[] decoded;
            long skippedCan, skippedDecoded;
            lock (_monitorDisplayGate)
            {
                if (_canUiStopped) return;
                // Show the most recent frames instead of seconds-old backlog;
                // dropped monitor rows are NOT a claim of CAN hardware loss.
                while (_pendingCanUi.Count > CanPerTick)
                {
                    _pendingCanUi.Dequeue();
                    _displaySkippedCan++;
                }
                while (_pendingDecodedUi.Count > DecodedPerTick)
                {
                    _pendingDecodedUi.Dequeue();
                    _displaySkippedDecoded++;
                }
                can = _pendingCanUi.ToArray();
                _pendingCanUi.Clear();
                decoded = _pendingDecodedUi.ToArray();
                _pendingDecodedUi.Clear();
                skippedCan = _displaySkippedCan;
                skippedDecoded = _displaySkippedDecoded;
            }
            // DispatcherTimer owns this method: both WPF collections are
            // mutated only on the dispatcher, one Reset per nonempty batch.
            CanRecords.AppendBatch(can, 500);
            DecodedCanRecords.AppendBatch(decoded, 500);
            var now = DateTimeOffset.UtcNow;
            if (now - _lastMonitorStatusUpdate >= TimeSpan.FromSeconds(1))
            {
                _lastMonitorStatusUpdate = now;
                CanMonitorDisplayStatus = skippedCan + skippedDecoded == 0
                    ? "RX / TX-ACK · 8 次/秒 · 最近 500 条"
                    : "显示限流：CAN " + skippedCan + " / DBC " + skippedDecoded +
                      " 条未入列表（不代表硬件丢帧）";
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // Stop only the defective WPF projection, not the USB/CAN worker.
            _canUiTimer?.Stop();
            UiExceptionReporter.Record("CAN monitor batch UI", error);
            StatusMessage = "CAN 监视刷新异常，显示已停止（不影响底层采集）：" +
                error.Message;
        }
    }

    private void StopCanMonitorUi()
    {
        _canUiTimer?.Stop();
        _canUiTimer = null;
        lock (_monitorDisplayGate)
        {
            _canUiStopped = true;
            _pendingCanUi.Clear();
            _pendingDecodedUi.Clear();
        }
    }
}
