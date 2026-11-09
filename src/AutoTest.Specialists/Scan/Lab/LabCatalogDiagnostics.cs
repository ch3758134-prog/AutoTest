using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace AutoTest.Specialists.Scan.Lab;

// Diagnostic projection over the EXISTING LabIdentityMatcher and catalog.
// Neither replay nor overlap inspection authorizes a production run or publication.
public sealed record LabSampleReplayRow(
    int RowNumber, string RawBarcode, string ExpectedRuleId, string ActualRuleId,
    string ExpectedRecipeId, string ActualRecipeId, string Code, bool Passed,
    IReadOnlyList<string> CandidateIds);

public sealed record LabSampleReplayReport(
    string CatalogRevision, DateTimeOffset DefaultObservedAtUtc,
    int TotalSamples, int ExaminedSamples, bool Truncated,
    int Passed, int Failed, IReadOnlyList<LabSampleReplayRow> Rows)
{
    public bool AllPassed => TotalSamples > 0 && !Truncated && Failed == 0 &&
                             ExaminedSamples == TotalSamples;
}

public sealed record LabRuleOverlapFinding(
    string FirstRuleId, string SecondRuleId, string Classification,
    int CollidingSamples, string Evidence);

public sealed record LabRuleOverlapReport(
    int EnabledRules, int ExaminedSamples, int TotalSamples, bool Incomplete,
    IReadOnlyList<LabRuleOverlapFinding> Findings)
{
    // A finite sample can prove a collision, but absence of collisions never proves disjointness.
    public bool HasConfirmedConflict => Findings.Any(x =>
        x.Classification is "IDENTICAL_REGEX" or "SAMPLE_COLLISION");
}

public sealed record LabCatalogChange(
    string EntityType, string EntityKey, string Kind, string ChangedFields);

public sealed record LabCatalogComparison(
    string PublishedRevision, string DraftRevision, IReadOnlyList<LabCatalogChange> Changes);

public static class LabCatalogDiagnostics
{
    public const int MaxSamplesPerReplay = 500;
    public const int MaxRulesPerOverlap = 64;
    private static readonly TimeSpan RegexLimit = TimeSpan.FromMilliseconds(50);

