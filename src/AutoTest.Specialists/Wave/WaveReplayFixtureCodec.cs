using System.Collections.Immutable;
using System.Text.Json;
using AutoTest.Contracts;
using AutoTest.Core;

namespace AutoTest.Specialists.Wave;

/// <summary>Versioned, deterministic mock/replay fixture. Not a physical CAN observation.</summary>
public static class WaveReplayFixtureCodec
{
    private sealed record FixturePoint(double Milliseconds, double? Value,
        Guid Evidence, bool Gap = false);
    private sealed record Fixture(int SchemaVersion, Guid DbcAssetId, int DbcRevision,
        string DbcSha256, string BusRole, uint FrameId, string SignalName,
        string Unit, DateTimeOffset OriginUtc, FixturePoint[] Samples);
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static (SignalRef Signal, WaveReplaySignalSource Source) Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty || bytes.Length > 4_194_304)
            throw new InvalidDataException("Replay fixture size invalid.");
        Fixture fixture;
        try { fixture = JsonSerializer.Deserialize<Fixture>(bytes, Options)
            ?? throw new InvalidDataException("Empty replay fixture."); }
        catch (JsonException e) { throw new InvalidDataException("Corrupt replay fixture.", e); }
        if (fixture.SchemaVersion != 1 || fixture.Samples is null ||
            fixture.Samples.Length is < 2 or > 10_000 ||
            fixture.BusRole is not ("DutCan" or "KeypadCan"))
            throw new InvalidDataException("Unsupported replay fixture schema.");
        var signal = new SignalRef(new AssetRef(AssetId.Create(fixture.DbcAssetId),
            AssetRevision.Create(fixture.DbcRevision),
            RevisionFingerprint.FromSha256(fixture.DbcSha256)),
            Enum.Parse<BusRole>(fixture.BusRole), fixture.FrameId,
            fixture.SignalName, fixture.Unit);
        WaveTimeQuality.ValidateSignal(signal);
        var origin = new TimePoint(fixture.OriginUtc, 1_000_000,
            TimeSource.Replay, TimeQuality.ReplayDerived);
        var samples = fixture.Samples.Select(point =>
        {
            if (!double.IsFinite(point.Milliseconds) || point.Milliseconds < 0 ||
                point.Milliseconds > 3_600_000 || point.Evidence == Guid.Empty ||
                (point.Value.HasValue && !double.IsFinite(point.Value.Value)))
                throw new InvalidDataException("Invalid replay sample.");
            // Fixture-owned microsecond counter is synthetic; no claim about hardware tick units.
            var tick = checked(1_000_000 + (long)Math.Round(point.Milliseconds * 1_000));
            return new SignalObservation(signal, point.Value, signal.Unit,
                new TimePoint(fixture.OriginUtc.AddMilliseconds(point.Milliseconds), tick,
                    TimeSource.Replay, TimeQuality.ReplayDerived),
                EvidenceQuality.Derived, new EvidenceRef(point.Evidence), point.Gap);
        }).ToImmutableArray();
        return (signal, new WaveReplaySignalSource(signal, origin, samples));
    }
}
