using System.Collections.Immutable;
using AutoTest.Projects.ControllerConfig;
using AutoTest.Projects.Project;
using Xunit;

namespace AutoTest.Projects.Tests.ControllerConfig;

public sealed class ControllerConfigurationTests
{
    private static (EcuControlProfile profile, Guid controllerId, Guid defaultState) Profile()
    {
        var state = ControllerState.Create("Initial", 0x1, 0);
        var controller = new ControllerDefinition(Guid.NewGuid(), "Rotary", 0, "Buttons", state.Id,
            ImmutableArray.Create(state));
        var profile = EcuControlProfile.Create("ECU-A", "KEY-SIM", Guid.NewGuid()) with
        { Controllers = ImmutableArray.Create(controller) };
        return (profile.Validate(), controller.Id, state.Id);
    }
    [Fact] public void ArbitraryStateCounts_AreSupported()
    {
        var (profile, controller, _) = Profile();
        var editor = new ControllerEditSession(profile);
        for (var i = 0; i < 24; i++) editor.AddState(controller, $"Custom-{i}", (uint)(i + 1));
        Assert.Equal(25, editor.Current.Controllers[0].States.Length);
    }
    [Theory]
    [InlineData(0x100000u)] [InlineData(uint.MaxValue)]
    public void MasksAboveTwentyBits_AreRejected(uint mask)
    {
        var state = ControllerState.Create("Invalid", 0, 0) with { KeyMask = mask };
        Assert.Throws<ProjectDataException>(() => state.Validate());
    }
    [Fact] public void DuplicateStateName_IsRejected()
    {
        var (profile, controller, _) = Profile();
        var editor = new ControllerEditSession(profile);
        Assert.Throws<ProjectDataException>(() => editor.AddState(controller, "initial", 0x2));
    }
    [Fact] public void DeletedDefault_IsRejected()
    {
        var (profile, controller, state) = Profile();
        Assert.Throws<ProjectDataException>(() => new ControllerEditSession(profile).RemoveState(controller, state));
    }
    [Fact] public void StaleReferences_AreRejectedAfterEdit()
    {
        var (profile, controller, state) = Profile();
        var reference = new StateReference(profile.ProfileId, controller, state, profile.Revision);
        var editor = new ControllerEditSession(profile);
        editor.AddState(controller, "Second", 0x3);
        Assert.Throws<ProjectDataException>(() => reference.Resolve(editor.Current));
    }
    [Fact] public void VoltageWindow_RejectsInvertedLimits()
    {
        Assert.Throws<ProjectDataException>(() => new VoltageWindow(1, 1, 2, 0).Validate());
        Assert.Throws<ProjectDataException>(() => new VoltageWindow(6, 1, 0, 2).Validate());
    }
    [Fact] public void ManualReading_SeparateFromConfiguredTarget()
    {
        var (profile, controller, state) = Profile();
        var editor = new ControllerEditSession(profile);
        editor.SetWindow(controller, state, new VoltageWindow(2, 2.5, 2.3, 2.7));
        var fact = editor.RecordManualVoltage(controller, state, 2, 3.1, "operator", "multimeter/shot-1", DateTimeOffset.UtcNow);
        Assert.Equal(MeasurementStatus.OutOfRange, fact.Status);
        Assert.Equal(ObservationOrigin.Manual, fact.Origin);
        Assert.Equal(2.5, editor.Current.Controllers[0].States[0].Windows[0].TargetVolts);
    }
    [Fact] public void HardwarePolicy_RoundTripAndValidateIdentity()
    {
        var (profile, controller, state) = Profile();
        var policy = new HardwareKeyPolicy(profile.HardwareProfileId, 0xFFFFF,
            ImmutableArray.Create(new IncompatibleKeyPair(1, 2)));
        var configured = profile with { KeyHardwarePolicy = policy };
        var reopened = ControllerRepository.Decode(ControllerRepository.Encode(configured));
        Assert.NotNull(reopened.KeyHardwarePolicy);
        Assert.Equal(policy.SupportedMask, reopened.KeyHardwarePolicy.SupportedMask);
        Assert.Equal(policy.HardwareProfileId, reopened.KeyHardwarePolicy.HardwareProfileId);
        Assert.Single(reopened.KeyHardwarePolicy.ForbiddenPairs);
        Assert.Equal(new IncompatibleKeyPair(1, 2),
            reopened.KeyHardwarePolicy.ForbiddenPairs[0]);
        var stalePolicy = configured with
        {
            KeyHardwarePolicy = policy with { HardwareProfileId = Guid.NewGuid() }
        };
        Assert.Throws<ProjectDataException>(() => stalePolicy.Validate());
        Assert.Null(profile.KeyHardwarePolicy); // Existing files remain readable.
    }

