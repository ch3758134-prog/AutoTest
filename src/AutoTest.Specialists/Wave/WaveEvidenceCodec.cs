using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoTest.Contracts;
using AutoTest.Core;

namespace AutoTest.Specialists.Wave;

public sealed record WaveEvidenceDocument(
    EvidenceRef Id, string Kind, WaveSeries Actual, WaveSeries? Reference,
    ReferenceRef? ReferenceAsset, string? ReferenceName, EvidenceRef? CaptureEvidence,
    WaveComparisonConfig? Configuration, WaveComparisonDetails? Result,
    RunId? Run, StepId? Step, DateTimeOffset CreatedUtc);

/// <summary>Explicit primitive JSON wire avoids silently deserializing invalid strong IDs.</summary>
public static class WaveEvidenceCodec
{
    public const int SchemaVersion = 1;
    public const string MediaType = "application/vnd.autotest.wave-evidence+json";
    private const int MaxEvidenceBytes = 64 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false
    };

    private sealed record SampleWire(double Value, string Unit, DateTimeOffset Utc,
        long MonotonicTicks, string Source, string Quality, bool Gap,
        Guid SourceEvidence, string InputQuality);
    private sealed record SeriesWire(Guid DbcId, int DbcRevision, string DbcFingerprint,
        string BusRole, uint FrameId, string SignalName, string Unit,
        DateTimeOffset OriginUtc, long OriginMonotonicTicks, string OriginSource,
        string OriginQuality, SampleWire[] Samples, long ObservedEvents,
        int RejectedEvents, bool Overflow, bool NonMonotonic, bool MixedClock,
        bool InvalidInput, bool HasGap, Guid[] InputEvidence);
    private sealed record ReferenceWire(int SchemaVersion, Guid AssetId,
        int Revision, string Name, SeriesWire Series);
    private sealed record DocumentWire(int SchemaVersion, Guid Id, string Kind,
        SeriesWire Actual, SeriesWire? Reference, Guid? ReferenceAssetId,
        int? ReferenceRevision, string? ReferenceFingerprint, string? ReferenceName, Guid? CaptureEvidence,
        WaveComparisonConfig? Configuration, WaveComparisonDetails? Result,
        Guid? Run, Guid? Step, DateTimeOffset CreatedUtc);

    public static byte[] Write(WaveEvidenceDocument document)
    {
        ValidateDocument(document);
        return JsonSerializer.SerializeToUtf8Bytes(new DocumentWire(SchemaVersion,
            document.Id.Value, document.Kind, Encode(document.Actual),
            document.Reference is null ? null : Encode(document.Reference),
            document.ReferenceAsset?.Asset.Id.Value,
            document.ReferenceAsset?.Asset.Revision.Value,
            document.ReferenceAsset?.Asset.Fingerprint.Sha256, document.ReferenceName,
            document.CaptureEvidence?.Value, document.Configuration, document.Result,
            document.Run?.Value, document.Step?.Value, document.CreatedUtc), Options);
    }

    public static WaveEvidenceDocument Read(ReadOnlySpan<byte> bytes)
    {
        var wire = Deserialize<DocumentWire>(bytes);
        if (wire.SchemaVersion != SchemaVersion || wire.Id == Guid.Empty)
            throw new InvalidDataException("Unsupported or incomplete wave evidence schema.");
        ReferenceRef? asset = null;
        if (wire.ReferenceAssetId.HasValue || wire.ReferenceRevision.HasValue ||
            wire.ReferenceFingerprint is not null)
        {
            if (wire.ReferenceAssetId is not { } id || wire.ReferenceRevision is not { } rev ||
                wire.ReferenceFingerprint is null)
                throw new InvalidDataException("Partial reference asset identity.");
            asset = new ReferenceRef(new AssetRef(AssetId.Create(id), AssetRevision.Create(rev),
                RevisionFingerprint.FromSha256(wire.ReferenceFingerprint)));
        }
        var document = new WaveEvidenceDocument(new EvidenceRef(wire.Id), wire.Kind,
            Decode(wire.Actual), wire.Reference is null ? null : Decode(wire.Reference), asset,
            wire.ReferenceName, wire.CaptureEvidence is null ? null : new EvidenceRef(wire.CaptureEvidence.Value),
            wire.Configuration, wire.Result,
            wire.Run is null ? null : RunId.Create(wire.Run.Value),
            wire.Step is null ? null : StepId.Create(wire.Step.Value), wire.CreatedUtc);
        ValidateDocument(document);
        return document;
    }

    public static bool Verify(WaveEvidenceDocument document)
    {
        try
        {
            ValidateDocument(document);
            if (document.Kind == "Capture") return document.Actual.Samples.Length > 0;
            if (document.Reference is null || document.Configuration is null || document.Result is null ||
                document.ReferenceAsset is null || document.CaptureEvidence is null ||
                string.IsNullOrWhiteSpace(document.ReferenceName))
                return false;
            var referenceBytes = WriteReference(document.ReferenceAsset.Asset.Id,
                document.ReferenceAsset.Asset.Revision, document.ReferenceName, document.Reference);
            if (!Fingerprint(referenceBytes).Matches(document.ReferenceAsset.Asset.Fingerprint))
                return false;
            var recomputed = WaveComparator.Compare(document.Actual, document.Reference,
                document.Configuration);
            return recomputed == document.Result;
        }
        catch (Exception e) when (e is ArgumentException or InvalidDataException or JsonException)
        {
            return false;
        }
    }

    public static byte[] WriteReference(AssetId id, AssetRevision revision,
        string name, WaveSeries series)
    {
        if (id.Value == Guid.Empty || revision.Value < 1 || string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Reference identity and name are required.");
        if (!WaveTimeQuality.Describe(series).CanJudge)
            throw new InvalidDataException("Invalid wave series cannot be promoted to reference.");
        return JsonSerializer.SerializeToUtf8Bytes(new ReferenceWire(SchemaVersion,
            id.Value, revision.Value, name, Encode(series)), Options);
    }

    public static (AssetId Id, AssetRevision Revision, string Name, WaveSeries Series)
        ReadReference(ReadOnlySpan<byte> bytes)
    {
        var wire = Deserialize<ReferenceWire>(bytes);
        if (wire.SchemaVersion != SchemaVersion || string.IsNullOrWhiteSpace(wire.Name))
            throw new InvalidDataException("Invalid wave reference schema.");
        var series = Decode(wire.Series);
        if (!WaveTimeQuality.Describe(series).CanJudge)
            throw new InvalidDataException("Reference data failed quality validation.");
        return (AssetId.Create(wire.AssetId), AssetRevision.Create(wire.Revision), wire.Name, series);
    }

    public static RevisionFingerprint Fingerprint(ReadOnlySpan<byte> data) =>
        RevisionFingerprint.FromBytes(data);

    public static RevisionFingerprint FingerprintSeries(WaveSeries series) =>
        Fingerprint(JsonSerializer.SerializeToUtf8Bytes(Encode(series), Options));

    private static void ValidateDocument(WaveEvidenceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Id.Value == Guid.Empty || document.Actual is null ||
            document.CreatedUtc == default)
            throw new InvalidDataException("Missing evidence identity/data/time.");
        if (document.Kind is not ("Capture" or "Comparison"))
            throw new InvalidDataException("Unsupported wave document type.");
        WaveTimeQuality.ValidateSignal(document.Actual.Signal);
        if (document.Kind == "Capture" && (document.Reference is not null ||
            document.ReferenceAsset is not null || document.ReferenceName is not null ||
            document.Result is not null || document.Configuration is not null ||
            document.CaptureEvidence is not null))
            throw new InvalidDataException("Capture contains comparison-only fields.");
        if (document.Kind == "Comparison" && (document.Reference is null ||
            document.ReferenceAsset is null || string.IsNullOrWhiteSpace(document.ReferenceName) ||
            document.Result is null || document.Configuration is null || document.CaptureEvidence is null ||
            document.CaptureEvidence.Value.Value == Guid.Empty))
            throw new InvalidDataException("Incomplete comparison evidence.");
    }

    private static SeriesWire Encode(WaveSeries series) =>
        new(series.Signal.DbcAsset.Id.Value, series.Signal.DbcAsset.Revision.Value,
            series.Signal.DbcAsset.Fingerprint.Sha256, series.Signal.Role.ToString(),
            series.Signal.FrameId, series.Signal.SignalName, series.Signal.Unit,
            series.Origin.Utc, series.Origin.MonotonicTicks, series.Origin.Source.ToString(),
            series.Origin.Quality.ToString(),
            series.Samples.Select(s => new SampleWire(s.Sample.Value, s.Sample.Unit,
                s.Sample.Time.Utc, s.Sample.Time.MonotonicTicks,
                s.Sample.Time.Source.ToString(), s.Sample.Time.Quality.ToString(),
                s.Sample.Gap, s.SourceEvidence.Value, s.InputQuality.ToString())).ToArray(),
            series.ObservedEvents, series.RejectedEvents, series.Overflow,
            series.NonMonotonic, series.MixedClock, series.InvalidInput, series.HasGap,
            series.InputEvidence.Select(x => x.Value).ToArray());

    private static WaveSeries Decode(SeriesWire wire)
    {
        if (wire is null || wire.Samples is null || wire.InputEvidence is null ||
            wire.ObservedEvents < 0 || wire.RejectedEvents < 0 ||
            wire.Samples.Length > 5_000_000 || wire.InputEvidence.Length > 5_000_001 ||
            wire.ObservedEvents < wire.Samples.Length)
            throw new InvalidDataException("Invalid waveform counts.");
        var signal = new SignalRef(new AssetRef(AssetId.Create(wire.DbcId),
            AssetRevision.Create(wire.DbcRevision),
            RevisionFingerprint.FromSha256(wire.DbcFingerprint)),
            ParseEnum<BusRole>(wire.BusRole), wire.FrameId, wire.SignalName, wire.Unit);
        WaveTimeQuality.ValidateSignal(signal);
        var origin = new TimePoint(wire.OriginUtc, wire.OriginMonotonicTicks,
            ParseEnum<TimeSource>(wire.OriginSource), ParseEnum<TimeQuality>(wire.OriginQuality));
        var samples = wire.Samples.Select(s =>
        {
            if (s is null || !double.IsFinite(s.Value))
                throw new InvalidDataException("Wave contains non-finite sample.");
            return new WaveCapturedSample(new WaveSample(s.Value, s.Unit,
                new TimePoint(s.Utc, s.MonotonicTicks, ParseEnum<TimeSource>(s.Source),
                    ParseEnum<TimeQuality>(s.Quality)), s.Gap),
                new EvidenceRef(s.SourceEvidence), ParseEnum<EvidenceQuality>(s.InputQuality));
        }).ToImmutableArray();
        return new WaveSeries(signal, origin, samples, wire.ObservedEvents,
            wire.RejectedEvents, wire.Overflow, wire.NonMonotonic, wire.MixedClock,
            wire.InvalidInput, wire.HasGap,
            wire.InputEvidence.Select(x => new EvidenceRef(x)).ToImmutableArray());
    }

    private static T ParseEnum<T>(string value) where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !Enum.TryParse<T>(value, false, out var result) ||
            !Enum.IsDefined(result) || !string.Equals(result.ToString(), value, StringComparison.Ordinal))
            throw new InvalidDataException($"Invalid enum value: {typeof(T).Name}.");
        return result;
    }

    private static T Deserialize<T>(ReadOnlySpan<byte> content)
    {
        if (content.IsEmpty || content.Length > MaxEvidenceBytes)
            throw new InvalidDataException("Wave payload is empty or too large.");
        try { return JsonSerializer.Deserialize<T>(content, Options)
            ?? throw new InvalidDataException("Wave payload is empty."); }
        catch (JsonException e) { throw new InvalidDataException("Wave JSON is corrupt.", e); }
    }
}

