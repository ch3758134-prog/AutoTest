using AutoTest.Core;

namespace AutoTest.Application.Production;

/// <summary>
/// A pure, single-use physical edge detector for a verified hardware adapter.
/// It is NOT an admission authority by itself: it never proves that the input
/// came from a real MCU. A UI boolean or software command must never be wired
/// to it as a physical START signal.
/// </summary>
public sealed record ProductionStartDecision(bool Accepted, string Code, RunId? Run);

public sealed class ProductionStartGate
{
    private readonly object _sync = new();
    private RunId? _armedRun;
    private bool _claimed;
    private bool _pressed = true;
    private bool _releaseObserved;

    public RunId? ArmedRun { get { lock (_sync) return _armedRun; } }

    public void Arm(RunId run)
    {
        if (run.Value == Guid.Empty)
            throw new ArgumentException("RunId must be nonempty.", nameof(run));
        lock (_sync)
        {
            if (_armedRun is { } previous && previous == run)
                throw new InvalidOperationException("Inspection cannot be rearmed.");
            if (_armedRun is not null && !_claimed)
                throw new InvalidOperationException("An earlier inspection is armed.");
            _armedRun = run;
            _claimed = false;
            _releaseObserved = false;
            _pressed = true; // Treat an unknown input as HELD, never released.
        }
    }

    public ProductionStartDecision ObservePhysical(RunId run, bool pressed)
    {
        lock (_sync)
        {
            if (_armedRun is not { } armed || armed != run)
                return new(false, "START_NOT_ARMED", _armedRun);
            if (_claimed)
                return new(false, "START_ALREADY_CLAIMED", armed);
            var wasPressed = _pressed;
            _pressed = pressed;
            if (!pressed)
            {
                _releaseObserved = true;
                return new(false, "PHYSICAL_RELEASE_OBSERVED", armed);
            }
            if (!_releaseObserved || wasPressed)
                return new(false, "PHYSICAL_FRESH_EDGE_REQUIRED", armed);
            _claimed = true;
            return new(true, "PHYSICAL_START_CLAIMED", armed);
        }
    }

    public void Disarm()
    {
        lock (_sync)
        {
            _armedRun = null;
            _claimed = false;
            _releaseObserved = false;
            _pressed = true;
        }
    }
}