    public static LabSampleReplayReport ReplaySamples(LabCatalog snapshot,
        DateTimeOffset defaultObservedAtUtc, int maxSamples = MaxSamplesPerReplay)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (maxSamples is < 1 or > MaxSamplesPerReplay)
            throw new ArgumentOutOfRangeException(nameof(maxSamples));
        var rules = (snapshot.IdentityRules ?? Array.Empty<LabIdentityRule>())
            .Where(x => x.Enabled).ToArray();
        var recipes = (snapshot.Recipes ?? Array.Empty<LabRecipe>())
            .Where(x => x.Enabled).ToArray();
        var samples = snapshot.Samples ?? Array.Empty<LabRuleSample>();
        var rows = new List<LabSampleReplayRow>();
        var defaultTime = defaultObservedAtUtc.ToUniversalTime();
        for (var i = 0; i < Math.Min(samples.Count, maxSamples); i++)
        {
            var sample = samples[i];
            if (!LabCatalogValidator.TryParseDate(sample.ObservedAtUtc, out var observedAt))
            {
                rows.Add(new(i + 1, sample.RawBarcode, sample.ExpectedRuleId, "",
                    sample.ExpectedRecipeId, "", "SAMPLE_TIME_INVALID", false, Array.Empty<string>()));
                continue;
            }
            var result = LabIdentityMatcher.EvaluateRules(
                rules, sample.RawBarcode, snapshot.Revision);
            if (result.Status != LabMatchStatus.Matched || result.Identity is null)
            {
                rows.Add(new(i + 1, sample.RawBarcode, sample.ExpectedRuleId, "",
                    sample.ExpectedRecipeId, "", result.Code, false, result.CandidateIds));
                continue;
            }
            // Use the same matching/time window code as the actual production path.
            var candidates = LabIdentityMatcher.FilterRecipes(
                recipes, result.Identity, observedAt ?? defaultTime);
            var selected = candidates.Length == 1 ? candidates[0] : null;
            var actualRule = result.Identity.RuleId;
            var actualRecipe = selected?.RecipeId ?? "";
            var code = !string.Equals(actualRule, sample.ExpectedRuleId, StringComparison.Ordinal)
                ? "SAMPLE_RULE_MISMATCH"
                : candidates.Length == 0 ? "RECIPE_NO_MATCH"
                : candidates.Length > 1 ? "RECIPE_AMBIGUOUS"
                : !string.Equals(actualRecipe, sample.ExpectedRecipeId, StringComparison.Ordinal)
                    ? "SAMPLE_RECIPE_MISMATCH" : "SAMPLE_PASSED";
            rows.Add(new(i + 1, sample.RawBarcode, sample.ExpectedRuleId, actualRule,
                sample.ExpectedRecipeId, actualRecipe, code, code == "SAMPLE_PASSED",
                candidates.Select(x => x.RecipeId + "@" + x.Revision).ToArray()));
        }
        return new(snapshot.Revision, defaultTime, samples.Count, rows.Count,
            samples.Count > maxSamples, rows.Count(x => x.Passed),
            rows.Count(x => !x.Passed), rows);
    }

    public static LabRuleOverlapReport InspectRuleOverlap(LabCatalog snapshot,
        int maxSamples = MaxSamplesPerReplay)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (maxSamples is < 1 or > MaxSamplesPerReplay)
            throw new ArgumentOutOfRangeException(nameof(maxSamples));
        var allRules = (snapshot.IdentityRules ?? Array.Empty<LabIdentityRule>())
            .Where(x => x.Enabled).ToArray();
        var samples = snapshot.Samples ?? Array.Empty<LabRuleSample>();
        var limited = allRules.Take(MaxRulesPerOverlap).ToArray();
        var findings = new List<LabRuleOverlapFinding>();
        var validSampleTexts = samples.Take(maxSamples)
            .Select(x => x.RawBarcode.TrimEnd('\r', '\n'))
            .Where(x => x.Length is > 0 and <= 4096 && x.IndexOf('\0') < 0)
            .ToArray();
        var compiled = new Regex?[limited.Length];
        for (var i = 0; i < limited.Length; i++)
        {
            try
            {
                compiled[i] = new Regex(limited[i].Pattern,
                    RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, RegexLimit);
            }
            catch (ArgumentException)
            {
                // Existing publication validator will also report invalid regex.
            }
        }

        for (var i = 0; i < limited.Length; i++)
        {
            for (var j = i + 1; j < limited.Length; j++)
            {
                var count = 0;
                var regexError = compiled[i] is null || compiled[j] is null;
                if (!regexError)
                {
                    foreach (var raw in validSampleTexts)
                    {
                        try
                        {
                            var a = compiled[i]!.Match(raw);
                            if (!a.Success || a.Index != 0 || a.Length != raw.Length) continue;
                            var b = compiled[j]!.Match(raw);
                            if (b.Success && b.Index == 0 && b.Length == raw.Length) count++;
                        }
                        catch (RegexMatchTimeoutException)
                        {
                            regexError = true;
                            break;
                        }
                    }
                }
                var samePattern = string.Equals(limited[i].Pattern, limited[j].Pattern,
                    StringComparison.Ordinal);
                var classification = regexError ? "REGEX_INVALID_OR_TIMEOUT"
                    : samePattern ? "IDENTICAL_REGEX"
                    : count > 0 ? "SAMPLE_COLLISION"
                    : "UNPROVEN_DISJOINTNESS";
                var evidence = regexError ? "正则编译或限时匹配失败，不可证明安全"
                    : samePattern ? "相同表达式必然存在交集（即使当前样本为空）"
                    : count > 0 ? "已在当前样本中观察到双重匹配"
                    : "有限样本未发现双重匹配；不能据此证明规则不重叠";
                findings.Add(new(limited[i].RuleId, limited[j].RuleId,
                    classification, count, evidence));
            }
        }
        return new(allRules.Length, validSampleTexts.Length, samples.Count,
            allRules.Length > MaxRulesPerOverlap || samples.Count > maxSamples ||
            validSampleTexts.Length < Math.Min(samples.Count, maxSamples), findings);
    }

    public static LabCatalogComparison Compare(LabCatalog published, LabCatalog draft)
    {
        ArgumentNullException.ThrowIfNull(published);
        ArgumentNullException.ThrowIfNull(draft);
        var changes = new List<LabCatalogChange>();
        if (!string.Equals(published.Revision, draft.Revision, StringComparison.Ordinal))
            changes.Add(new("Catalog", "Revision", "Changed", "Revision"));
        if (published.Published != draft.Published)
            changes.Add(new("Catalog", "Publication", "Changed", "Published"));
        CompareRules(published, draft, changes);
        CompareRecipes(published, draft, changes);
        if (!(published.Samples ?? Array.Empty<LabRuleSample>())
            .SequenceEqual(draft.Samples ?? Array.Empty<LabRuleSample>()))
            changes.Add(new("Samples", "All", "Changed",
                $"Count: {(published.Samples?.Count ?? 0)} → {(draft.Samples?.Count ?? 0)}; 样本文本未在比较摘要中显示"));
        return new(published.Revision, draft.Revision, changes);
    }

    private static void CompareRules(LabCatalog left, LabCatalog right, List<LabCatalogChange> changes)
    {
        var before = (left.IdentityRules ?? Array.Empty<LabIdentityRule>())
            .GroupBy(x => x.RuleId, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
        var after = (right.IdentityRules ?? Array.Empty<LabIdentityRule>())
            .GroupBy(x => x.RuleId, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
        foreach (var key in before.Keys.Union(after.Keys, StringComparer.Ordinal)
                     .OrderBy(x => x, StringComparer.Ordinal))
        {
            if (!before.TryGetValue(key, out var old))
            { changes.Add(new("Rule", key, "Added", "RuleId")); continue; }
            if (!after.TryGetValue(key, out var now))
            { changes.Add(new("Rule", key, "Removed", "RuleId")); continue; }
            var fields = new List<string>();
            if (old.Pattern != now.Pattern) fields.Add("Pattern");
            if (old.IdentityFieldsCsv != now.IdentityFieldsCsv) fields.Add("IdentityFieldsCsv");
            if (old.Enabled != now.Enabled) fields.Add("Enabled");
            if (old.OverlapReviewId != now.OverlapReviewId) fields.Add("OverlapReviewId");
            if (fields.Count > 0) changes.Add(new("Rule", key, "Changed", string.Join(", ", fields)));
        }
    }

    private static void CompareRecipes(LabCatalog left, LabCatalog right, List<LabCatalogChange> changes)
    {
        static string Key(LabRecipe x) => x.RecipeId + "@" + x.Revision;
        var before = (left.Recipes ?? Array.Empty<LabRecipe>())
            .GroupBy(Key, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
        var after = (right.Recipes ?? Array.Empty<LabRecipe>())
            .GroupBy(Key, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
        foreach (var key in before.Keys.Union(after.Keys, StringComparer.Ordinal)
                     .OrderBy(x => x, StringComparer.Ordinal))
        {
            if (!before.TryGetValue(key, out var old))
            { changes.Add(new("Recipe", key, "Added", "RecipeId/Revision")); continue; }
            if (!after.TryGetValue(key, out var now))
            { changes.Add(new("Recipe", key, "Removed", "RecipeId/Revision")); continue; }
            var fields = new List<string>();
            if (old.ModelField != now.ModelField) fields.Add("ModelField");
            if (old.CustomerField != now.CustomerField) fields.Add("CustomerField");
            if (old.TestProgram != now.TestProgram) fields.Add("TestProgram");
            if (old.StationProfile != now.StationProfile) fields.Add("StationProfile");
            if (old.FlashImage != now.FlashImage) fields.Add("FlashImage");
            if (old.RequiresFlash != now.RequiresFlash) fields.Add("RequiresFlash");
            if (old.Enabled != now.Enabled) fields.Add("Enabled");
            if (old.EffectiveFromUtc != now.EffectiveFromUtc) fields.Add("EffectiveFromUtc");
            if (old.EffectiveUntilUtc != now.EffectiveUntilUtc) fields.Add("EffectiveUntilUtc");
            if (fields.Count > 0) changes.Add(new("Recipe", key, "Changed", string.Join(", ", fields)));
        }
    }
}