/// <summary>Content-checksummed, append-only evidence repository; no shared database dependency.</summary>
public sealed class WaveEvidenceStore
{
    private readonly string _folder;
    public WaveEvidenceStore(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentException("Evidence folder required.", nameof(folder));
        _folder = Path.GetFullPath(folder);
    }

    public EvidenceRef Write(WaveEvidenceDocument document)
    {
        var bytes = WaveEvidenceCodec.Write(document);
        var path = FilePath(document.Id);
        WaveAtomicFile.WriteWithSidecar(path, bytes);
        return document.Id;
    }

    public WaveEvidenceDocument Read(EvidenceRef id)
    {
        var doc = ReadSingle(id);
        if (doc.Kind == "Comparison")
        {
            if (doc.CaptureEvidence is null || doc.CaptureEvidence.Value == id)
                throw new InvalidDataException("Invalid comparison-to-capture link.");
            var source = ReadSingle(doc.CaptureEvidence.Value);
            if (source.Kind != "Capture" ||
                !WaveEvidenceCodec.FingerprintSeries(source.Actual).Matches(
                    WaveEvidenceCodec.FingerprintSeries(doc.Actual)) ||
                source.Run != doc.Run || source.Step != doc.Step)
                throw new InvalidDataException("Capture evidence fingerprint or run/step link differs.");
        }
        return doc;
    }

