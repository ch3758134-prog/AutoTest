using System.Diagnostics;
using AutoTest.Core;
using AutoTest.Contracts;
using AutoTest.Devices.Station;
using AutoTest.Testing.Authoring;
using AutoTest.Testing.Runtime;

namespace AutoTest.Desktop.Testing;

/// <summary>Host request/USB acknowledgement timing only. NOT a measured ECU RX
/// period, physical bus oscilloscope trace or proof of ECU reception.</summary>
public sealed record PeriodicCanTaskEvidence(
    Guid TaskId, string Channel, uint CanId, bool Extended,
    byte Dlc, string DataHex, int PeriodMs, int DurationMs,
    DateTimeOffset StartedUtc, DateTimeOffset? StoppedUtc, string StopReason,
    string SourceCaseId, string SourceStepId, Guid SourceStepOccurrenceId,
    long HostRequests, long UsbAcks, DateTimeOffset? LastUsbAckUtc,
    double LongestAckIntervalMs,
    long SkippedPeriods, string? Fault);

public sealed partial class ConfiguredStationActionPort
{
    private TestDocumentId _currentCaseId;
    private StepId _currentStepId;
    private Guid _currentStepOccurrence;
    private readonly List<PeriodicTracker> _periodicHistory = new();
    private readonly object _periodicHistoryGate = new();

    public void BindStep(TestDocumentId caseId, StepId stepId, Guid stepOccurrenceId)
    {
        _currentCaseId = caseId;
        _currentStepId = stepId;
        _currentStepOccurrence = stepOccurrenceId;
    }

    private sealed class PeriodicTracker
    {
        private readonly object _sync = new();
        private readonly CanFrame _frame;
        private readonly int _period, _duration;
        private readonly string _case, _step;
        private readonly Guid _occurrence;
        private readonly Guid _task = Guid.NewGuid();
        private readonly DateTimeOffset _begin = DateTimeOffset.UtcNow;
        private DateTimeOffset? _end;
        private string _stopReason = "";
        private string? _fault;
        private long _requests, _acks, _skipped, _lastAckTicks;
        private DateTimeOffset? _lastAckUtc;
        private double _longest;

        internal PeriodicTracker(CanFrame frame, int period, int duration,
            TestDocumentId caseId, StepId stepId, Guid occurrence)
        {
            _frame = frame; _period = period; _duration = duration;
            _case = caseId.ToString(); _step = stepId.ToString();
            _occurrence = occurrence;
        }
        public void RecordRequest() { lock (_sync) _requests++; }
        public void RecordAck()
        {
            var timestamp = Stopwatch.GetTimestamp();
            lock (_sync)
            {
                // Initial frame was already ACKed by the host. This is still
                // not a CAN controller transmit-complete or ECU receive signal.
                if (_acks == 0) _requests++;
                if (_lastAckTicks != 0)
                    _longest = Math.Max(_longest,
                        Stopwatch.GetElapsedTime(_lastAckTicks, timestamp).TotalMilliseconds);
                _lastAckTicks = timestamp;
                _lastAckUtc = DateTimeOffset.UtcNow;
                _acks++;
            }
        }
        public void RecordSkipped(long missed)
        {
            if (missed > 0) lock (_sync) _skipped += missed;
        }
        public void StopWith(string reason)
        {
            lock (_sync)
            {
                if (_end is not null) return;
                _end = DateTimeOffset.UtcNow;
                _stopReason = reason;
            }
        }
        public void RecordFault(Exception error)
        {
            lock (_sync) _fault = error.GetType().Name + ": " + error.Message;
            StopWith("SEND_ERROR");
        }
        public PeriodicCanTaskEvidence Snapshot()
        {
            lock (_sync)
                return new PeriodicCanTaskEvidence(_task, _frame.Role.ToString(),
                    _frame.Id, _frame.ExtendedId, _frame.Dlc,
                    Convert.ToHexString(_frame.Data.ToArray()), _period, _duration,
                    _begin, _end, _stopReason, _case, _step, _occurrence,
                    _requests, _acks, _lastAckUtc, _longest, _skipped, _fault);
        }
    }

    private PeriodicTracker BeginPeriodicEvidence(CanFrame frame, int period, int duration)
    {
        lock (_periodicHistoryGate)
        {
            if (_periodicHistory.Count >= 4000)
                throw new InvalidOperationException("周期 CAN 任务创建/替换次数超出证据容量；终止整组，不得丢弃追溯记录。");
            var meter = new PeriodicTracker(frame, period, duration,
                _currentCaseId, _currentStepId, _currentStepOccurrence);
            _periodicHistory.Add(meter);
            return meter;
        }
    }

    /// <summary>Bounded by the number of task starts/replacements; snapshots
    /// are copied after foreground steps and again after project cleanup.</summary>
    public IReadOnlyList<PeriodicCanTaskEvidence> PeriodicEvidenceSnapshot()
    {
        lock (_periodicHistoryGate)
            return _periodicHistory.Select(x => x.Snapshot()).ToArray();
    }
}
