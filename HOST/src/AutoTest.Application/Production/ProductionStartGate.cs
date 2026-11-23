using AutoTest.Core;

namespace AutoTest.Application.Production;

/// <summary>
/// Software START and a verified hardware START rising edge are two inputs to
/// the same one-shot inspection admission. The gate never sends I/O itself.
/// It cannot be armed by a raw scan, and cannot rearm the same RunId after a
/// completed or uncertain inspection. The device adapter must debounce its
/// electrical signal and report transitions; a held button is not a new edge.
/// </summary>
public enum ProductionStartSource { Software, PhysicalButton }
public sealed record ProductionStartDecision(bool Accepted, string Code,
    ProductionStartSource Source, RunId? Run);

public sealed class ProductionStartGate
{
    private readonly object _sync = new();
    private RunId? _armedRun;
    private bool _claimed;
    private bool _physicalPressed;
    private bool _physicalReleaseObserved;

    public RunId? ArmedRun
    {
        get { lock (_sync) return _armedRun; }
    }

    /// <summary>
    /// Only the formal Ready transition may arm this gate. Every new RunId
    /// begins with physical button release unconfirmed, preventing a stuck
    /// switch from starting the next DUT.
    /// </summary>
    public void Arm(RunId run)
    {
        if (run.Value == Guid.Empty)
            throw new ArgumentException("RunId must be non-empty.", nameof(run));
        lock (_sync)
        {
            if (_armedRun is { } previous && previous.Equals(run))
                throw new InvalidOperationException("The same inspection cannot be rearmed.");
            if (_armedRun is not null && !_claimed)
                throw new InvalidOperationException("An earlier inspection is still armed.");
            _armedRun = run;
            _claimed = false;
            _physicalReleaseObserved = false;
            _physicalPressed = true; // conservatively treat the input as held
        }
    }

    public ProductionStartDecision RequestSoftware(RunId run)
    {
        lock (_sync) return Claim(run, ProductionStartSource.Software);
    }

    /// <summary>
    /// Called only by the verified physical-input adapter, not by a UI
    /// binding. The first released state following Arm must be observed before
    /// a press can be considered a fresh rising edge.
    /// </summary>
    public ProductionStartDecision ObservePhysical(RunId run, bool pressed)
    {
        lock (_sync)
        {
            var wasPressed = _physicalPressed;
            _physicalPressed = pressed;
            if (!pressed)
            {
                if (_armedRun is { } active && active.Equals(run))
                    _physicalReleaseObserved = true;
                return new(false, "PHYSICAL_RELEASE_OBSERVED", ProductionStartSource.PhysicalButton,
                    _armedRun);
            }
            if (!_physicalReleaseObserved || wasPressed)
                return new(false, "PHYSICAL_FRESH_EDGE_REQUIRED",
                    ProductionStartSource.PhysicalButton, _armedRun);
            return Claim(run, ProductionStartSource.PhysicalButton);
        }
    }

    private ProductionStartDecision Claim(RunId run, ProductionStartSource source)
    {
        if (_armedRun is not { } armed || !armed.Equals(run))
            return new(false, "START_NOT_ARMED", source, _armedRun);
        if (_claimed)
            return new(false, "START_ALREADY_CLAIMED", source, armed);
        _claimed = true;
        return new(true, "START_CLAIMED", source, armed);
    }

    /// <summary>
    /// Disarm only after the domain session has actually left Ready/Running
    /// and its recovery policy has explicitly allowed a new inspection.
    /// A subsequent inspection must use a distinct RunId.
    /// </summary>
    public void Disarm()
    {
        lock (_sync)
        {
            _armedRun = null;
            _claimed = false;
            _physicalReleaseObserved = false;
            _physicalPressed = true;
        }
    }
}
