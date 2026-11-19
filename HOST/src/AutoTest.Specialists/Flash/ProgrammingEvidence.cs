using System.Collections.Immutable;

namespace AutoTest.Specialists.Flash;

public enum ProgrammingJobState { Queued, Running, Completed, Failed, Cancelled, Uncertain }
public sealed record ProgrammingStageFact(DateTimeOffset Utc,string Stage,string Status,
    long ConfirmedBytes,string Detail);
public sealed record ProgrammingJobSnapshot(Guid Job,ProgrammingJobState State,
    ImmutableArray<ProgrammingStageFact> Facts,string? FailureCode);

// In-memory facts are simulation evidence, NOT persisted Run facts or measured ECU writes.
public sealed class ProgrammingEvidence(Guid id)
{
    private readonly object _sync = new();
    private readonly List<ProgrammingStageFact> _facts = [];
    private ProgrammingJobState _state=ProgrammingJobState.Queued;
    private string? _failure;
    public void RecordStage(string stage,string status,long confirmedBytes,string detail="")
    {
        lock(_sync) _facts.Add(new(DateTimeOffset.UtcNow,stage,status,confirmedBytes,detail));
    }
    public void Finalize(ProgrammingJobState state,string? failure = null)
    {
        lock(_sync)
        {
            if (_state is ProgrammingJobState.Completed or ProgrammingJobState.Failed
                or ProgrammingJobState.Cancelled or ProgrammingJobState.Uncertain)
                throw new InvalidOperationException("Programming job is already terminal.");
            _state = state; _failure = failure;
        }
    }
    public ProgrammingJobSnapshot Observe()
    {
        lock(_sync) return new(id,_state,_facts.ToImmutableArray(),_failure);
    }
}
