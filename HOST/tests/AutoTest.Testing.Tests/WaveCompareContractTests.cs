using System.Collections.Immutable;
using AutoTest.Testing.Authoring;
using AutoTest.Testing.Preparation;
using Xunit;

namespace AutoTest.Testing.Tests;

/// <summary>Pure contract checks: no fake USB device, waveform or DUT result.</summary>
public sealed class WaveCompareContractTests
{
    private static TestDocument Document(string tolerance, string coverage,
        string reference = "ApprovedReference")
    {
        var node = StepNode.Create("WaveCompare", "实际波形判定")
            .WithProperty("captureKey", "capture1")
            .WithProperty("variable", "waveVerdict")
            .WithProperty("tolerance", tolerance)
            .WithProperty("coverage", coverage)
            .WithProperty("profile", "")
            .WithProperty("reference", reference);
        var document = TestDocument.Create("WaveContract");
        return document with
        {
            Phases = document.Phases.SetItem(TestPhase.Test,
                ImmutableArray.Create(node))
        };
    }

    [Fact]
    public void WaveCompare_WithReferenceAndValidCriteria_IsAuthorable()
    {
        var errors = StepTreeValidator.ValidateTree(Document("0.05", "0.95"),
            StepDescriptorCatalog.Builtins());
        Assert.Empty(errors);
    }

    [Fact]
    public void WaveCompare_CompilesInTheRealExecutorOnly()
    {
        var doc = Document("0.05", "0.95");
        Assert.Empty(new PlanCompiler().Prepare(doc, allowRealOperations: true).Diagnostics);
        Assert.Contains(new PlanCompiler().Prepare(doc).Diagnostics,
            error => error.Contains("not executable in V1", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("-1", "0.95", "waveform absolute tolerance")]
    [InlineData("NaN", "0.95", "waveform absolute tolerance")]
    [InlineData("0.05", "1.5", "waveform coverage")]
    [InlineData("0.05", "-0.1", "waveform coverage")]
    public void WaveCompare_RejectsUnsafeCriteria(
        string tolerance, string coverage, string fragment)
    {
        var errors = StepTreeValidator.ValidateTree(Document(tolerance, coverage),
            StepDescriptorCatalog.Builtins());
        Assert.Contains(errors, error => error.Contains(fragment, StringComparison.Ordinal));
    }

    [Fact]
    public void WaveCompare_RequiresNamedOrMappedReference()
    {
        var errors = StepTreeValidator.ValidateTree(Document("0.05", "0.95", ""),
            StepDescriptorCatalog.Builtins());
        Assert.Contains(errors, error => error.Contains("named reference", StringComparison.Ordinal));
    }
}
