using System.Collections.Immutable;
using AutoTest.Core;

namespace AutoTest.Contracts;

public enum RunFactKind { Request, CommandAck, MockObservation, Uncertainty,
    Rejection, Judgement, RecordWrite }
public sealed record RunFact(RunId Run, Guid EventId, Guid OperationId,
    RunFactKind Kind, string Description, TimePoint Time, EvidenceQuality Quality,
    ImmutableArray<EvidenceRef> Evidence);
public sealed record RecordWriteReceipt(RunId Run, Guid EventId, bool Durable,
    string PersistenceStatus);
public sealed record RecordCommitReceipt(RunId Run, bool Durable,
    string PersistenceStatus);
public sealed record RecordQuery(RunId Run, int MaxCount);
public interface IRunFactSink
{
    ValueTask<RecordWriteReceipt> AppendAsync(RunFact fact, CancellationToken ct);
    ValueTask<RecordCommitReceipt> CommitAsync(RunId run, CancellationToken ct);
}
public interface IRecordReader
{
    ValueTask<ImmutableArray<RunFact>> QueryAsync(RecordQuery query, CancellationToken ct);
}
