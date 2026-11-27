using AutoTest.Testing.Authoring;
using Xunit;

namespace AutoTest.Testing.Tests;

public sealed class TestDocumentFormatContractTests
{
    [Fact]
    public void NewDocumentsUseTheCurrentNonLegacyFormat()
    {
        var document = TestDocument.Create("Current TEST");

        Assert.Equal(2, document.FormatVersion);
        Assert.Equal(TestDocument.CurrentFormat, document.FormatVersion);
    }

    [Fact]
    public void DisabledUnknownNodeCannotBeSavedAsAValidTestDocument()
    {
        var document = TestDocument.Create("Invalid disabled node");
        var invalid = StepNode.Create("CanSend").WithProperty("enabled", "false");
        document = document with
        {
            Phases = document.Phases.SetItem(TestPhase.Test,
                System.Collections.Immutable.ImmutableArray.Create(invalid))
        };

        Assert.Throws<InvalidDataException>(() => TestDocumentCodec.Serialize(document));
    }

    [Fact]
    public void LegacyEnableAliasesAreNotRecognizedAsExecutionState()
    {
        var node = StepNode.Create("Wait").WithProperty("legacy.enabled", "false");

        Assert.True(StepExecutionPolicy.IsEnabled(node));
        var document = TestDocument.Create("No legacy aliases") with
        {
            Phases = TestDocument.Create("No legacy aliases").Phases.SetItem(
                TestPhase.Test, System.Collections.Immutable.ImmutableArray.Create(node))
        };
        Assert.Contains(StepTreeValidator.ValidateTree(document,
            StepDescriptorCatalog.Builtins(), allowIncompleteSteps: true),
            issue => issue.Contains("legacy.enabled", StringComparison.Ordinal));
    }
}