    private WaveEvidenceDocument ReadSingle(EvidenceRef id)
    {
        var bytes = WaveAtomicFile.ReadVerified(FilePath(id));
        var doc = WaveEvidenceCodec.Read(bytes);
        if (doc.Id != id || !WaveEvidenceCodec.Verify(doc))
            throw new InvalidDataException("Wave evidence identity or comparison verification failed.");
        return doc;
    }

    public bool Verify(EvidenceRef id)
    {
        try { _ = Read(id); return true; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or
                                 InvalidDataException or ArgumentException or JsonException) { return false; }
    }

    private string FilePath(EvidenceRef id)
    {
        if (id.Value == Guid.Empty) throw new ArgumentException("Evidence ID is empty.", nameof(id));
        return Path.Combine(_folder, id.Value.ToString("N") + ".wave.json");
    }
}

internal static class WaveAtomicFile
{
    private const int MaxBytes = 64 * 1024 * 1024;
    public static void WriteWithSidecar(string path, byte[] content)
    {
        if (content.Length is 0 or > MaxBytes) throw new InvalidDataException("Wave artifact size invalid.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path) || File.Exists(path + ".sha256"))
            throw new IOException("Wave evidence/reference is immutable: revision already exists.");
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var payloadMoved = false;
        try
        {
            File.WriteAllBytes(temp, content);
            File.WriteAllText(temp + ".sha256", Convert.ToHexString(SHA256.HashData(content)), Encoding.ASCII);
            File.Move(temp, path);
            payloadMoved = true;
            File.Move(temp + ".sha256", path + ".sha256");
        }
        catch
        {
            // Two-file move is not a transaction. Roll back an unfinished local write.
            if (payloadMoved && !File.Exists(path + ".sha256") && File.Exists(path))
                File.Delete(path);
            throw;
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
            if (File.Exists(temp + ".sha256")) File.Delete(temp + ".sha256");
        }
    }

    public static byte[] ReadVerified(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length is <= 0 or > MaxBytes)
            throw new InvalidDataException("Wave artifact missing or invalid size.");
        var bytes = File.ReadAllBytes(path);
        var checksum = File.ReadAllText(path + ".sha256").Trim();
        if (!string.Equals(checksum, Convert.ToHexString(SHA256.HashData(bytes)),
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Wave artifact SHA-256 mismatch.");
        return bytes;
    }
}