    [Fact] public void HardwarePolicy_RejectsUnsupportedAndIncompatibleKeys()
    {
        var (profile, controller, state) = Profile();
        var editor = new ControllerEditSession(profile);
        editor.AddState(controller, "Second", 0x2);
        var policy = new HardwareKeyPolicy(profile.HardwareProfileId, 0xFFFFF,
            ImmutableArray.Create(new IncompatibleKeyPair(1, 2)));
        var states = editor.Current.Controllers[0].States;
        var refs = states.Select(s => new StateReference(profile.ProfileId, controller, s.Id, editor.Current.Revision));
        Assert.Throws<ProjectDataException>(() => policy.ValidateCombinedMask(editor.Current, refs));
    }
    [Fact] public void DeleteAndAdd_KeepStateOrdersUnique()
    {
        var (profile, controller, _) = Profile();
        var editor = new ControllerEditSession(profile);
        editor.AddState(controller, "Second", 0x2);
        editor.AddState(controller, "Third", 0x4);
        var middle = editor.Current.Controllers[0].States.Single(s => s.Name == "Second");
        editor.RemoveState(controller, middle.Id);
        editor.AddState(controller, "Fourth", 0x8);
        Assert.Equal(new[] { 0, 1, 2 }, editor.Current.Controllers[0].States.Select(s => s.Order).ToArray());
    }
    [Fact] public void Undo_RestoresPreviousRevisionAndStateCount()
    {
        var (profile, controller, _) = Profile();
        var editor = new ControllerEditSession(profile);
        editor.AddState(controller, "Added", 0x8);
        editor.Undo();
        Assert.Equal(profile.Revision, editor.Current.Revision);
        Assert.Single(editor.Current.Controllers[0].States);
    }
    [Fact] public async Task SaveReopenAndConflictPreservesFacts()
    {
        var (profile, controller, state) = Profile();
        var dir = Path.Combine(Path.GetTempPath(), "AutoTest_Controller_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "control.json");
        try
        {
            var initial = await ControllerRepository.SaveAsync(profile, path);
            var reopened = await ControllerRepository.OpenAsync(path);
            Assert.Equal(profile.ProfileId, reopened.Profile.ProfileId);
            Assert.Equal(1u, reopened.Profile.ResolveState(controller, state, profile.Revision).KeyMask);
            var changed = new ControllerEditSession(reopened.Profile);
            changed.AddState(controller, "Second", 0x3);
            var saved = await ControllerRepository.SaveAsync(changed.Current, path, initial.Sha256);
            Assert.Equal(changed.Current.Revision, saved.Revision);
            var stale = await Assert.ThrowsAsync<ProjectDataException>(() => ControllerRepository.SaveAsync(profile, path, initial.Sha256));
            Assert.Equal("CTRL_CONFLICT", stale.Code);
            Assert.Equal(2, (await ControllerRepository.OpenAsync(path)).Profile.Controllers[0].States.Length);
        }
        finally { Directory.Delete(dir, true); }
    }
    [Fact] public async Task CorruptImport_IsExplicitError()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "{ not-json }");
            var error = await Assert.ThrowsAsync<ProjectDataException>(() => ControllerRepository.OpenAsync(path));
            Assert.Equal("CTRL_JSON", error.Code);
        }
        finally { File.Delete(path); }
    }


    [Fact] public void ButtonTiming_StoredWithConfiguredStateAndRoundTrips()
    {
        var (profile, controller, stateId) = Profile();
        var state = profile.Controllers[0].States[0] with
        {
            ActionTiming = new ButtonActionTiming(150, 350, 80)
        };
        profile = profile with
        {
            Controllers = ImmutableArray.Create(profile.Controllers[0] with
            { States = ImmutableArray.Create(state) })
        };
        var reloaded = ControllerRepository.Decode(ControllerRepository.Encode(profile));
        var actual = reloaded.ResolveState(controller, stateId, reloaded.Revision);
        Assert.Equal(new ButtonActionTiming(150, 350, 80), actual.ActionTiming);
        Assert.Throws<ProjectDataException>(() =>
            (state with { ActionTiming = new ButtonActionTiming(-1, 0, 0) }).Validate());
    }

    [Fact] public void ButtonPulse_CombinesKeyStatesAndRestoresPriorActiveMask()
    {
        var (profile, controller, stateId) = Profile();
        var editor = new ControllerEditSession(profile);
        editor.AddState(controller, "Secondary", 0x2);
        var updated = editor.Current;
        var targetId = updated.Controllers[0].States.Single(x => x.Name == "Secondary").Id;
        var initial = new StateReference(updated.ProfileId, controller, stateId, updated.Revision);
        var target = new StateReference(updated.ProfileId, controller, targetId, updated.Revision);
        var policy = new HardwareKeyPolicy(updated.HardwareProfileId, 0xFFFFF,
            ImmutableArray<IncompatibleKeyPair>.Empty);
        var plan = ButtonActionPlanner.Resolve(updated, policy, [initial],
            new ButtonActionRequest(target, ButtonActuation.Pulse,
                new ButtonActionTiming(120, 250, 60)));
        Assert.Equal(1u, plan.MaskBefore);
        Assert.Equal(3u, plan.MaskDuringOperation);
        Assert.Equal(1u, plan.MaskAfterOperation);
        Assert.Equal(120, plan.Timing.DelayBeforePressMs);
        Assert.Equal(250, plan.Timing.DefaultPressDurationMs);
        Assert.Single(plan.ActiveAfterOperation);
        Assert.Equal(initial, plan.ActiveAfterOperation[0]);
    }

    [Fact] public void ButtonHoldAndRelease_PreserveUnrelatedActiveStates()
    {
        var (profile, controller, stateId) = Profile();
        var editor = new ControllerEditSession(profile);
        editor.AddState(controller, "Other", 0x4);
        var updated = editor.Current;
        var id = updated.Controllers[0].States.Single(x => x.Name == "Other").Id;
        var first = new StateReference(updated.ProfileId, controller, stateId, updated.Revision);
        var second = new StateReference(updated.ProfileId, controller, id, updated.Revision);
        var hardware = new HardwareKeyPolicy(updated.HardwareProfileId, 0xFFFFF,
            ImmutableArray<IncompatibleKeyPair>.Empty);
        var hold = ButtonActionPlanner.Resolve(updated, hardware, [first],
            new ButtonActionRequest(second, ButtonActuation.Hold,
                new ButtonActionTiming(50, 0, 20)));
        Assert.Equal(5u, hold.MaskAfterOperation);
        Assert.Equal(0, hold.Timing.DelayAfterReleaseMs);
        var release = ButtonActionPlanner.Resolve(updated, hardware, hold.ActiveAfterOperation,
            new ButtonActionRequest(second, ButtonActuation.Release,
                new ButtonActionTiming(50, 0, 20)));
        Assert.Equal(1u, release.MaskAfterOperation);
        Assert.Equal(0, release.Timing.DelayBeforePressMs);
        Assert.Equal(20, release.Timing.DelayAfterReleaseMs);
    }

    [Fact] public void ButtonPlanning_RejectsConflictStaleMappingAndUntimedPulse()
    {
        var (profile, controller, stateId) = Profile();
        var editor = new ControllerEditSession(profile);
        editor.AddState(controller, "Other", 0x2);
        var updated = editor.Current;
        var targetId = updated.Controllers[0].States.Last().Id;
        var first = new StateReference(updated.ProfileId, controller, stateId, updated.Revision);
        var target = new StateReference(updated.ProfileId, controller, targetId, updated.Revision);
        var blocked = new HardwareKeyPolicy(updated.HardwareProfileId, 0xFFFFF,
            ImmutableArray.Create(new IncompatibleKeyPair(1, 2)));
        Assert.Throws<ProjectDataException>(() => ButtonActionPlanner.Resolve(
            updated, blocked, [first],
            new ButtonActionRequest(target, ButtonActuation.Hold)));
        var available = blocked with { ForbiddenPairs = ImmutableArray<IncompatibleKeyPair>.Empty };
        Assert.Throws<ProjectDataException>(() => ButtonActionPlanner.Resolve(
            updated, available, [], new ButtonActionRequest(target, ButtonActuation.Pulse)));
        var stale = target with { Revision = profile.Revision };
        Assert.Throws<ProjectDataException>(() => ButtonActionPlanner.Resolve(
            updated, available, [], new ButtonActionRequest(stale, ButtonActuation.Hold)));
    }

}
