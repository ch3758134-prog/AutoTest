using AutoTest.Application.TestAuthoring;
using AutoTest.Testing.Authoring;
using Xunit;

namespace AutoTest.Application.Tests;

public sealed class TestAuthoringUseCasesTests
{
    [Fact]
    public void CreatesActionSpecificDraftsFromTheCanonicalTestSchema()
    {
        var useCases = new TestAuthoringUseCases();

        var can = useCases.CreateStepDraft("can");
        var key = useCases.CreateStepDraft("key");
        var branch = useCases.CreateStepDraft("if");

        Assert.Equal("Write", can.Type);
        Assert.Equal("HEX:0000000000000000", can.Properties["value"]);
        Assert.Equal("Hold:0x00000:1000", key.Properties["value"]);
        Assert.Equal("==", branch.Properties["operator"]);
        Assert.Contains("then", branch.Slots.Keys);
        Assert.Contains("else", branch.Slots.Keys);
    }

    [Fact]
    public void DoesNotExposeUnimplementedOrRemovedNodesForEditing()
    {
        var useCases = new TestAuthoringUseCases();

        Assert.True(useCases.SupportsEditing("Write"));
        Assert.True(useCases.SupportsEditing("WaveCompare"));
        Assert.False(useCases.SupportsEditing("When"));
        Assert.False(useCases.SupportsEditing("TryCatch"));
        Assert.False(useCases.SupportsEditing("Flash"));
        Assert.False(useCases.SupportsEditing("CanSend"));
    }

    [Fact]
    public void ExposesOnlyVariablesProvenAvailableBeforeTheSelectedStep()
    {
        var useCases = new TestAuthoringUseCases();
        var document = TestDocument.Create("Variable flow");
        var tx = new StepEditTransaction(document);
        tx.Insert(TestPhase.Test, null, null, 0,
            StepNode.Create("SetVariable").WithProperty("variable", "brakeState")
                .WithProperty("value", "1"));
        var selected = StepNode.Create("If").WithProperty("variable", "brakeState")
            .WithProperty("operator", "==").WithProperty("equals", "1")
            .WithSlot("then").WithSlot("else");
        tx.Insert(TestPhase.Test, null, null, 1, selected);

        Assert.Contains(useCases.AvailableVariables(tx.Current, selected.Id),
            variable => variable.Name == "brakeState");
    }
}