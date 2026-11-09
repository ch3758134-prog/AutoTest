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
}
