using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace AutoTest.Specialists.Scan.Lab;

public static class LabCatalogValidator
{
    private static readonly TimeSpan RegexLimit = TimeSpan.FromMilliseconds(50);

    public static IReadOnlyList<string> Validate(LabCatalog catalog, bool forPublication)
    {
        var errors = new List<string>();
        if (catalog is null) return new[] { "CATALOG_MISSING" };
        var rules = (catalog.IdentityRules ?? Array.Empty<LabIdentityRule>()).Where(x => x.Enabled).ToArray();
        var recipes = (catalog.Recipes ?? Array.Empty<LabRecipe>()).Where(x => x.Enabled).ToArray();
        if (rules.Length == 0) errors.Add("IDENTITY_RULES_EMPTY");
        if (recipes.Length == 0) errors.Add("RECIPES_EMPTY");
        foreach (var duplicate in rules.GroupBy(x => x.RuleId, StringComparer.Ordinal)
                     .Where(x => x.Count() > 1)) errors.Add("DUPLICATE_RULE_ID:" + duplicate.Key);
        foreach (var duplicate in recipes.GroupBy(x => x.RecipeId + ":" + x.Revision, StringComparer.Ordinal)
                     .Where(x => x.Count() > 1)) errors.Add("DUPLICATE_RECIPE_ID:" + duplicate.Key);
        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.RuleId) || string.IsNullOrWhiteSpace(rule.Pattern))
            { errors.Add("IDENTITY_RULE_EMPTY:" + rule.RuleId); continue; }
            var keys = SplitFields(rule.IdentityFieldsCsv);
            if (keys.Length == 0 || keys.Distinct(StringComparer.Ordinal).Count() != keys.Length)
                errors.Add("IDENTITY_KEYS_INVALID:" + rule.RuleId);
            try
            {
                var regex = new Regex(rule.Pattern, RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
                    RegexLimit);
                var groups = regex.GetGroupNames().Where(x => !int.TryParse(x, out _)).ToArray();
                foreach (var key in keys)
                    if (!groups.Contains(key, StringComparer.Ordinal))
                        errors.Add("IDENTITY_KEY_NOT_CAPTURED:" + rule.RuleId + ":" + key);
            }
            catch (ArgumentException) { errors.Add("IDENTITY_REGEX_INVALID:" + rule.RuleId); }
        }
        // General regex-language intersection cannot be proved by finite sample testing.
        // Distinct rules require an explicit scope review; known identical patterns always conflict.
        for (var i = 0; i < rules.Length; i++)
            for (var j = i + 1; j < rules.Length; j++)
            {
                if (StringComparer.Ordinal.Equals(rules[i].Pattern, rules[j].Pattern))
                    errors.Add("IDENTITY_PATTERN_DUPLICATE:" + rules[i].RuleId + ":" + rules[j].RuleId);
                else if (forPublication && (string.IsNullOrWhiteSpace(rules[i].OverlapReviewId) ||
                                             string.IsNullOrWhiteSpace(rules[j].OverlapReviewId)))
                    errors.Add("IDENTITY_OVERLAP_REVIEW_REQUIRED:" + rules[i].RuleId + ":" + rules[j].RuleId);
            }
        foreach (var recipe in recipes)
        {
            if (string.IsNullOrWhiteSpace(recipe.RecipeId) || string.IsNullOrWhiteSpace(recipe.Revision))
                errors.Add("RECIPE_ID_REVISION_MISSING:" + recipe.RecipeId);
            if (!ValidArtifact(recipe.TestProgram)) errors.Add("TEST_PROGRAM_UNRESOLVED:" + recipe.RecipeId);
            if (!ValidArtifact(recipe.StationProfile)) errors.Add("STATION_PROFILE_UNRESOLVED:" + recipe.RecipeId);
            if (recipe.RequiresFlash && !ValidArtifact(recipe.FlashImage))
                errors.Add("FLASH_IMAGE_UNRESOLVED:" + recipe.RecipeId);
            if (!TryParseDate(recipe.EffectiveFromUtc, out var from) ||
                !TryParseDate(recipe.EffectiveUntilUtc, out var until) ||
                (from is not null && until is not null && from >= until))
                errors.Add("RECIPE_TIME_WINDOW_INVALID:" + recipe.RecipeId);
        }
        for (var i = 0; i < recipes.Length; i++)
            for (var j = i + 1; j < recipes.Length; j++)
                if (PotentialOverlap(recipes[i], recipes[j]))
                    errors.Add("RECIPE_CONDITION_OVERLAP:" + recipes[i].RecipeId + ":" + recipes[j].RecipeId);
        foreach (var sample in catalog.Samples ?? Array.Empty<LabRuleSample>())
        {
            if (string.IsNullOrWhiteSpace(sample.RawBarcode)) { errors.Add("SAMPLE_EMPTY"); continue; }
            if (!TryParseDate(sample.ObservedAtUtc, out var observed))
            { errors.Add("SAMPLE_TIME_INVALID:" + sample.RawBarcode); continue; }
            var result = LabIdentityMatcher.EvaluateRules(rules, sample.RawBarcode, catalog.Revision);
            if (result.Status != LabMatchStatus.Matched ||
                !StringComparer.Ordinal.Equals(result.Identity?.RuleId, sample.ExpectedRuleId))
                errors.Add("SAMPLE_IDENTITY_MISMATCH:" + sample.RawBarcode);
            else
            {
                var candidates = LabIdentityMatcher.FilterRecipes(recipes, result.Identity!, observed);
                if (candidates.Length != 1 || !StringComparer.Ordinal.Equals(candidates[0].RecipeId, sample.ExpectedRecipeId))
                    errors.Add("SAMPLE_RECIPE_MISMATCH:" + sample.RawBarcode);
            }
        }
        return errors;
    }

    internal static string[] SplitFields(string? csv) =>
        (csv ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    internal static bool ValidArtifact(LabArtifact? asset) =>
        asset is not null && !string.IsNullOrWhiteSpace(asset.Key) &&
        !string.IsNullOrWhiteSpace(asset.Revision) && !string.IsNullOrWhiteSpace(asset.Sha256) &&
        asset.Resolved && asset.Source != LabOrigin.Unknown;

    internal static bool TryParseDate(string? text, out DateTimeOffset? date)
    {
        date = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var parsed)) return false;
        date = parsed.ToUniversalTime();
        return true;
    }

    private static bool PotentialOverlap(LabRecipe a, LabRecipe b)
    {
        if (!TryParseDate(a.EffectiveFromUtc, out var aFrom) ||
            !TryParseDate(a.EffectiveUntilUtc, out var aUntil) ||
            !TryParseDate(b.EffectiveFromUtc, out var bFrom) ||
            !TryParseDate(b.EffectiveUntilUtc, out var bUntil)) return true; // fail closed
        return (string.IsNullOrWhiteSpace(a.ModelField) || string.IsNullOrWhiteSpace(b.ModelField) ||
            StringComparer.Ordinal.Equals(a.ModelField, b.ModelField)) &&
            (string.IsNullOrWhiteSpace(a.CustomerField) || string.IsNullOrWhiteSpace(b.CustomerField) ||
            StringComparer.Ordinal.Equals(a.CustomerField, b.CustomerField)) &&
            (aUntil ?? DateTimeOffset.MaxValue) > (bFrom ?? DateTimeOffset.MinValue) &&
            (bUntil ?? DateTimeOffset.MaxValue) > (aFrom ?? DateTimeOffset.MinValue);
    }
}
