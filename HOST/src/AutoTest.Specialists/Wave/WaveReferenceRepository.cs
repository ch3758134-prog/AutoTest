using AutoTest.Contracts;
using AutoTest.Core;

namespace AutoTest.Specialists.Wave;

public sealed record WaveReference(ReferenceRef Reference, string Name, WaveSeries Series);

/// <summary>Reference identity is immutable (AssetId, positive revision, content SHA-256).</summary>
public sealed class WaveReferenceRepository
{
    private readonly string _folder;
    public WaveReferenceRepository(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentException("Reference directory required.", nameof(folder));
        _folder = Path.GetFullPath(folder);
    }

    public WaveReference SaveReference(AssetId id, AssetRevision revision,
        string name, WaveSeries series)
    {
        ArgumentNullException.ThrowIfNull(series);
        var bytes = WaveEvidenceCodec.WriteReference(id, revision, name, series);
        var fingerprint = WaveEvidenceCodec.Fingerprint(bytes);
        var reference = new ReferenceRef(new AssetRef(id, revision, fingerprint));
        WaveAtomicFile.WriteWithSidecar(FilePath(reference), bytes);
        return new WaveReference(reference, name, series);
    }

    public WaveReference LoadReference(ReferenceRef reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var bytes = WaveAtomicFile.ReadVerified(FilePath(reference));
        if (!WaveEvidenceCodec.Fingerprint(bytes).Matches(reference.Asset.Fingerprint))
            throw new InvalidDataException("Pinned reference fingerprint does not match disk data.");
        var item = WaveEvidenceCodec.ReadReference(bytes);
        if (item.Id != reference.Asset.Id || item.Revision != reference.Asset.Revision)
            throw new InvalidDataException("Reference asset identity/version mismatch.");
        return new WaveReference(reference, item.Name, item.Series);
    }

    private string FilePath(ReferenceRef reference)
    {
        if (reference.Asset.Id.Value == Guid.Empty || reference.Asset.Revision.Value < 1 ||
            string.IsNullOrEmpty(reference.Asset.Fingerprint.Sha256))
            throw new ArgumentException("Invalid pinned reference.", nameof(reference));
        return Path.Combine(_folder, reference.Asset.Id.Value.ToString("N") +
            "-r" + reference.Asset.Revision.Value + ".wfm.json");
    }
}
