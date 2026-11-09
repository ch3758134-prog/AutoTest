using System.Security.Cryptography;
using AutoTest.Core;

namespace AutoTest.Records.Storage;

public enum ArtifactIntegrity { Present, Missing, Corrupt }

/// <summary>Content-addressed immutable evidence reference; relative path is derived, never supplied by a caller.</summary>
public sealed record ArtifactDescriptor(Guid ArtifactId, string Kind, string MediaType,
    long ByteLength, string Sha256, string RelativePath, string Provenance)
{
    public void Validate()
    {
        if (ArtifactId == Guid.Empty || string.IsNullOrWhiteSpace(Kind) ||
            string.IsNullOrWhiteSpace(MediaType) || string.IsNullOrWhiteSpace(Provenance) ||
            ByteLength < 0 || Sha256 is not { Length: 64 } || !Sha256.All(Uri.IsHexDigit) ||
            RelativePath != ArtifactRepository.BuildRelativePath(Sha256))
            throw new InvalidDataException("Invalid immutable artifact reference.");
    }
}

/// <summary>Artifact bytes are outside SQLite; SQLite stores only descriptor metadata.</summary>
public sealed class ArtifactRepository
{
    private readonly string _root;
    public ArtifactRepository(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _root = Path.GetFullPath(rootDirectory);
    }

    public static string BuildRelativePath(string sha256)
    {
        if (sha256 is null || sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
            throw new ArgumentException("Expected SHA-256 hex.", nameof(sha256));
        var hex = sha256.ToLowerInvariant();
        return $"objects/{hex[..2]}/{hex}";
    }

    public async Task<ArtifactDescriptor> PutAsync(Stream source, string kind, string mediaType,
        string provenance, EvidenceRef? evidenceIdentity = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead || string.IsNullOrWhiteSpace(kind) ||
            string.IsNullOrWhiteSpace(mediaType) || string.IsNullOrWhiteSpace(provenance))
            throw new ArgumentException("Artifact stream/kind/mediaType/provenance is invalid.");
        Directory.CreateDirectory(_root);
        var staging = Path.Combine(_root, ".staging-" + Guid.NewGuid().ToString("N"));
        try
        {
            long size;
            string hash;
            await using (var output = new FileStream(staging, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await source.CopyToAsync(output, ct).ConfigureAwait(false);
                await output.FlushAsync(ct).ConfigureAwait(false);
                output.Flush(true);
                size = output.Length;
            }
            await using (var verify = File.OpenRead(staging))
                hash = Convert.ToHexString(await SHA256.HashDataAsync(verify, ct).ConfigureAwait(false)).ToLowerInvariant();
            var relativePath = BuildRelativePath(hash);
            var destination = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            try { File.Move(staging, destination, overwrite: false); }
            catch (IOException) when (File.Exists(destination)) { /* existing deduplicated blob must be reverified */ }
            var descriptor = new ArtifactDescriptor(evidenceIdentity?.Value ?? Guid.NewGuid(), kind, mediaType, size, hash,
                relativePath, provenance);
            if (await VerifyAsync(descriptor, ct).ConfigureAwait(false) != ArtifactIntegrity.Present)
                throw new InvalidDataException("Artifact collision, missing bytes, or hash mismatch.");
            return descriptor;
        }
        finally { if (File.Exists(staging)) File.Delete(staging); }
    }

    public async Task<ArtifactIntegrity> VerifyAsync(ArtifactDescriptor artifact, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        artifact.Validate();
        var path = Path.Combine(_root, artifact.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path)) return ArtifactIntegrity.Missing;
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length != artifact.ByteLength) return ArtifactIntegrity.Corrupt;
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false)).ToLowerInvariant();
            return hash == artifact.Sha256 ? ArtifactIntegrity.Present : ArtifactIntegrity.Corrupt;
        }
        catch (FileNotFoundException) { return ArtifactIntegrity.Missing; }
        catch (DirectoryNotFoundException) { return ArtifactIntegrity.Missing; }
        catch (IOException) { return ArtifactIntegrity.Corrupt; }
    }

    public async Task<Stream> OpenReadAsync(ArtifactDescriptor artifact, CancellationToken ct = default)
    {
        var status = await VerifyAsync(artifact, ct).ConfigureAwait(false);
        if (status != ArtifactIntegrity.Present)
            throw new InvalidDataException($"Evidence artifact is {status}.");
        return new FileStream(Path.Combine(_root, artifact.RelativePath.Replace('/', Path.DirectorySeparatorChar)),
            FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
    }
}
