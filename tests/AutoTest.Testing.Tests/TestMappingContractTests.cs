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
}
