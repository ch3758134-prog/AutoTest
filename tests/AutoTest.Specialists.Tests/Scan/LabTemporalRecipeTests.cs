using System;
using System.Linq;
using AutoTest.Specialists.Scan.Lab;
using Xunit;

namespace AutoTest.Specialists.Tests.Scan;

public sealed class LabTemporalRecipeTests
{
    [Fact]
    public void DisjointPublishedRecipeRevisionsSelectOnlyOneAtTime()
    {
        var c = LabDemoCatalog.Create();
        var boundary = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var first = c.Recipes[0] with { Revision = "old", EffectiveUntilUtc = boundary.ToString("O") };
        var second = c.Recipes[0] with { Revision = "new", EffectiveFromUtc = boundary.ToString("O") };
        var catalog = c with {
            Recipes = new[] { first, second },
            Samples = Array.Empty<LabRuleSample>()
        };
        Assert.Empty(LabCatalogValidator.Validate(catalog, true));
        var before = LabIdentityMatcher.Match(catalog, "M000001", catalog.Revision, boundary.AddSeconds(-1));
        var after = LabIdentityMatcher.Match(catalog, "M000001", catalog.Revision, boundary);
        Assert.Equal("old", before.Recipe!.Revision);
        Assert.Equal("new", after.Recipe!.Revision);
    }

    [Fact]
    public void OverlappingEffectivePeriodsCannotBePublished()
    {
        var c = LabDemoCatalog.Create();
        var boundary = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var a = c.Recipes[0] with { Revision = "v1", EffectiveUntilUtc = boundary.AddHours(1).ToString("O") };
        var b = c.Recipes[0] with { Revision = "v2", EffectiveFromUtc = boundary.ToString("O") };
        var catalog = c with { Recipes = new[] { a, b }, Samples = Array.Empty<LabRuleSample>() };
        Assert.Contains(LabCatalogValidator.Validate(catalog, true),
            x => x.StartsWith("RECIPE_CONDITION_OVERLAP", StringComparison.Ordinal));
    }

    [Fact]
    public void TemporalGapReportsNoRecipeNotFalseMatch()
    {
        var c = LabDemoCatalog.Create();
        var boundary = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var catalog = c with {
            Recipes = new[] { c.Recipes[0] with { EffectiveFromUtc = boundary.ToString("O") } },
            Samples = Array.Empty<LabRuleSample>()
        };
        var result = LabIdentityMatcher.Match(catalog, "M000001", catalog.Revision,
            boundary.AddDays(-1));
        Assert.Equal(LabMatchStatus.NoMatch, result.Status);
        Assert.Equal("RECIPE_NO_MATCH", result.Code);
    }
}
