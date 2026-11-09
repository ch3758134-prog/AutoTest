using System;
using System.Linq;
using AutoTest.Specialists.Scan.Lab;
using Xunit;

namespace AutoTest.Specialists.Tests.Scan;

public sealed class LabCatalogDiagnosticsTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 9, 0, 0, 0, TimeSpan.Zero);
    private static LabCatalog Demo() => LabDemoCatalog.Create();

    [Fact]
    public void PublishedDemoCanBeReplayedAgainstDraftWithoutChangingProductionRevision()
    {
        var original = Demo();
        var draft = original with { Published = false };
        var result = LabCatalogDiagnostics.ReplaySamples(draft, At);
        Assert.True(result.AllPassed);
        Assert.Equal(1, result.Passed);
        Assert.Equal(0, result.Failed);
        Assert.Equal("M000001", result.Rows[0].RawBarcode);
        Assert.Equal("MOCK-RECIPE", result.Rows[0].ActualRecipeId);
        Assert.Equal(original.Revision, result.CatalogRevision);
        Assert.False(draft.Published);
    }

    [Fact]
    public void InvalidDraftRegexIsDiagnosedRatherThanBlockingDiagnosticReplay()
    {
        var catalog = Demo() with
        {
            Published = false,
            IdentityRules = new[] { new LabIdentityRule("BAD", "([", "serial", true) }
        };
        var result = LabCatalogDiagnostics.ReplaySamples(catalog, At);
        Assert.False(result.AllPassed);
        Assert.Equal("REGEX_INVALID", result.Rows[0].Code);
        Assert.Equal(1, result.Failed);
    }

    [Fact]
    public void ExplicitWrongExpectedRuleOrRecipeIsIdentified()
    {
        var c = Demo() with
        {
            Samples = new[]
            {
                new LabRuleSample("M000001", "WRONG-RULE", "MOCK-RECIPE"),
                new LabRuleSample("M000001", "MOCK-SERIAL", "WRONG-RECIPE")
            }
        };
        var result = LabCatalogDiagnostics.ReplaySamples(c, At);
        Assert.Equal(new[] { "SAMPLE_RULE_MISMATCH", "SAMPLE_RECIPE_MISMATCH" },
            result.Rows.Select(x => x.Code));
    }

    [Fact]
    public void BadSampleDateIsExplicitlyRejectedWithoutTimeDependentFallback()
    {
        var c = Demo() with
        {
            Samples = new[] { new LabRuleSample("M000001", "MOCK-SERIAL", "MOCK-RECIPE", "not-date") }
        };
        var result = LabCatalogDiagnostics.ReplaySamples(c, At);
        Assert.Equal("SAMPLE_TIME_INVALID", result.Rows[0].Code);
        Assert.False(result.AllPassed);
    }

    [Fact]
    public void ReplayUsesExistingTemporalRecipeMatchingAtEachSampleTimestamp()
    {
        var c = Demo();
        var boundary = At;
        var oldRecipe = c.Recipes[0] with { Revision = "old", EffectiveUntilUtc = boundary.ToString("O") };
        var newRecipe = c.Recipes[0] with { Revision = "new", EffectiveFromUtc = boundary.ToString("O") };
        var draft = c with
        {
            Recipes = new[] { oldRecipe, newRecipe },
            Samples = new[]
            {
                new LabRuleSample("M000001", "MOCK-SERIAL", "MOCK-RECIPE", boundary.AddMinutes(-1).ToString("O")),
                new LabRuleSample("M000002", "MOCK-SERIAL", "MOCK-RECIPE", boundary.ToString("O"))
            }
        };
        var result = LabCatalogDiagnostics.ReplaySamples(draft, At);
        Assert.True(result.AllPassed);
        Assert.Equal(2, result.Passed);
        Assert.Equal(1, result.Rows[0].CandidateIds.Count);
        Assert.Contains("old", result.Rows[0].CandidateIds[0]);
        Assert.Contains("new", result.Rows[1].CandidateIds[0]);
    }

    [Fact]
    public void TruncatedSampleCoverageNeverReportsCompletePass()
    {
        var c = Demo() with
        {
            Samples = Enumerable.Range(0, LabCatalogDiagnostics.MaxSamplesPerReplay + 1)
                .Select(_ => new LabRuleSample("M000001", "MOCK-SERIAL", "MOCK-RECIPE")).ToArray()
        };
        var result = LabCatalogDiagnostics.ReplaySamples(c, At);
        Assert.Equal(LabCatalogDiagnostics.MaxSamplesPerReplay, result.ExaminedSamples);
        Assert.True(result.Truncated);
        Assert.False(result.AllPassed);
        Assert.Equal(LabCatalogDiagnostics.MaxSamplesPerReplay + 1, result.TotalSamples);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LabCatalogDiagnostics.ReplaySamples(c, At, LabCatalogDiagnostics.MaxSamplesPerReplay + 1));
    }

    [Fact]
    public void DuplicatePatternsAreProvenOverlappingEvenWithNoSamples()
    {
        var c = Demo() with
        {
            Samples = Array.Empty<LabRuleSample>(),
            IdentityRules = new[]
            {
                new LabIdentityRule("A", @"(?<serial>M[0-9]{6})", "serial", true),
                new LabIdentityRule("B", @"(?<serial>M[0-9]{6})", "serial", true)
            }
        };
        var result = LabCatalogDiagnostics.InspectRuleOverlap(c);
        var finding = Assert.Single(result.Findings);
        Assert.Equal("IDENTICAL_REGEX", finding.Classification);
        Assert.True(result.HasConfirmedConflict);
    }

    [Fact]
    public void SampleCanProveTwoDifferentExpressionsCollide()
    {
        var c = Demo() with
        {
            IdentityRules = new[]
            {
                new LabIdentityRule("A", @"(?<serial>M[0-9]{6})", "serial", true),
                new LabIdentityRule("B", @"(?<serial>M[0-9]+)", "serial", true)
            }
        };
        var finding = Assert.Single(LabCatalogDiagnostics.InspectRuleOverlap(c).Findings);
        Assert.Equal("SAMPLE_COLLISION", finding.Classification);
        Assert.Equal(1, finding.CollidingSamples);
    }

    [Fact]
    public void NoCollisionInSamplesMustNeverBeReportedAsProofOfSafety()
    {
        var c = Demo() with
        {
            IdentityRules = new[]
            {
                new LabIdentityRule("A", @"(?<serial>M[0-9]{6})", "serial", true),
                new LabIdentityRule("B", @"(?<serial>Z[0-9]{6})", "serial", true)
            }
        };
        var result = LabCatalogDiagnostics.InspectRuleOverlap(c);
        Assert.Equal("UNPROVEN_DISJOINTNESS", Assert.Single(result.Findings).Classification);
        Assert.False(result.HasConfirmedConflict);
    }

    [Fact]
    public void RevisionComparisonIdentifiesRealFieldChangesWithoutExposingSampleBodies()
    {
        var c = Demo();
        var newRule = c.IdentityRules[0] with { Pattern = @"(?<serial>Z[0-9]{6})" };
        var newRecipe = c.Recipes[0] with { EffectiveUntilUtc = At.ToString("O") };
        var edited = c with
        {
            Published = false,
            IdentityRules = new[] { newRule },
            Recipes = new[] { newRecipe },
            Samples = new[] { new LabRuleSample("SECRET-BARCODE-1", "MOCK-SERIAL", "MOCK-RECIPE") }
        };
        var report = LabCatalogDiagnostics.Compare(c, edited);
        Assert.Contains(report.Changes, x => x.EntityType == "Rule" && x.ChangedFields.Contains("Pattern"));
        Assert.Contains(report.Changes, x => x.EntityType == "Recipe" && x.ChangedFields.Contains("EffectiveUntilUtc"));
        Assert.Contains(report.Changes, x => x.EntityType == "Samples");
        Assert.DoesNotContain("SECRET-BARCODE-1", string.Join(" ", report.Changes.Select(x => x.ChangedFields)));
    }

    [Fact]
    public void BatchRuleLimitMarksEvidenceIncomplete()
    {
        var c = Demo() with
        {
            IdentityRules = Enumerable.Range(0, LabCatalogDiagnostics.MaxRulesPerOverlap + 1)
                .Select(i => new LabIdentityRule("R" + i, @"(?<serial>M[0-9]{6})", "serial", true))
                .ToArray()
        };
        var report = LabCatalogDiagnostics.InspectRuleOverlap(c, 1);
        Assert.True(report.Incomplete);
        Assert.Equal(LabCatalogDiagnostics.MaxRulesPerOverlap + 1, report.EnabledRules);
    }
}
