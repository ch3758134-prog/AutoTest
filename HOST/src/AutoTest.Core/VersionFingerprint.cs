using System.Security.Cryptography;
using System.Collections.Immutable;

namespace AutoTest.Core;

public readonly record struct RevisionFingerprint
{
    public string Sha256 { get; }
    private RevisionFingerprint(string sha256) => Sha256 = sha256;
    public static RevisionFingerprint FromSha256(string hex)
    {
        if (hex is null || hex.Length != 64 || !hex.All(Uri.IsHexDigit))
            throw new ArgumentException("SHA-256 requires exactly 64 hex characters.", nameof(hex));
        return new RevisionFingerprint(hex.ToLowerInvariant());
    }
    public static RevisionFingerprint FromBytes(ReadOnlySpan<byte> bytes) =>
        new(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    public bool Matches(RevisionFingerprint other) =>
        !string.IsNullOrEmpty(Sha256) && string.Equals(Sha256, other.Sha256, StringComparison.Ordinal);
    public override string ToString() => Sha256 ?? "<invalid>";
}

public readonly record struct AssetRevision(int Value)
{
    public static AssetRevision Create(int value) => value > 0 ? new(value) :
        throw new ArgumentOutOfRangeException(nameof(value));
}
public sealed record AssetRef(AssetId Id, AssetRevision Revision, RevisionFingerprint Fingerprint);
