using System.Collections.Immutable;
using System.Text.RegularExpressions;
using AutoTest.Contracts;
using AutoTest.Core;

namespace AutoTest.Specialists.Scan;

public enum RulePublication { Draft, Published }
public sealed record IdentityRule(string RuleId, string Pattern,
    ImmutableArray<string> IdentityKeyFields, RulePublication Publication);
public sealed record ParsedIdentity(string IdentityKey, ImmutableDictionary<string, string> Fields,
    string RuleId, RevisionFingerprint RuleFingerprint);
public sealed record IdentityEvaluation(ParsedIdentity? Identity, string Code,
    ImmutableArray<string> MatchedRuleIds)
{
    public bool Accepted => Identity is not null && Code == "IDENTITY_MATCHED";
}

// Revision of the entire published rule set, independent of project/resource revision.
public sealed class IdentityRuleSet
{
    private readonly ImmutableArray<IdentityRule> _rules;
    public RevisionFingerprint Fingerprint { get; }
    public bool Published { get; }

    public IdentityRuleSet(IEnumerable<IdentityRule> rules, RevisionFingerprint fingerprint,
        bool published)
    {
        _rules = rules.ToImmutableArray(); Fingerprint = fingerprint; Published = published;
    }

    public ImmutableArray<string> Validate()
    {
        var errors = ImmutableArray.CreateBuilder<string>();
        if (_rules.Length == 0) errors.Add("RULESET_EMPTY");
        foreach (var duplicated in _rules.GroupBy(x => x.RuleId, StringComparer.Ordinal)
                     .Where(x => x.Count() > 1)) errors.Add("RULE_ID_DUPLICATE:" + duplicated.Key);
        foreach (var rule in _rules.Where(x => !Published || x.Publication == RulePublication.Published))
        {
            if (string.IsNullOrWhiteSpace(rule.RuleId) || string.IsNullOrWhiteSpace(rule.Pattern))
            { errors.Add("RULE_EMPTY_FIELD:" + rule.RuleId); continue; }
            if (rule.IdentityKeyFields.IsDefaultOrEmpty || rule.IdentityKeyFields.Any(string.IsNullOrWhiteSpace))
                errors.Add("RULE_IDENTITY_FIELDS_EMPTY:" + rule.RuleId);
            try
            {
                var names = new Regex(rule.Pattern, RegexOptions.CultureInvariant,
                    TimeSpan.FromMilliseconds(100)).GetGroupNames();
                foreach (var field in rule.IdentityKeyFields)
                    if (!names.Contains(field, StringComparer.Ordinal))
                        errors.Add("RULE_FIELD_NOT_CAPTURED:" + rule.RuleId + ":" + field);
            }
            catch (ArgumentException) { errors.Add("RULE_REGEX_INVALID:" + rule.RuleId); }
        }
        // Regex overlap is undecidable in general. Runtime ambiguity is *always* rejected.
        return errors.ToImmutable();
    }

    public IdentityEvaluation Match(RawScan raw)
    {
        if (!Published) return new(null, "RULESET_NOT_PUBLISHED", ImmutableArray<string>.Empty);
        if (Validate().Length != 0) return new(null, "RULESET_INVALID", ImmutableArray<string>.Empty);
        if (string.IsNullOrWhiteSpace(raw.Text)) return new(null, "SCAN_EMPTY", ImmutableArray<string>.Empty);
        var matches = ImmutableArray.CreateBuilder<ParsedIdentity>();
        foreach (var rule in _rules.Where(x => x.Publication == RulePublication.Published))
        {
            System.Text.RegularExpressions.Match m;
            try { m = Regex.Match(raw.Text, rule.Pattern, RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100)); }
            catch (RegexMatchTimeoutException)
            { return new(null, "RULE_REGEX_TIMEOUT", ImmutableArray.Create(rule.RuleId)); }
            if (!m.Success || m.Index != 0 || m.Length != raw.Text.Length) continue;
            var fields = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
            foreach (var groupName in new Regex(rule.Pattern, RegexOptions.CultureInvariant,
                         TimeSpan.FromMilliseconds(100)).GetGroupNames())
                if (!int.TryParse(groupName, out _)) fields[groupName] = m.Groups[groupName].Value;
            if (rule.IdentityKeyFields.Any(k => !fields.TryGetValue(k, out var v) || string.IsNullOrEmpty(v)))
                return new(null, "IDENTITY_FIELD_MISSING", ImmutableArray.Create(rule.RuleId));
            // Prefix each segment with its length to avoid identity-key separator collisions.
            var key = string.Concat(rule.IdentityKeyFields.Select(k =>
                fields[k].Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + fields[k]));
            matches.Add(new ParsedIdentity(key, fields.ToImmutable(), rule.RuleId, Fingerprint));
        }
        if (matches.Count == 0) return new(null, "IDENTITY_NO_MATCH", ImmutableArray<string>.Empty);
        if (matches.Count > 1) return new(null, "IDENTITY_AMBIGUOUS",
            matches.Select(x => x.RuleId).ToImmutableArray());
        return new(matches[0], "IDENTITY_MATCHED", ImmutableArray.Create(matches[0].RuleId));
    }
}
