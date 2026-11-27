using System.IO;
using AutoTest.Contracts;
using AutoTest.Core;

namespace AutoTest.Desktop.Hardware;

/// <summary>
/// A fresh, real ECU CAN receive event after clamp actuation is the fixture's
/// functional readiness feedback, NOT a mechanical sensor or safety interlock.
/// </summary>
public sealed class EcuCanFixtureFeedback : IDisposable
{
    private readonly HardwareConnectionService _hardware;
    private readonly long _epoch;
    private readonly TaskCompletionSource<CanObservation> _received =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _sync = new();
    private DateTimeOffset _afterPressUtc = DateTimeOffset.MaxValue;
    private bool _armed;
    private bool _disposed;

    public EcuCanFixtureFeedback(HardwareConnectionService hardware)
    {
        _hardware = hardware ?? throw new ArgumentNullException(nameof(hardware));
        if (!hardware.IsConnected || hardware.CurrentStationEpoch <= 0)
            throw new IOException("主控未连接，不能检测 ECU CAN 夹具反馈。");
        _epoch = hardware.CurrentStationEpoch;
        hardware.FrameObserved += OnFrame;
    }

    public void ArmAfterPress()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_armed) throw new InvalidOperationException("夹具反馈已开始检测。");
            if (!_hardware.IsConnected || _hardware.CurrentStationEpoch != _epoch)
                throw new IOException("主控连接发生变化，夹具通信状态无效。");
            _afterPressUtc = DateTimeOffset.UtcNow;
            _armed = true;
        }
    }

    private void OnFrame(CanObservation observation)
    {
        lock (_sync)
        {
            if (_disposed || !_armed ||
                observation.Frame.Role != BusRole.DutCan ||
                observation.EvidenceQuality != EvidenceQuality.HostObserved ||
                observation.Evidence.Value == Guid.Empty ||
                observation.Time.Utc <= _afterPressUtc ||
                !_hardware.IsConnected || _hardware.CurrentStationEpoch != _epoch)
                return;
            _received.TrySetResult(observation);
        }
    }

    public async Task<CanObservation> WaitAsync(TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_armed) throw new InvalidOperationException("尚未完成夹具压紧指令。");
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            var frame = await _received.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            if (!_hardware.IsConnected || _hardware.CurrentStationEpoch != _epoch)
                throw new IOException("等待 ECU CAN 时主控连接发生变化。");
            return frame;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("夹具压紧后没有收到新的 ECU CAN 报文，通信就绪未确认。请检查夹具、ECU 供电、IGN 和 CAN 接线。");
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _hardware.FrameObserved -= OnFrame;
        }
    }
}
