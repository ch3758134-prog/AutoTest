using System.Collections.Immutable;
using AutoTest.Core;
using AutoTest.Testing.Authoring;
using AutoTest.Testing.Preparation;
using Xunit;

namespace AutoTest.Testing.Tests;

/// <summary>Authoring contracts only, no simulated DUT or USB acceptance.</summary>
public sealed class WaveCompareContractTests
{
    private static TestDocument Document(bool mapped, string profile = "ApprovedWave")
    {
        var node = StepNode.Create("WaveCompare", "真实波形判定")
            .WithProperty("captureKey", "capture1")
            .WithProperty("variable", "waveVerdict")
            .WithProperty("profile", mapped ? profile : "")
            .WithProperty("reference", "ApprovedReference");
        var original = TestDocument.Create("WaveContract");
        var result = original with
        {
            Phases = original.Phases.SetItem(TestPhase.Test,
                ImmutableArray.Create(node))
        };
        return mapped ? result with
        {
            Mappings = ImmutableArray.Create(new TestTargetMapping(
                profile, TestTargetKind.WaveProfile, AssetId.New(),
                1, new string('a', 64)))
        } : result;
    }

    [Fact]
    public void ReviewedWaveProfile_IsRequiredForRealAutomaticTest()
    {
        Assert.Empty(StepTreeValidator.ValidateTree(Document(true),
            StepDescriptorCatalog.Builtins()));
        var legacy = StepTreeValidator.ValidateTree(Document(false),
            StepDescriptorCatalog.Builtins());
        Assert.Contains(legacy, error => error.Contains(
            "requires an explicitly reviewed and mapped WAVE profile",
            StringComparison.Ordinal));
    }

    [Fact]
    public void WaveCompare_CompilesInRealExecutorOnlyWithMappedProfile()
    {
        var doc = Document(true);
        Assert.Empty(new PlanCompiler().Prepare(doc, allowRealOperations: true).Diagnostics);
        Assert.Contains(new PlanCompiler().Prepare(doc).Diagnostics,
            error => error.Contains("not executable in V1", StringComparison.Ordinal));
    }

    [Fact]
    public void WaveCompare_RejectsUnknownProfileWithoutSilentlyUsingReferenceName()
    {
        var doc = Document(false);
        var step = doc.Phases[TestPhase.Test][0]
            .WithProperty("profile", "NotMapped");
        doc = doc with
        {
            Phases = doc.Phases.SetItem(TestPhase.Test,
                ImmutableArray.Create(step))
        };
        var errors = StepTreeValidator.ValidateTree(doc,
            StepDescriptorCatalog.Builtins());
        Assert.Contains(errors,
            error => error.Contains("unmapped wave analysis profile", StringComparison.Ordinal));
    }

    [Fact]
    public void LegacyTolerancePropertiesCannotReplaceReviewedProfile()
    {
        var doc = Document(false);
        var node = doc.Phases[TestPhase.Test][0]
            .WithProperty("tolerance", "0.05")
            .WithProperty("coverage", "0.95");
        doc = doc with
        {
            Phases = doc.Phases.SetItem(TestPhase.Test,
                ImmutableArray.Create(node))
        };
        Assert.NotEmpty(StepTreeValidator.ValidateTree(doc,
            StepDescriptorCatalog.Builtins()));
    }
}
