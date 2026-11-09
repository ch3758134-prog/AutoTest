using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AutoTest.Specialists.Scan.Lab;

public static class LabIdentityMatcher
{
    public static LabMatch Match(LabCatalog catalog, string? raw, string expectedRevision,
        DateTimeOffset? observedAtUtc = null)
    {
        if (!catalog.Published)
            return Reject(LabMatchStatus.InvalidCatalog, "CATALOG_NOT_PUBLISHED") with { CatalogRevision = catalog.Revision };
        if (!StringComparer.Ordinal.Equals(catalog.Revision, expectedRevision))
            return Reject(LabMatchStatus.StaleCatalog, "CATALOG_REVISION_STALE") with { CatalogRevision = catalog.Revision };
        if (LabCatalogValidator.Validate(catalog, forPublication: true).Count != 0)
            return Reject(LabMatchStatus.InvalidCatalog, "CATALOG_INVALID") with { CatalogRevision = catalog.Revision };
        var result = EvaluateRules(catalog.IdentityRules.Where(x => x.Enabled).ToArray(), raw, catalog.Revision);
        if (result.Status != LabMatchStatus.Matched || result.Identity is null)
    return result with { CatalogRevision = catalog.Revision };
        var recipes = FilterRecipes(catalog.Recipes.Where(x => x.Enabled).ToArray(), result.Identity!, observedAtUtc);
        if (recipes.Length == 0) return Reject(LabMatchStatus.NoMatch, "RECIPE_NO_MATCH") with { CatalogRevision = catalog.Revision };
        if (recipes.Length > 1) return Reject(LabMatchStatus.Ambiguous, "RECIPE_AMBIGUOUS",
            recipes.Select(x => x.RecipeId).ToArray()) with { CatalogRevision = catalog.Revision };
        return result with { Recipe = recipes[0], CatalogRevision = catalog.Revision };
    }

    internal static LabMatch EvaluateRules(IReadOnlyList<LabIdentityRule> rules, string? raw,
        string revision)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Reject(LabMatchStatus.Empty, "SCAN_EMPTY");
        if (raw.Length > 4096 || raw.IndexOf('\0') >= 0)
            return Reject(LabMatchStatus.Invalid, "SCAN_INVALID_LENGTH_OR_CHARACTER");
        // Trim scanner CR/LF terminators only. Preserve content spaces and leading zeros.
        var scan = raw.TrimEnd('\r', '\n');
        var found = new List<LabIdentity>();
        foreach (var rule in rules.Where(x => x.Enabled))
        {
            try
            {
                var rx = new Regex(rule.Pattern, RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
                    TimeSpan.FromMilliseconds(50));
                var match = rx.Match(scan);
                if (!match.Success || match.Index != 0 || match.Length != scan.Length) continue;
                var fields = rx.GetGroupNames().Where(x => !int.TryParse(x, out _))
                    .ToDictionary(x => x, x => match.Groups[x].Value, StringComparer.Ordinal);
                var identityFields = LabCatalogValidator.SplitFields(rule.IdentityFieldsCsv);
                if (identityFields.Length == 0 || identityFields.Any(x =>
                    !fields.TryGetValue(x, out var v) || string.IsNullOrEmpty(v)))
                    return Reject(LabMatchStatus.Invalid, "IDENTITY_FIELD_EMPTY", new[] { rule.RuleId });
                // Include lengths to prevent composite-key collision; never convert SN to a number.
                var key = new StringBuilder();
                foreach (var field in identityFields)
                {
                    key.Append(field.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(field)
                        .Append(fields[field].Length.ToString(CultureInfo.InvariantCulture))
                        .Append(':').Append(fields[field]);
                }
                found.Add(new LabIdentity(key.ToString(), rule.RuleId, fields));
            }
            catch (RegexMatchTimeoutException)
            { return Reject(LabMatchStatus.Invalid, "REGEX_TIMEOUT", new[] { rule.RuleId }); }
            catch (ArgumentException)
            { return Reject(LabMatchStatus.Invalid, "REGEX_INVALID", new[] { rule.RuleId }); }
        }
        if (found.Count == 0) return Reject(LabMatchStatus.NoMatch, "IDENTITY_NO_MATCH");
        if (found.Count != 1) return Reject(LabMatchStatus.Ambiguous, "IDENTITY_AMBIGUOUS",
            found.Select(x => x.RuleId).ToArray());
        return new(LabMatchStatus.Matched, "IDENTITY_MATCHED", found[0], null,
            new[] { found[0].RuleId }, revision, scan);
    }

    internal static LabRecipe[] FilterRecipes(IReadOnlyList<LabRecipe> recipes, LabIdentity identity,
        DateTimeOffset? observedAtUtc = null)
    {
        var at = observedAtUtc ?? DateTimeOffset.UtcNow;
        return recipes.Where(x => x.Enabled && IsEffective(x, at) &&
            (string.IsNullOrWhiteSpace(x.ModelField) || (identity.Fields.TryGetValue("model", out var m) &&
                StringComparer.Ordinal.Equals(x.ModelField, m))) &&
            (string.IsNullOrWhiteSpace(x.CustomerField) || (identity.Fields.TryGetValue("customer", out var c) &&
                StringComparer.Ordinal.Equals(x.CustomerField, c)))).ToArray();
    }

    private static bool IsEffective(LabRecipe recipe, DateTimeOffset at)
    {
        if (!LabCatalogValidator.TryParseDate(recipe.EffectiveFromUtc, out var from) ||
            !LabCatalogValidator.TryParseDate(recipe.EffectiveUntilUtc, out var until)) return false;
        return (from is null || at >= from.Value) && (until is null || at < until.Value);
    }

    private static LabMatch Reject(LabMatchStatus status, string code,
        IReadOnlyList<string>? candidates = null) =>
        new(status, code, null, null, candidates ?? Array.Empty<string>());
}
