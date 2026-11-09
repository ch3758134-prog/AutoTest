using AutoTest.Core;
using AutoTest.Projects.Project;
namespace AutoTest.Projects.Resources;

public sealed record AssetEntry(AssetId Id, int Revision, string Kind, string Name, string RelativePath, string Sha256)
{
    public static AssetEntry Create(string kind, string name, string relativePath, ReadOnlySpan<byte> bytes) =>
        new AssetEntry(AssetId.New(), 1, kind, name, relativePath, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant()).Validate();
    public AssetEntry Validate()
    {
        if (Id.Value == Guid.Empty || Revision < 1 || string.IsNullOrWhiteSpace(Kind) ||
            string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(RelativePath) ||
            Sha256 is null || Sha256.Length != 64 || !Sha256.All(Uri.IsHexDigit))
            throw new ProjectDataException("ASSET_INVALID", "Asset metadata is malformed.");
        ProjectPathPolicy.Normalize(Path.GetTempPath(), RelativePath);
        return this;
    }
    public bool VerifyHash(ReadOnlySpan<byte> bytes) =>
        string.Equals(Sha256, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)), StringComparison.OrdinalIgnoreCase);
    public AssetRef ToReference() => new(Id, AssetRevision.Create(Revision), RevisionFingerprint.FromSha256(Sha256));
}
public sealed record AssetDependency(AssetId From, AssetId To, int RequiredRevision, string Purpose);
