using System.Collections.Immutable;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Testing.Authoring;
using AutoTest.Testing.Preparation;
using Xunit;

namespace AutoTest.Testing.Tests;

public sealed class TestMappingContractTests
{
    [Fact]
    public void Mapping_RoundTripsInsideOriginalTestDocumentAndEnablesRealSteps()
    {
        var binding = new TestTargetMapping("VehicleState", TestTargetKind.Signal,
            AssetId.New(), 3, new string('a', 64), BusRole.DutCan, 0x123,
            SignalName: "Status", Unit: "");
        var tx = new StepEditTransaction(TestDocument.Create("Bound ECU"));
        tx.UpsertMapping(binding);
        tx.Insert(TestPhase.Test, null, null, 0, StepNode.Create("Read")
            .WithProperty("target", binding.Name).WithProperty("variable", "state")
            .WithProperty("mode", "Next").WithProperty("timeoutMs", "1000")
            .WithProperty("maxAgeMs", "1000"));
        var json = TestDocumentCodec.Serialize(tx.Current);
        var restored = TestDocumentCodec.Deserialize(json);
        Assert.Single(restored.Mappings);
        Assert.Equal(binding, restored.Mappings[0]);
        Assert.NotEmpty(new PlanCompiler().Prepare(restored).Diagnostics);
        Assert.Empty(new PlanCompiler().Prepare(restored, allowRealOperations: true).Diagnostics);
    }

    [Fact]
    public void ReferencedMapping_CannotBeRemoved()
    {
        var binding = new TestTargetMapping("Button", TestTargetKind.Button,
            AssetId.New(), 1, new string('b', 64), ControllerId: Guid.NewGuid(),
            StateId: Guid.NewGuid());
        var tx = new StepEditTransaction(TestDocument.Create("Test"));
        tx.UpsertMapping(binding);
        tx.Insert(TestPhase.Test, null, null, 0, StepNode.Create("Write")
            .WithProperty("target", "Button").WithProperty("value", "Hold"));
        Assert.Throws<InvalidOperationException>(() => tx.RemoveMapping("Button"));
    }
    [Theory]
    [InlineData(false, 0x7FFu, true)]
    [InlineData(false, 0x800u, false)]
    [InlineData(true, 0x1FFFFFFFu, true)]
    [InlineData(true, 0x20000000u, false)]
    public void RawCanMapping_ValidatesFrameFormatWithoutDbc(
        bool extended, uint id, bool valid)
    {
        var mapping = new TestTargetMapping("CAN1_RAW", TestTargetKind.RawCan,
            default, 0, "", BusRole.DutCan, id, extended);
        if (valid) Assert.Same(mapping, mapping.Validate());
        else Assert.Throws<ArgumentException>(() => mapping.Validate());
    }

    [Fact]
    public void RawKeyMapping_UsesOnlyFixedKeypadBusAndPreservesTestFingerprint()
    {
        var mapping = new TestTargetMapping("KEY_SIM20", TestTargetKind.RawKey,
            default, 0, "", BusRole.KeypadCan);
        mapping.Validate();
        Assert.Throws<ArgumentException>(() =>
            (mapping with { Role = BusRole.DutCan }).Validate());
        var tx = new StepEditTransaction(TestDocument.Create("Press"));
        tx.InsertWithMapping(TestPhase.Test, null, null, 0,
            StepNode.Create("Write").WithProperty("target", mapping.Name)
                .WithProperty("value", "Pulse:0x00001:300"), mapping);
        var plan = new PlanCompiler().Prepare(tx.Current, allowRealOperations: true);
        Assert.Empty(plan.Diagnostics);
        Assert.True(plan.ValidateFingerprint());
    }


