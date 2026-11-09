using System.Text.Json;
using AutoTest.Contracts;
using AutoTest.Core;

namespace AutoTest.Specialists.Wave;

public sealed record WaveReferenceEntry(ReferenceRef Reference, SignalRef Signal,
    string Name, DateTimeOffset CreatedUtc);

/// <summary>
/// Index of immutable reference assets. JSON stores only validated primitives, never relies on
/// the default deserializer for the private-constructor RevisionFingerprint value object.
/// </summary>
public sealed class WaveReferenceCatalog
{
    private sealed record Row(Guid ReferenceId, int ReferenceRevision, string ReferenceSha256,
        Guid DbcId, int DbcRevision, string DbcSha256, BusRole Role, uint FrameId,
        string SignalName, string Unit, string Name, DateTimeOffset CreatedUtc);

    private readonly string _path;
    private readonly WaveReferenceRepository _repository;
    private readonly object _gate = new();

    public WaveReferenceCatalog(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var directory = Path.GetFullPath(root);
        _path = Path.Combine(directory, "index.json");
        _repository = new WaveReferenceRepository(Path.Combine(directory, "assets"));
    }

    public IReadOnlyList<WaveReferenceEntry> List()
    {
        lock (_gate) return ReadIndex();
    }

    public WaveReference Save(string name, WaveSeries captured)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(captured);
        if (!WaveTimeQuality.Describe(captured).CanJudge)
            throw new InvalidDataException("Cannot promote invalid or incomplete waveform to reference.");
        lock (_gate)
        {
            var existing = ReadIndex();
            var created = _repository.SaveReference(AssetId.New(), AssetRevision.Create(1), name, captured);
            existing.Add(new WaveReferenceEntry(created.Reference, captured.Signal, name, DateTimeOffset.UtcNow));
            PersistIndex(existing);
            return created;
        }
    }

    public WaveReference? LatestFor(SignalRef signal)
    {
        WaveTimeQuality.ValidateSignal(signal);
        lock (_gate)
        {
            var row = ReadIndex().LastOrDefault(x => x.Signal == signal);
            return row is null ? null : _repository.LoadReference(row.Reference);
        }
    }

    public WaveReference Load(ReferenceRef reference) => _repository.LoadReference(reference);

    private List<WaveReferenceEntry> ReadIndex()
    {
        if (!File.Exists(_path)) return new List<WaveReferenceEntry>();
        var info = new FileInfo(_path);
        if (info.Length is <= 0 or > 8_388_608)
            throw new InvalidDataException("Wave reference catalog size invalid.");
        try
        {
            var rows = JsonSerializer.Deserialize<List<Row>>(File.ReadAllBytes(_path))
                ?? throw new InvalidDataException("Wave reference catalog is null.");
            if (rows.Count > 10_000) throw new InvalidDataException("Wave catalog limit exceeded.");
            return rows.Select(row =>
            {
                var reference = new ReferenceRef(new AssetRef(AssetId.Create(row.ReferenceId),
                    AssetRevision.Create(row.ReferenceRevision),
                    RevisionFingerprint.FromSha256(row.ReferenceSha256)));
                var signal = new SignalRef(new AssetRef(AssetId.Create(row.DbcId),
                    AssetRevision.Create(row.DbcRevision), RevisionFingerprint.FromSha256(row.DbcSha256)),
                    row.Role, row.FrameId, row.SignalName, row.Unit);
                WaveTimeQuality.ValidateSignal(signal);
                return new WaveReferenceEntry(reference, signal, row.Name, row.CreatedUtc);
            }).ToList();
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        { throw new InvalidDataException("Wave reference catalog is corrupt.", ex); }
    }

    private void PersistIndex(List<WaveReferenceEntry> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var rows = entries.Select(entry => new Row(entry.Reference.Asset.Id.Value,
                entry.Reference.Asset.Revision.Value, entry.Reference.Asset.Fingerprint.Sha256,
                entry.Signal.DbcAsset.Id.Value, entry.Signal.DbcAsset.Revision.Value,
                entry.Signal.DbcAsset.Fingerprint.Sha256, entry.Signal.Role, entry.Signal.FrameId,
                entry.Signal.SignalName, entry.Signal.Unit, entry.Name, entry.CreatedUtc)).ToArray();
            File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(rows));
            File.Move(temp, _path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
