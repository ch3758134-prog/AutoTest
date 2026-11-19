using System.Collections.Immutable;
using AutoTest.Testing.Authoring;
using Xunit;

namespace AutoTest.Testing.Tests;

public sealed class PublicFlowCodecTests
{
    [Fact]
    public void RoundTripHasOneContinuousTreeAndNoProductionPhases()
    {
        var wait = StepNode.Create("Wait", "保持 300 毫秒")
            .WithProperty("milliseconds", "300");
        var branch = StepNode.Create("If", "条件")
            .WithProperty("variable", "state")
            .WithProperty("equals", "1")
            .WithSlot("then", wait)
            .WithSlot("else");
        var flow = PublicFlowDocument.Create("公共准备流程") with
        {
            Body = ImmutableArray.Create(branch),
            Parameters = ImmutableArray.Create(
                new PublicFlowParameter("state", "string", true))
        };
        var json = PublicFlowCodec.Serialize(flow);
        Assert.DoesNotContain("phases", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("prepare", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cleanup", json, StringComparison.OrdinalIgnoreCase);
        var read = PublicFlowCodec.Deserialize(json);
        Assert.Equal(flow.FlowId, read.FlowId);
        Assert.Equal(flow.Name, read.Name);
        Assert.Equal(flow.Walk().Select(x => x.Id), read.Walk().Select(x => x.Id));
        Assert.Equal("300", read.Body[0].Slots["then"][0].Properties["milliseconds"]);
        Assert.Equal("state", Assert.Single(read.Parameters).Name);
    }

    [Fact]
    public void CopyGivesNewFlowIdentityAndNewNestedStepIds()
    {
        var flow = PublicFlowDocument.Create("来源") with
        {
            Body = ImmutableArray.Create(StepNode.Create("Sequence")
                .WithSlot("body", StepNode.Create("Wait")
                    .WithProperty("milliseconds", "1")))
        };
        var copy = flow.CopyAs("副本");
        Assert.NotEqual(flow.FlowId, copy.FlowId);
        Assert.NotEqual(flow.Walk().Select(n => n.Id), copy.Walk().Select(n => n.Id));
        Assert.Equal(flow.Walk().Select(n => n.Type), copy.Walk().Select(n => n.Type));
    }

    [Fact]
    public void RejectInvalidPinsRatherThanInventingMissingRevision()
    {
        var flow = PublicFlowDocument.Create("固定引用") with
        {
            References = ImmutableArray.Create(
                new PublicFlowReference(Guid.NewGuid(), 1, "NOT_SHA256"))
        };
        Assert.Throws<FormatException>(() => PublicFlowCodec.Serialize(flow));
    }

    [Fact]
    public void LegacyTestCodecRemainsThreePhaseAndIndependent()
    {
        var test = TestDocument.Create("test");
        var text = TestDocumentCodec.Serialize(test);
        Assert.Contains("Phases", text, StringComparison.Ordinal);
        Assert.Equal(test.Id, TestDocumentCodec.Deserialize(text).Id);
    }
}
