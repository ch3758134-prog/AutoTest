using System.Collections.Immutable;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Records.Model;
using AutoTest.Records.Queries;

namespace AutoTest.Records.Storage;

/// <summary>Implements the approved fact sink and leaves verdict ownership with runtime/application.</summary>
public sealed class RunRecordWriter : IRunFactSink, IRecordReader
{
    private readonly RecordDatabase _database;
    public RunRecordWriter(RecordDatabase database) => _database = database ?? throw new ArgumentNullException(nameof(database));

    public Task BeginAsync(RunRecord record, CancellationToken ct = default) => _database.BeginAsync(record, ct);

    public async ValueTask<RecordWriteReceipt> AppendAsync(RunFact fact, CancellationToken ct) =>
        await _database.AppendAsync(fact, ct).ConfigureAwait(false);

    /// <summary>Preserves the interface contract without inventing a verdict from an ACK or fact list.</summary>
    public async ValueTask<RecordCommitReceipt> CommitAsync(RunId run, CancellationToken ct)
    {
        const string reason = "No verified run completion supplied to IRunFactSink.CommitAsync";
        var existing = await new RunHistoryQuery(_database).FindByRunAsync(run, ct).ConfigureAwait(false);
        if (existing is { Persistence: RecordPersistenceState.Committed,
                TerminalState: nameof(RunState.Unknown), Verdict: null, FailureReason: reason })
            return new RecordCommitReceipt(run, true, "AlreadyCommitted");
        return await _database.CommitAsync(new RunCompletion(run, DateTimeOffset.UtcNow,
            RunState.Unknown, null, reason), ct).ConfigureAwait(false);
    }

    public Task<RecordCommitReceipt> CompleteAsync(RunCompletion completion, CancellationToken ct = default) =>
        _database.CommitAsync(completion, ct);

    public async ValueTask<ImmutableArray<RunFact>> QueryAsync(RecordQuery query, CancellationToken ct) =>
        await _database.ReadFactsAsync(query, ct).ConfigureAwait(false);

    public Task AppendStepAsync(RunStepFact fact, CancellationToken ct = default) =>
        _database.AppendStepAsync(fact, ct);

    public Task AttachArtifactAsync(RunId run, ArtifactDescriptor artifact, CancellationToken ct = default) =>
        _database.AttachArtifactAsync(run, artifact, ct);
}
