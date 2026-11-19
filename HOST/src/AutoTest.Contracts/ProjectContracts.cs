using System.Collections.Immutable;
using AutoTest.Core;

namespace AutoTest.Contracts;

public readonly record struct ProjectRevision(int Value);
public sealed record ProjectSnapshot(ProjectId Id, ProjectRevision Revision,
    RevisionFingerprint Fingerprint, ImmutableArray<AssetRef> Assets, int SchemaVersion);
public sealed record ProjectSaveRequest(ProjectSnapshot ExpectedSnapshot, string Path,
    RevisionFingerprint ExpectedOnDisk);
public sealed record SaveReceipt(ProjectId Project, RevisionFingerprint WrittenFingerprint,
    bool Durable, string Path);
public sealed record SignalRef(AssetRef DbcAsset, BusRole Role, uint FrameId,
    string SignalName, string Unit);
public sealed record SignalObservation(SignalRef Signal, double? Value, string Unit,
    TimePoint Time, EvidenceQuality Quality, EvidenceRef Evidence, bool HasGap);
public interface IProjectService
{
    ValueTask<OperationResult<ProjectSnapshot>> OpenAsync(string path, CancellationToken ct);
    ValueTask<OperationResult<SaveReceipt>> SaveAsync(ProjectSaveRequest request, CancellationToken ct);
}
public interface IAssetResolver
{
    OperationResult<AssetRef> Resolve(AssetId id, AssetRevision revision);
}
public interface IDbcDecoder
{
    SignalObservation Decode(SignalRef signal, CanObservation observation);
}
