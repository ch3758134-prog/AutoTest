using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Windows.Data;
using System.Windows.Threading;
using AutoTest.Contracts;
using AutoTest.Devices.Diagnostics;
using Prism.Commands;
using Prism.Mvvm;

namespace AutoTest.Desktop.Bench;

// Real DUT CAN1 broadcasts only, not USB commands, KEY_SIM CAN0 or operator logs.
public sealed record BenchCanTraceRow(DateTimeOffset ReceivedAt, string Id, string FrameType,
    byte Dlc, string Data, long Count);

public sealed class BenchCanIdRow : BindableBase
{
    private DateTimeOffset _lastAt;
    private byte _dlc;
    private string _data;
    private long _count;
    public string Id { get; }
    public string FrameType { get; }
    public DateTimeOffset LastAt { get => _lastAt; private set => SetProperty(ref _lastAt, value); }
    public byte Dlc { get => _dlc; private set => SetProperty(ref _dlc, value); }
    public string Data { get => _data; private set => SetProperty(ref _data, value); }
    public long Count { get => _count; private set => SetProperty(ref _count, value); }
    public BenchCanIdRow(DateTimeOffset at, uint id, bool extended, byte dlc, string data)
    {
        Id = $"0x{id:X}";
        FrameType = extended ? "扩展" : "标准";
        _lastAt = at; _dlc = dlc; _data = data; _count = 1;
    }
    public void Update(DateTimeOffset at, byte dlc, string data)
    { LastAt = at; Dlc = dlc; Data = data; Count++; }
}

public sealed class BenchCanMonitorViewModel : BindableBase
{
    private readonly Func<HardwareBenchController?> _hardware;
    private readonly ConcurrentQueue<CanObservation> _pending = new();
    private readonly Dictionary<(uint, bool), BenchCanIdRow> _ids = new();
    private readonly DispatcherTimer _refresh;
    private HardwareBenchController? _source;
    private int _pendingCount;
    private long _received;
    private long _displayDropped;
    private string _filter = "";
    private bool _paused;
    private string _status = "等待真实 DUT CAN1 接收数据";
    private const int MaxPending = 5000;
    private const int MaxHistory = 2500;
    private const int MaxIds = 1500;

    public ObservableCollection<BenchCanIdRow> ById { get; } = new();
    public ObservableCollection<BenchCanTraceRow> History { get; } = new();
    public ICollectionView IdView { get; }
    public ICollectionView HistoryView { get; }
    public DelegateCommand PauseCommand { get; }
    public DelegateCommand ClearCommand { get; }
    public string FilterId
    {
        get => _filter;
        set { if (SetProperty(ref _filter, value)) { IdView.Refresh(); HistoryView.Refresh(); } }
    }
    public bool Paused
    {
        get => _paused;
        set
        {
            if (SetProperty(ref _paused, value))
            {
                RaisePropertyChanged(nameof(PauseLabel));
                Status = value ? "显示已暂停（仅暂停表格刷新）" : "持续观察真实 DUT CAN1 广播";
            }
        }
    }
    public string PauseLabel => Paused ? "继续显示" : "暂停显示";
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public long Received => _received;
    public long DisplayDropped => Interlocked.Read(ref _displayDropped);
    public long DeviceDropped => _source?.CanDropped ?? 0;

    public BenchCanMonitorViewModel(Func<HardwareBenchController?> hardware)
    {
        _hardware = hardware;
        IdView = CollectionViewSource.GetDefaultView(ById);
        HistoryView = CollectionViewSource.GetDefaultView(History);
        IdView.Filter = o => o is BenchCanIdRow row && Matches(row.Id);
        HistoryView.Filter = o => o is BenchCanTraceRow row && Matches(row.Id);
        PauseCommand = new DelegateCommand(() => Paused = !Paused);
        ClearCommand = new DelegateCommand(ClearDisplay);
        _refresh = new DispatcherTimer(DispatcherPriority.Background)
        { Interval = TimeSpan.FromMilliseconds(100) };
        _refresh.Tick += (_, _) => Drain();
    }
    private bool Matches(string canId) =>
        string.IsNullOrWhiteSpace(_filter) ||
        canId.Contains(_filter.Trim(), StringComparison.OrdinalIgnoreCase);

    public void Start()
    {
        var current = _hardware();
        if (!ReferenceEquals(_source, current))
        {
            if (_source is not null) _source.CanReceived -= OnReceive;
            _source = current;
            if (_source is not null) _source.CanReceived += OnReceive;
        }
        _refresh.Start();
        Status = current is null ? "共享设备接收源尚不可用" :
            "DUT CAN1 接收监视已就绪；连接主控后显示 ECU 广播";
    }

    public void Stop()
    {
        _refresh.Stop();
        if (_source is not null) _source.CanReceived -= OnReceive;
        _source = null;
        while (_pending.TryDequeue(out _)) { }
        Interlocked.Exchange(ref _pendingCount, 0);
    }

    private void OnReceive(CanObservation sample)
    {
        if (sample.Frame.Role != BusRole.DutCan) return;
        _pending.Enqueue(sample);
        if (Interlocked.Increment(ref _pendingCount) > MaxPending && _pending.TryDequeue(out _))
        { Interlocked.Decrement(ref _pendingCount); Interlocked.Increment(ref _displayDropped); }
    }

    private void Drain()
    {
        if (_source is not null && !ReferenceEquals(_source, _hardware())) Start();
        int processed = 0;
        while (processed++ < 500 && _pending.TryDequeue(out var sample))
        {
            Interlocked.Decrement(ref _pendingCount);
            _received++;
            if (Paused) { Interlocked.Increment(ref _displayDropped); continue; }
            var f = sample.Frame;
            var data = string.Join(" ", f.Data.Select(b => b.ToString("X2")));
            var at = sample.Time.Utc.ToLocalTime();
            var key = (f.Id, f.ExtendedId);
            if (_ids.TryGetValue(key, out var existing))
                existing.Update(at, f.Dlc, data);
            else if (_ids.Count < MaxIds)
            {
                var row = new BenchCanIdRow(at, f.Id, f.ExtendedId, f.Dlc, data);
                _ids.Add(key, row); ById.Add(row);
            }
            else Interlocked.Increment(ref _displayDropped);
            History.Insert(0, new BenchCanTraceRow(at, $"0x{f.Id:X}",
                f.ExtendedId ? "扩展" : "标准", f.Dlc, data, _received));
            if (History.Count > MaxHistory) History.RemoveAt(History.Count - 1);
        }
        RaisePropertyChanged(nameof(Received));
        RaisePropertyChanged(nameof(DeviceDropped));
        RaisePropertyChanged(nameof(DisplayDropped));
    }

    public void ClearDisplay()
    {
        ById.Clear();
        History.Clear();
        _ids.Clear();
        _received = 0;
        Interlocked.Exchange(ref _displayDropped, 0);
        RaisePropertyChanged(nameof(Received));
        RaisePropertyChanged(nameof(DisplayDropped));
        // No CAN commands are sent; only UI display is reset.
    }
}
