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

}
