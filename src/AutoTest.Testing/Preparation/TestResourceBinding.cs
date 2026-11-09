using System.Collections.Immutable;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Testing.Authoring;

namespace AutoTest.Testing.Preparation;

/// <summary>
/// TEST-local interpretation of an existing Core AssetRef. The JSON authoring format still
/// carries scalar strings, but a runnable plan must resolve typed identity, revision and hash.
/// No new cross-module resource DTO is introduced.
/// </summary>
public static class TestResourceBinding
{
    public const string IdKey = "assetId";
    public const string RevisionKey = "assetRevision";
    public const string HashKey = "assetSha256";

    public static ImmutableArray<string> Validate(TestDocument document, IAssetResolver? resolver)
    {
        ArgumentNullException.ThrowIfNull(document);
        var errors = ImmutableArray.CreateBuilder<string>();
        foreach (var node in document.Walk())
        {
            var p = node.Properties;
            if (!p.ContainsKey(IdKey) && !p.ContainsKey(RevisionKey) && !p.ContainsKey(HashKey))
                continue;
            var where = node.Id.ToString();
            if (!p.TryGetValue(IdKey, out var idText) || !AssetId.TryParse(idText, out var id))
            {
                errors.Add($"{where}: invalid typed assetId");
                continue;
            }
            if (!p.TryGetValue(RevisionKey, out var revisionText) ||
                !int.TryParse(revisionText, out var revision) || revision < 1)
            {
                errors.Add($"{where}: invalid typed assetRevision");
                continue;
            }
            RevisionFingerprint hash;
            try
            {
                if (!p.TryGetValue(HashKey, out var text)) throw new ArgumentException();
                hash = RevisionFingerprint.FromSha256(text);
            }
            catch (ArgumentException)
            {
                errors.Add($"{where}: invalid typed assetSha256");
                continue;
            }
            if (resolver is null)
            {
                errors.Add($"{where}: PROJECT IAssetResolver is unavailable; typed asset binding cannot be verified");
                continue;
            }
            try
            {
                var resolved = resolver.Resolve(id, AssetRevision.Create(revision));
                if (resolved.Outcome != Outcome.Accepted || resolved.Value is null ||
                    resolved.Value.Id != id || resolved.Value.Revision.Value != revision ||
                    !resolved.Value.Fingerprint.Matches(hash))
                    errors.Add($"{where}: missing or stale asset binding (AssetId/Revision/SHA256 mismatch)");
            }
            catch (Exception ex)
            {
                errors.Add($"{where}: PROJECT resolver failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
        return errors.ToImmutable();
    }
}
