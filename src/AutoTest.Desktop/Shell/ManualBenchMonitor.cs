using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Threading;
using AutoTest.Contracts;
using AutoTest.Devices.Can;
using Prism.Commands;
using Prism.Mvvm;

namespace AutoTest.Desktop.Shell;

/// <summary>Actual host-side CAN observation; never synthesizes DUT acknowledgement.</summary>
public sealed partial class ManualBenchViewModel
{
    public sealed class MonitorEntry : BindableBase
    {
        private string _lastTime;
        private string _data;
        private long _count;
        private long _lost;
        public MonitorEntry(string channel, string direction, bool extended, uint id,
            string time, string data, long lost)
        {
            Channel = channel;
            Direction = direction;
            Extended = extended;
            Id = "0x" + id.ToString(extended ? "X8" : "X3");
            _lastTime = time;
            _data = data;
            _lost = lost;
            _count = 1;
        }
        public string Channel { get; }
        public string Direction { get; }
        public bool Extended { get; }
        public string Format => Extended ? "扩展" : "标准";
        public string Id { get; }
        public string Time { get => _lastTime; private set => SetProperty(ref _lastTime, value); }
        public string Data { get => _data; private set => SetProperty(ref _data, value); }
        public long Count { get => _count; private set => SetProperty(ref _count, value); }
        public long Lost { get => _lost; private set => SetProperty(ref _lost, value); }
        public string Status => Lost > 0 ? "丢帧 " + Lost : "接收";
        public void Apply(MonitorEntry newer)
        {
            Time = newer.Time;
            Data = newer.Data;
            Count += newer.Count;
            // HardwareCanReceiveBridge reports cumulative loss; never sum snapshots.
            Lost = Math.Max(Lost, newer.Lost);
            RaisePropertyChanged(nameof(Status));
        }
    }

    private readonly ObservableCollection<MonitorEntry> _monitorGrouped = new();
    private readonly ObservableCollection<MonitorEntry> _monitorTimeline = new();
    private readonly Dictionary<string, MonitorEntry> _monitorIndex = new();
    private readonly Queue<MonitorEntry> _monitorPausedQueue = new();
    private ICollectionView _monitorGroupView = null!;
    private ICollectionView _monitorTimelineView = null!;
    private string _monitorQuery = "";
    private string _monitorChannel = "全部";
    private bool _monitorShowTimeline;
    private bool _monitorPaused;
    private long _monitorPauseOverflow;
    private long _monitorLastObservedLoss;
    private long _monitorLastGeneration;
    private readonly object _incomingMonitorSync = new();
    private readonly Queue<CanObservation> _incomingMonitor = new();
    private readonly DispatcherTimer _monitorDispatchTimer = new()
    { Interval = TimeSpan.FromMilliseconds(100) };
    private long _uiQueueDropped;
    public string CanRxReceiveStatus => _hardware.CanReceiveStatus +
        " / UI排队=" + IncomingMonitorCount + " / UI排队丢弃=" +
        Interlocked.Read(ref _uiQueueDropped);
    private int IncomingMonitorCount
    {
        get { lock (_incomingMonitorSync) return _incomingMonitor.Count; }
    }
    private void QueueMonitorObservation(CanObservation observation)
    {
        lock (_incomingMonitorSync)
        {
            // Keep native USB receive and TEST processing independent of a busy WPF UI.
            if (_incomingMonitor.Count >= 5000)
            {
                _incomingMonitor.Dequeue();
                Interlocked.Increment(ref _uiQueueDropped);
            }
            _incomingMonitor.Enqueue(observation);
        }
    }
    private void FlushMonitorObservations()
    {
        if (_disposed) return;
        for (var i = 0; i < 250; i++)
        {
            CanObservation item;
            lock (_incomingMonitorSync)
            {
                if (_incomingMonitor.Count == 0) break;
                item = _incomingMonitor.Dequeue();
            }
            RegisterRxMonitor(item);
        }
        RaisePropertyChanged(nameof(CanRxReceiveStatus));
    }
    private void StopMonitorDispatch()
    {
        _monitorDispatchTimer.Stop();
        lock (_incomingMonitorSync) _incomingMonitor.Clear();
    }
    public IReadOnlyList<string> MonitorChannelChoices { get; } = new[] { "全部", "ECU CAN", "KEY CAN" };
    public ICollectionView MonitorDisplay => MonitorShowTimeline ? _monitorTimelineView : _monitorGroupView;
    public string MonitorQuery
    {
        get => _monitorQuery;
        set { if (SetProperty(ref _monitorQuery, value ?? "")) RefreshMonitorViews(); }
    }
    public string MonitorChannel
    {
        get => _monitorChannel;
        set { if (SetProperty(ref _monitorChannel, value ?? "全部")) RefreshMonitorViews(); }
    }
    public bool MonitorShowTimeline
    {
        get => _monitorShowTimeline;
        set
        {
            if (SetProperty(ref _monitorShowTimeline, value))
                RaisePropertyChanged(nameof(MonitorDisplay));
        }
    }
    public bool MonitorPaused
    {
        get => _monitorPaused;
        private set
        {
            if (SetProperty(ref _monitorPaused, value))
                RaisePropertyChanged(nameof(MonitorPauseLabel));
        }
    }
    public string MonitorPauseLabel => MonitorPaused ? "恢复显示" : "暂停显示";
    public string MonitorCountSummary => _monitorGrouped.Count + " 个 CAN 组合 · " +
        _monitorTimeline.Count + " 条接收记录" +
        (MonitorPaused ? " · 已暂停视图（未停止硬件接收）" : "");
    public DelegateCommand ToggleMonitorPauseCommand { get; private set; } = null!;
    public DelegateCommand ClearMonitorCommand { get; private set; } = null!;

