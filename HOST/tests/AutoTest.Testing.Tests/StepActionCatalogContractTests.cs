using AutoTest.Testing.Authoring;
using Xunit;

namespace AutoTest.Testing.Tests;

public sealed class StepActionCatalogContractTests
{
    [Fact]
    public void EveryPaletteActionHasOneExecutableNodeDefinition()
    {
        var catalog = StepDescriptorCatalog.Builtins();

        Assert.Equal(catalog.Actions.Count, catalog.Actions.Select(x => x.Key)
            .Distinct(StringComparer.Ordinal).Count());

        foreach (var action in catalog.Actions)
        {
            var descriptor = Assert.IsType<StepDescriptor>(catalog.Lookup(action.StepType));
            Assert.NotEqual(StepExecutionCapability.Unsupported, descriptor.Execution);
        }

        Assert.DoesNotContain(catalog.Actions, x => x.StepType is "Flash" or "UdsRequest" or
            "CallPublicFlow" or "TryCatch" or "When");
    }

    [Fact]
    public void SharedWriteNodeUsesActionSpecificDefaults()
    {
        var catalog = StepDescriptorCatalog.Builtins();

        var can = catalog.CreateNodeForAction("can");
        var key = catalog.CreateNodeForAction("key");
        var ign = catalog.CreateNodeForAction("ign");

        Assert.Equal("HEX:0000000000000000", can.Properties["value"]);
        Assert.Equal("0", can.Properties["periodMs"]);
        Assert.Equal("Hold:0x00000:1000", key.Properties["value"]);
        Assert.Equal("ON", ign.Properties["value"]);
        Assert.Equal("target", catalog.FindAction("can")!.TargetProperty);
    }

    [Fact]
    public void ContextualCaseIsNotOfferedAsAGlobalAction()
    {
        var catalog = StepDescriptorCatalog.Builtins();
        var caseAction = Assert.Single(catalog.Actions.Where(x => x.Key == "case"));

        Assert.True(caseAction.ContextualOnly);
        Assert.Equal("Case", caseAction.StepType);
    }

    [Fact]
    public void EveryNewActionCreatesItsRequiredSlotsAndParameterDefaults()
    {
        var catalog = StepDescriptorCatalog.Builtins();

        foreach (var action in catalog.Actions)
        {
            var descriptor = catalog.Lookup(action.StepType)!;
            var node = catalog.CreateNodeForAction(action.Key);

            Assert.All(descriptor.RequiredSlots, slot => Assert.True(node.Slots.ContainsKey(slot)));
            Assert.All(descriptor.Parameters,
                parameter => Assert.True(node.Properties.ContainsKey(parameter.Key)));
        }
    }
}