using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using AutoTest.Contracts;
using AutoTest.Core;

namespace AutoTest.DeviceLab;

/// <summary>One physical CAN identifier row, updated by the actual RX stream.</summary>
public sealed class CanIdMonitorRow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Channel { get; }
    public string Id { get; }
    public string Format { get; }
    public string LastTime { get; private set; } = "—";
    public string DataHex { get; private set; } = "";
    public string PeriodMs { get; private set; } = "—";
    public string Dlc { get; private set; } = "0";
    public long Count { get; private set; }
    public long LostFrames { get; private set; }
    public long Generation { get; private set; }
    private DateTimeOffset? _previous;

    public CanIdMonitorRow(CanObservation observation)
    {
        Channel = observation.Frame.Role == BusRole.DutCan ? "CAN1 / DUT" : "CAN0 / KEY";
        Id = $"0x{observation.Frame.Id:X}";
        Format = observation.Frame.ExtendedId ? "29-bit" : "11-bit";
        Update(observation);
    }
    public void Update(CanObservation observation)
    {
        if (Generation != observation.SessionGeneration)
        {
            _previous = null;
            Count = 0;
        }
        Count++;
        Generation = observation.SessionGeneration;
        var stamp = observation.Time.Utc;
        PeriodMs = _previous is null ? "—" :
            ((stamp - _previous.Value).TotalMilliseconds).ToString("F2", CultureInfo.InvariantCulture);
        _previous = stamp;
        LastTime = stamp.ToLocalTime().ToString("HH:mm:ss.fff");
        Dlc = observation.Frame.Dlc.ToString(CultureInfo.InvariantCulture);
        DataHex = Convert.ToHexString(observation.Frame.Data.AsSpan());
        LostFrames = observation.LostFrames;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
}

/// <summary>
/// Bounded UI aggregation; USB RX callbacks only enqueue and never wait for WPF.
/// Overflow is always accounted for, and connection generations never merge.
/// </summary>
public sealed class CanIdMonitor : IDisposable
{
    private const int PendingCapacity = 8192;
    private readonly ConcurrentQueue<CanObservation> _pending = new();
    private readonly Dictionary<(BusRole Role, uint Id, bool Extended), CanIdMonitorRow> _rows = new();
    private readonly DispatcherTimer _timer;
    private long _queued, _dropped;
    private long _generation;
    private bool _disposed;
    public ObservableCollection<CanIdMonitorRow> Rows { get; } = new();
    public long UiDropped => Interlocked.Read(ref _dropped);
    public long Generation => _generation;

    public CanIdMonitor(Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        { Interval = TimeSpan.FromMilliseconds(120) };
        _timer.Tick += OnTick;
        _timer.Start();
    }
    public void Enqueue(CanObservation observation)
    {
        if (_disposed || observation.Evidence.Value == Guid.Empty ||
            observation.Time.Source != TimeSource.HostArrival ||
            observation.EvidenceQuality != EvidenceQuality.HostObserved) return;
        if (Interlocked.Increment(ref _queued) > PendingCapacity)
        {
            Interlocked.Decrement(ref _queued);
            Interlocked.Increment(ref _dropped);
            return;
        }
        _pending.Enqueue(observation);
    }
    private void OnTick(object? sender, EventArgs e)
    {
        var count = 0;
        while (count++ < 1200 && _pending.TryDequeue(out var item))
        {
            Interlocked.Decrement(ref _queued);
            if (item.SessionGeneration < _generation) continue;
            if (item.SessionGeneration != _generation)
            {
                _generation = item.SessionGeneration;
                Rows.Clear(); _rows.Clear();
            }
            var key = (item.Frame.Role, item.Frame.Id, item.Frame.ExtendedId);
            if (!_rows.TryGetValue(key, out var row))
            {
                row = new CanIdMonitorRow(item);
                _rows.Add(key, row);
                Rows.Add(row);
            }
            else row.Update(item);
        }
    }
    public void Clear()
    {
        while (_pending.TryDequeue(out _)) Interlocked.Decrement(ref _queued);
        Rows.Clear(); _rows.Clear();
        _generation = 0;
        Interlocked.Exchange(ref _dropped, 0);
    }
    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
        Clear();
    }
}