    private void InitializeMonitor()
    {
        _monitorGroupView = CollectionViewSource.GetDefaultView(_monitorGrouped);
        _monitorTimelineView = CollectionViewSource.GetDefaultView(_monitorTimeline);
        _monitorGroupView.Filter = MatchMonitor;
        _monitorTimelineView.Filter = MatchMonitor;
        ToggleMonitorPauseCommand = new DelegateCommand(ToggleMonitorPause);
        ClearMonitorCommand = new DelegateCommand(ClearMonitor);
        _monitorDispatchTimer.Tick += (_, _) => FlushMonitorObservations();
        _monitorDispatchTimer.Start();
    }
    private bool MatchMonitor(object item)
    {
        if (item is not MonitorEntry entry) return false;
        if (MonitorChannel != "全部" && MonitorChannel != entry.Channel) return false;
        var term = MonitorQuery.Trim();
        return term.Length == 0 ||
            entry.Id.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            entry.Data.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            entry.Channel.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            entry.Format.Contains(term, StringComparison.OrdinalIgnoreCase);
    }
    private void RefreshMonitorViews()
    {
        if (_monitorGroupView is null || _monitorTimelineView is null) return;
        _monitorGroupView.Refresh();
        _monitorTimelineView.Refresh();
    }

    private void RegisterRxMonitor(CanObservation observation)
    {
        var frame = observation.Frame;
        if (_monitorLastGeneration != observation.SessionGeneration)
        {
            _monitorLastGeneration = observation.SessionGeneration;
            _monitorLastObservedLoss = 0;
        }
        if (observation.LostFrames > _monitorLastObservedLoss)
        {
            var newLoss = observation.LostFrames - _monitorLastObservedLoss;
            Append(new BenchLogEntry(DateTime.Now.ToString("HH:mm:ss.fff"), "CAN", "LOSS",
                "0x" + frame.Id.ToString("X"), "",
                "接收路径新增丢帧约 " + newLoss + "；设备无损日志不可由此证明。"));
            _monitorLastObservedLoss = observation.LostFrames;
        }
        var entry = new MonitorEntry(
            frame.Role == BusRole.DutCan ? "ECU CAN" : "KEY CAN",
            "RX", frame.ExtendedId, frame.Id,
            observation.Time.Utc.ToLocalTime().ToString("HH:mm:ss.fff"),
            Convert.ToHexString(frame.Data.AsSpan()), observation.LostFrames);
        if (MonitorPaused)
        {
            if (_monitorPausedQueue.Count < 1500) _monitorPausedQueue.Enqueue(entry);
            else _monitorPauseOverflow++;
            return;
        }
        AddMonitorEntry(entry);
    }
    private void AddMonitorEntry(MonitorEntry entry)
    {
        var groupKey = entry.Channel + ":" + entry.Format + ":" + entry.Id;
        if (!_monitorIndex.TryGetValue(groupKey, out var aggregate))
        {
            aggregate = new MonitorEntry(entry.Channel, entry.Direction, entry.Extended,
                Convert.ToUInt32(entry.Id[2..], 16), entry.Time, entry.Data, entry.Lost);
            _monitorIndex.Add(groupKey, aggregate);
            _monitorGrouped.Insert(0, aggregate);
            while (_monitorGrouped.Count > 500)
            {
                var oldest = _monitorGrouped[^1];
                _monitorIndex.Remove(oldest.Channel + ":" + oldest.Format + ":" + oldest.Id);
                _monitorGrouped.RemoveAt(_monitorGrouped.Count - 1);
            }
        }
        else aggregate.Apply(entry);
        _monitorTimeline.Add(entry);
        while (_monitorTimeline.Count > 1500) _monitorTimeline.RemoveAt(0);
        RaisePropertyChanged(nameof(MonitorCountSummary));
    }
    private void ToggleMonitorPause()
    {
        if (!MonitorPaused)
        {
            MonitorPaused = true;
            RaisePropertyChanged(nameof(MonitorCountSummary));
            return;
        }
        MonitorPaused = false;
        while (_monitorPausedQueue.Count > 0) AddMonitorEntry(_monitorPausedQueue.Dequeue());
        if (_monitorPauseOverflow > 0)
        {
            Append(new BenchLogEntry(DateTime.Now.ToString("HH:mm:ss.fff"),
                "CAN", "LOSS", "", "", "监视显示暂停缓冲丢弃 " +
                _monitorPauseOverflow + " 条视图记录；实际设备接收未停止。"));
            _monitorPauseOverflow = 0;
        }
        RaisePropertyChanged(nameof(MonitorCountSummary));
    }
    private void ClearMonitor()
    {
        lock (_incomingMonitorSync) _incomingMonitor.Clear();
        _monitorIndex.Clear();
        _monitorGrouped.Clear();
        _monitorTimeline.Clear();
        _monitorPausedQueue.Clear();
        _monitorPauseOverflow = 0;
        RaisePropertyChanged(nameof(MonitorCountSummary));
    }
    public MonitorEntry[] SnapshotMonitorTimeline() => _monitorTimeline.ToArray();
}
