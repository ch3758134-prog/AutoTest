using System.Collections.Immutable;
using AutoTest.Projects.Project;

namespace AutoTest.Projects.ControllerConfig;

public sealed class ControllerEditSession
{
    private readonly Stack<EcuControlProfile> _undo = new();
    public EcuControlProfile Current { get; private set; }
    public ControllerEditSession(EcuControlProfile profile) => Current = profile.Validate();
    private void Apply(EcuControlProfile next)
    {
        next.Validate();
        _undo.Push(Current);
        Current = next with { Revision = checked(Current.Revision + 1) };
    }
    public void AddController(string name, string presentation = "Buttons")
    {
        var state = ControllerState.Create("Default", 0, 0);
        var controller = new ControllerDefinition(Guid.NewGuid(), name, Current.Controllers.Length,
            presentation, state.Id, ImmutableArray.Create(state));
        Apply(Current with { Controllers = Current.Controllers.Add(controller) });
    }
    public void RenameController(Guid id, string name) => ReplaceController(Get(id) with { Name = name });
    public void RemoveController(Guid id)
    {
        var original = Get(id);
        var revised = Current.Controllers.Remove(original).OrderBy(c => c.Order)
            .Select((c, index) => c with { Order = index }).ToImmutableArray();
        Apply(Current with { Controllers = revised });
    }
    public void AddState(Guid controllerId, string name, uint mask)
    {
        var controller = Get(controllerId);
        var state = ControllerState.Create(name, mask, controller.States.Length);
        ReplaceController(controller with { States = controller.States.Add(state) });
    }
    public void EditState(Guid controllerId, ControllerState replacement)
    {
        var controller = Get(controllerId);
        var idx = Array.FindIndex(controller.States.ToArray(), s => s.Id == replacement.Id);
        if (idx < 0) throw new ProjectDataException("CTRL_STATE_NOT_FOUND", "State not found.");
        ReplaceController(controller with { States = controller.States.SetItem(idx, replacement) });
    }
    public void RemoveState(Guid controllerId, Guid stateId)
    {
        var controller = Get(controllerId);
        if (controller.DefaultStateId == stateId)
            throw new ProjectDataException("CTRL_DEFAULT", "Choose another default before deleting this state.");
        if (controller.States.Any(s => s.ReturnStateId == stateId))
            throw new ProjectDataException("CTRL_REFERENCED", "State is a spring-return target.");
        var state = controller.States.FirstOrDefault(s => s.Id == stateId) ??
            throw new ProjectDataException("CTRL_STATE_NOT_FOUND", "State missing.");
        var revised = controller.States.Remove(state).OrderBy(s => s.Order)
            .Select((s, index) => s with { Order = index }).ToImmutableArray();
        ReplaceController(controller with { States = revised });
    }
    public void SetDefault(Guid controllerId, Guid stateId) =>
        ReplaceController(Get(controllerId) with { DefaultStateId = stateId });
    public void SetWindow(Guid controllerId, Guid stateId, VoltageWindow window)
    {
        window.Validate();
        var state = Current.ResolveState(controllerId, stateId, Current.Revision);
        var windows = state.Windows.RemoveAll(w => w.Channel == window.Channel).Add(window);
        EditState(controllerId, state with { Windows = windows });
    }
    public VoltageMeasurement RecordManualVoltage(Guid controllerId, Guid stateId, int channel,
        double measuredVolts, string operatorId, string evidenceRef, DateTimeOffset at)
    {
        var state = Current.ResolveState(controllerId, stateId, Current.Revision);
        var window = state.Windows.FirstOrDefault(w => w.Channel == channel);
        if (window is null) throw new ProjectDataException("CTRL_WINDOW_MISSING", "Configure channel before recording a measurement.");
        var status = !double.IsFinite(measuredVolts) ? MeasurementStatus.Invalid :
            window.MinimumVolts.HasValue && (measuredVolts < window.MinimumVolts || measuredVolts > window.MaximumVolts) ?
            MeasurementStatus.OutOfRange : !window.MinimumVolts.HasValue ? MeasurementStatus.Unqualified : MeasurementStatus.Valid;
        var measurement = new VoltageMeasurement(Guid.NewGuid(), channel, measuredVolts, at,
            ObservationOrigin.Manual, status, evidenceRef, operatorId, Current.Revision + 1).Validate();
        EditState(controllerId, state with { Measurements = state.Measurements.Add(measurement) });
        return measurement;
    }
    public void MoveState(Guid controllerId, Guid stateId, int newOrder)
    {
        var controller = Get(controllerId);
        if (newOrder < 0 || newOrder >= controller.States.Length)
            throw new ProjectDataException("CTRL_ORDER", "State order is outside available positions.");
        var ordered = controller.States.OrderBy(s => s.Order).ToList();
        var item = ordered.FirstOrDefault(s => s.Id == stateId) ??
            throw new ProjectDataException("CTRL_STATE_NOT_FOUND", "State missing.");
        ordered.Remove(item);
        ordered.Insert(newOrder, item);
        ReplaceController(controller with { States = ordered.Select((s, i) => s with { Order = i }).ToImmutableArray() });
    }
    public void Undo() { if (_undo.TryPop(out var prev)) Current = prev; }
    private ControllerDefinition Get(Guid id) => Current.Controllers.FirstOrDefault(c => c.Id == id) ??
        throw new ProjectDataException("CTRL_NOT_FOUND", "Controller not found.");
    private void ReplaceController(ControllerDefinition replacement)
    {
        var idx = Array.FindIndex(Current.Controllers.ToArray(), c => c.Id == replacement.Id);
        if (idx < 0) throw new ProjectDataException("CTRL_NOT_FOUND", "Controller not found.");
        Apply(Current with { Controllers = Current.Controllers.SetItem(idx, replacement) });
    }
}


