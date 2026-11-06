using System.Collections.Immutable;
using AutoTest.Contracts;
using AutoTest.Core;

namespace AutoTest.Specialists.Scan;

public sealed record ProductionRecipe(string RecipeId, int Revision,
    string? RequiredModel, string? RequiredCustomer, ImmutableArray<AssetRef> Assets,
    bool RequiresFlash, bool Published);

public sealed record ConfigMatchResult(MatchDecision Decision, string? RecipeId,
    int? RecipeRevision, ImmutableArray<string> CandidateIds);

// Fail closed: never choose the first matching recipe, never silently substitute resources.
public sealed class ConfigMatchService : IIdentityMatcher
{
    private readonly IdentityRuleSet _rules;
    private readonly RuleRevision _revision;
    private readonly ProjectRevision _projectRevision;
    private readonly ImmutableArray<ProductionRecipe> _recipes;

    public ConfigMatchService(IdentityRuleSet rules, ProjectRevision projectRevision,
        IEnumerable<ProductionRecipe> recipes)
    {
        _rules = rules; _revision = new RuleRevision(rules.Fingerprint);
        _projectRevision = projectRevision; _recipes = recipes.ToImmutableArray();
    }

    public MatchDecision Match(RawScan rawScan, RuleRevision ruleRevision,
        ProjectRevision projectRevision) => Evaluate(rawScan, ruleRevision, projectRevision).Decision;

    public ConfigMatchResult Evaluate(RawScan rawScan, RuleRevision ruleRevision,
        ProjectRevision projectRevision)
    {
        if (!_revision.Fingerprint.Matches(ruleRevision.Fingerprint) || _projectRevision != projectRevision)
            return Reject("MATCH_REVISION_STALE");
        var parsed = _rules.Match(rawScan);
        if (!parsed.Accepted || parsed.Identity is null) return Reject(parsed.Code);
        var fields = parsed.Identity.Fields;
        var candidates = _recipes.Where(r => r.Published &&
            (r.RequiredModel is null || (fields.TryGetValue("model", out var model) && model == r.RequiredModel)) &&
            (r.RequiredCustomer is null || (fields.TryGetValue("customer", out var customer) && customer == r.RequiredCustomer)))
            .ToImmutableArray();
        if (candidates.Length == 0) return Reject("CONFIG_NO_MATCH");
        if (candidates.Length > 1) return Reject("CONFIG_AMBIGUOUS", candidates.Select(r => r.RecipeId));
        var recipe = candidates[0];
        if (recipe.Revision <= 0 || recipe.Assets.IsDefaultOrEmpty ||
            recipe.Assets.Any(a => a.Id.Value == Guid.Empty || a.Revision.Value <= 0 ||
                string.IsNullOrWhiteSpace(a.Fingerprint.Sha256)))
            return Reject("CONFIG_ASSET_INVALID", new[] { recipe.RecipeId });
        return new(new MatchDecision(true, parsed.Identity.IdentityKey, recipe.Assets, null),
            recipe.RecipeId, recipe.Revision, ImmutableArray.Create(recipe.RecipeId));
    }

    public static ConfigMatchResult RejectAmbiguous(IEnumerable<string> candidates) =>
        Reject("CONFIG_AMBIGUOUS", candidates);

    private static ConfigMatchResult Reject(string code, IEnumerable<string>? candidates = null) =>
        new(new MatchDecision(false, null, ImmutableArray<AssetRef>.Empty, code), null, null,
            candidates?.ToImmutableArray() ?? ImmutableArray<string>.Empty);
}