    [Fact]
    public void R2StyleAssetlessIgnRawCanAndKeySimMappings_RoundTripWithoutInventingAssetIds()
    {
        var doc = TestDocument.Create("R2 Contract") with
        {
            Mappings = ImmutableArray.Create(
                new TestTargetMapping("台架.IGN", TestTargetKind.Ignition, default, 0, ""),
                new TestTargetMapping("待核.CAN", TestTargetKind.RawCan, default, 0, "",
                    BusRole.DutCan, 0x7B0),
                new TestTargetMapping("台架.KEY_SIM.20Key", TestTargetKind.RawKey,
                    default, 0, "", BusRole.KeypadCan))
        };
        var restored = TestDocumentCodec.Deserialize(TestDocumentCodec.Serialize(doc));
        Assert.Equal(3, restored.Mappings.Length);
        Assert.All(restored.Mappings, x => Assert.Equal(Guid.Empty, x.Asset.Value));
    }

    [Fact]
    public void AssetBackedMappingWithoutAssetId_ReportsItsExactName()
    {
        var doc = TestDocument.Create("Bad Generated Project") with
        {
            Mappings = ImmutableArray.Create(
                new TestTargetMapping("绿通.丢失的DBC报文", TestTargetKind.CanMessage,
                    default, 1, new string('a', 64), BusRole.DutCan, 0x123))
        };
        var error = Assert.Throws<FormatException>(() =>
            TestDocumentCodec.Deserialize(TestDocumentCodec.Serialize(doc)));
        Assert.Contains("绿通.丢失的DBC报文", error.Message);
    }

    [Fact]
    public void EnabledStepScope_ExcludesDisabledSubtreeAndUnusedHardwarePermissions()
    {
        var active = new TestTargetMapping("ECU_CAN", TestTargetKind.RawCan,
            default, 0, "", BusRole.DutCan, 0x123);
        var unused = new TestTargetMapping("KEY_UNUSED", TestTargetKind.RawKey,
            default, 0, "", BusRole.KeypadCan);
        var disabled = StepNode.Create("Sequence").WithProperty("enabled", "false")
            .WithSlot("body", StepNode.Create("Write")
                .WithProperty("target", unused.Name)
                .WithProperty("value", "Hold:0x00001:1000"));
        var enabled = StepNode.Create("Write")
            .WithProperty("target", active.Name).WithProperty("value", "HEX:0102");
        var doc = TestDocument.Create("Scope") with
        {
            Mappings = ImmutableArray.Create(active, unused)
        };
        doc = doc with { Phases = doc.Phases.SetItem(TestPhase.Test,
            ImmutableArray.Create(disabled, enabled)) };
        Assert.Single(TestRunStepScope.RequiredMappings(doc));
        Assert.Equal(active.Name, TestRunStepScope.RequiredMappings(doc)[0].Name);
        Assert.True(TestRunStepScope.RequiresPhysicalWrite(doc));
        var withoutActive = doc with
        {
            Phases = doc.Phases.SetItem(TestPhase.Test, ImmutableArray.Create(disabled))
        };
        Assert.False(TestRunStepScope.RequiresPhysicalWrite(withoutActive));
    }

    [Fact]
    public void RealPreflight_RejectsShortCanCycleAndAccidentalKeypadDbcTransmit()
    {
        var raw = new TestTargetMapping("CAN0_ECU", TestTargetKind.RawCan,
            default, 0, "", BusRole.DutCan, 0x701);
        var dbcWrongBus = new TestTargetMapping("DBC_TO_KEYPAD", TestTargetKind.CanMessage,
            AssetId.New(), 1, new string('a', 64), BusRole.KeypadCan, 0x123);
        var doc = TestDocument.Create("InvalidOperation") with
        {
            Mappings = ImmutableArray.Create(raw, dbcWrongBus)
        };
        doc = doc with
        {
            Phases = doc.Phases.SetItem(TestPhase.Test, ImmutableArray.Create(
                StepNode.Create("Write").WithProperty("target", raw.Name)
                    .WithProperty("value", "HEX:01").WithProperty("periodMs", "15"),
                StepNode.Create("Write").WithProperty("target", dbcWrongBus.Name)
                    .WithProperty("value", "HEX:0000000000000000")))
        };
        var plan = new PlanCompiler().Prepare(doc, allowRealOperations: true);
        Assert.Contains(plan.Diagnostics, issue => issue.Contains("CAN 周期", StringComparison.Ordinal));
        Assert.Contains(plan.Diagnostics, issue => issue.Contains("ECU CAN", StringComparison.Ordinal));
    }
}
