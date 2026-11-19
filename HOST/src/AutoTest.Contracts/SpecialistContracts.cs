using System.Collections.Immutable;
using AutoTest.Core;

namespace AutoTest.Contracts;

public sealed record InputPolicy(bool RequireFocus, int MaxLength, string InputOrigin);
public sealed record RawScan(string Text, string Origin, TimePoint Time, bool HadFocus);
public sealed record BarcodeScanEvent(RawScan Scan, EvidenceRef Evidence);
public sealed record RuleRevision(RevisionFingerprint Fingerprint);
public sealed record MatchDecision(bool Allowed, string? DutIdentity,
    ImmutableArray<AssetRef> MatchedAssets, string? RejectionCode);
public sealed record UdsRequest(ImmutableArray<byte> Pdu, Guid CorrelationId);
public sealed record UdsEndpointProfile(BusRole Role, uint RequestCanId, uint ResponseCanId,
    TimeSpan P2, TimeSpan P2Star, string Addressing);
public sealed record UdsExchangeResult(Outcome Outcome, ImmutableArray<byte> RequestPdu,
    ImmutableArray<byte> ResponsePdu, byte? NegativeResponseCode,
    ImmutableArray<EvidenceRef> Evidence, TimePoint CompletedAt);
public sealed record WaveCaptureConfig(TimeSpan Duration, int MaxSamples);
public sealed record WaveSample(double Value, string Unit, TimePoint Time, bool Gap);
public sealed record WaveEvidenceRef(EvidenceRef Evidence, RevisionFingerprint SourceRevision,
    string Unit, TimeQuality Quality);
public sealed record ReferenceRef(AssetRef Asset);
public sealed record CompareProfile(double Tolerance, double MinimumCoverage);
public sealed record ComparisonResult(Verdict Verdict, double? Coverage, string Reason,
    ImmutableArray<EvidenceRef> Evidence);
public sealed record ProgrammingRequest(StationId Station, string Target,
    AssetRef Image, RevisionFingerprint ProfileRevision);
public sealed record ProgrammingReadiness(bool Ready, ImmutableArray<string> Blockers);
public sealed record ProgrammingPlan(ProgrammingRequest Request,
    ImmutableArray<string> LockedStages, RevisionFingerprint PlanFingerprint);
public readonly record struct ProgrammingJobId(Guid Value);

public interface IBarcodeInput
{
    IAsyncEnumerable<BarcodeScanEvent> ObserveAsync(InputPolicy policy, CancellationToken ct);
}
public interface IIdentityMatcher
{
    MatchDecision Match(RawScan rawScan, RuleRevision ruleRevision,
        ProjectRevision projectRevision);
}
public interface IUdsClient
{
    ValueTask<UdsExchangeResult> RequestAsync(UdsRequest request,
        UdsEndpointProfile profile, CancellationToken ct);
}
public interface IWaveformService
{
    ValueTask<WaveEvidenceRef> CaptureAsync(SignalRef signal, WaveCaptureConfig config,
        CancellationToken ct);
    ValueTask<ComparisonResult> CompareAsync(WaveEvidenceRef evidence,
        ReferenceRef reference, CompareProfile profile);
}
public interface IProgrammingService
{
    ValueTask<ProgrammingReadiness> PreflightAsync(ProgrammingRequest request,
        CancellationToken ct);
    ValueTask<ProgrammingJobId> ExecuteAsync(ProgrammingPlan plan,
        SafetyAuthorization authorization, CancellationToken ct);
}
