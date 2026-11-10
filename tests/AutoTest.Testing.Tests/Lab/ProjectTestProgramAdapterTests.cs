using AutoTest.Core;
using AutoTest.Testing.Authoring;
using AutoTest.Testing.Lab;
using Xunit;

namespace AutoTest.Testing.Tests.Lab;

public sealed class ProjectTestProgramAdapterTests
{
    [Fact]
    public void ProjectTestProgramPreservesAllStepIdsPhasesPropertiesAndSlots()
    {
        var document = TestDocument.Create("Real project test source");
        var branch = StepNode.Create("Case", "ready")
            .WithProperty("equals", "ready")
            .WithSlot("body", StepNode.Create("Assert", "assert")
                .WithProperty("variable", "voltage")
                .WithProperty("equals", "12"));
        var switchNode = StepNode.Create("Switch", "choice")
            .WithProperty("variable", "mode").WithSlot("cases", branch);
        var editor = new StepEditTransaction(document);
        editor.Insert(TestPhase.Prepare, null, null, 0,
            StepNode.Create("SetVariable", "setup")
                .WithProperty("variable", "mode").WithProperty("value", "ready"));
        editor.Insert(TestPhase.Test, null, null, 0, switchNode);
        editor.Insert(TestPhase.Cleanup, null, null, 0,
            StepNode.Create("Wait", "cleanup").WithProperty("milliseconds", "0"));
        var source = editor.Current;
        var converted = ProjectTestProgramAdapter.ToLabDocument(source);
        Assert.Equal(source.Id.Value, converted.Id);
        Assert.Equal(source.Name, converted.Name);
        Assert.Equal(source.Walk().Select(x => x.Id.Value),
            converted.Walk().Select(x => x.Id));
        Assert.Equal("ready", converted.Phases[LabPhase.Test][0]
            .Slots["cases"][0].Parameters["equals"]);
        Assert.Equal(1, converted.Phases[LabPhase.Prepare].Count);
        Assert.Equal(1, converted.Phases[LabPhase.Cleanup].Count);
        var plan = ProjectTestProgramAdapter.Prepare(source);
        Assert.True(plan.Ready, string.Join(";", plan.Diagnostics));
        Assert.Equal(converted.Id, plan.Restore().Id);
    }

    [Fact]
    public void UnsupportedHardwareNodeFailsClosedRatherThanDisappearing()
    {
        var tx = new StepEditTransaction(TestDocument.Create("physical test"));
        tx.Insert(TestPhase.Test, null, null, 0,
            StepNode.Create("Flash", "unapproved ECU flash"));
        var error = Assert.Throws<NotSupportedException>(
            () => ProjectTestProgramAdapter.Prepare(tx.Current));
        Assert.Contains("PROJECT_TEST_NODE_UNSUPPORTED", error.Message);
    }

    [Fact]
    public async Task PhysicalRunStillRequiresAuthorizedExecutionAdapter()
    {
        var tx = new StepEditTransaction(TestDocument.Create("physical run"));
        tx.Insert(TestPhase.Test, null, null, 0,
            StepNode.Create("Wait").WithProperty("milliseconds", "0"));
        var plan = ProjectTestProgramAdapter.Prepare(tx.Current);
        Assert.True(plan.Ready);
        var runner = new LabRunCoordinator();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync(plan, new SimulatedLabStation(), LabSource.Physical));
        Assert.Contains("PHYSICAL_TEST_EXECUTION_DENIED", error.Message);
    }
}
