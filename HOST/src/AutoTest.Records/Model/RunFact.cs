using AutoTest.Contracts;

namespace AutoTest.Records.Model;

/// <summary>Validates contract facts without introducing a parallel public fact type.</summary>
public static class RunFactTimeline
{
    public static void Validate(RunFact fact)
    {
        ArgumentNullException.ThrowIfNull(fact);
        if (fact.Run.Value == Guid.Empty || fact.EventId == Guid.Empty || fact.OperationId == Guid.Empty ||
            string.IsNullOrWhiteSpace(fact.Description) || fact.Description.Length > 32768 ||
            fact.Time.Utc == default || fact.Time.Utc.Offset != TimeSpan.Zero ||
            fact.Evidence.IsDefault || !Enum.IsDefined(fact.Kind) || !Enum.IsDefined(fact.Quality) ||
            !Enum.IsDefined(fact.Time.Source) || !Enum.IsDefined(fact.Time.Quality))
            throw new InvalidDataException("RunFact is incomplete or not UTC.");
        if (fact.Evidence.Any(x => x.Value == Guid.Empty))
            throw new InvalidDataException("Empty evidence identity is not permitted.");
    }

    public static void ValidateSequence(IEnumerable<RunFact> facts, AutoTest.Core.RunId run)
    {
        var ids = new HashSet<Guid>();
        foreach (var fact in facts)
        {
            Validate(fact);
            if (fact.Run != run || !ids.Add(fact.EventId))
                throw new InvalidDataException("RunFact RunId mismatch or duplicate EventId.");
        }
    }
}


/// <summary>
/// A signal observation's immutable DBC binding, not an inferred live workspace reference.
/// Optional for older runs and for steps unrelated to CAN/DBC.
/// </summary>
public sealed record RunSignalProvenance(string BusRole, uint FrameId, string SignalName,
    string Unit, Guid DbcAssetId, int DbcRevision, string DbcSha256,
    DateTimeOffset ObservedUtc, long SourceMonotonicTicks,
    string TimeSource, string TimeQuality)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(BusRole) || BusRole.Length > 40 ||
            string.IsNullOrWhiteSpace(SignalName) || SignalName.Length > 256 ||
            (Unit?.Length ?? 0) > 128 ||
            DbcAssetId == Guid.Empty || DbcRevision < 1 ||
            DbcSha256 is not { Length: 64 } || !DbcSha256.All(Uri.IsHexDigit) ||
            ObservedUtc == default || ObservedUtc.Offset != TimeSpan.Zero ||
            string.IsNullOrWhiteSpace(TimeSource) || string.IsNullOrWhiteSpace(TimeQuality))
            throw new InvalidDataException("Incomplete real DBC signal provenance.");
    }
}

/// <summary>Record-owned structured step evidence until the shared runtime fact CT carries these fields.</summary>
public sealed record RunStepFact(AutoTest.Core.RunId Run, Guid EventId, AutoTest.Core.StepId Step,
    int Attempt, string Disposition, string? Expected, string? Actual,
    AutoTest.Core.TimePoint Time, AutoTest.Core.EvidenceQuality Quality,
    System.Collections.Immutable.ImmutableArray<AutoTest.Core.EvidenceRef> Evidence)
{
    // Optional v1 payload extension. Null means a legacy fact, never synthesize IDs.
    public Guid? StepOccurrenceId { get; init; }
    public Guid? ParentOccurrenceId { get; init; }
    public string? Phase { get; init; }
    public string? ExecutionPath { get; init; }
    public int? LoopIteration { get; init; }
    public DateTimeOffset? StartedUtc { get; init; }
    public DateTimeOffset? CompletedUtc { get; init; }
    public TimeSpan? Duration { get; init; }
    public DateTimeOffset? ObservationUtc { get; init; }
    public string? Unit { get; init; }
    public string? SignalStatus { get; init; }
    public string? FailureCode { get; init; }
    public string? Detail { get; init; }
    public string? RequirementId { get; init; }
    public string? ReportNote { get; init; }
    public string? AdditionalResultsJson { get; init; }
    public bool? ShowEvidence { get; init; }
    public RunSignalProvenance? SignalProvenance { get; init; }

    public void Validate()
    {
        SignalProvenance?.Validate();
        if (StepOccurrenceId is not null &&
            (StartedUtc is null || CompletedUtc is null || Duration is null ||
                Phase is null || string.IsNullOrWhiteSpace(ExecutionPath)))
            throw new InvalidDataException("An occurrence requires a phase, execution path and time bounds.");
        if (StepOccurrenceId == Guid.Empty || ParentOccurrenceId == Guid.Empty ||
            LoopIteration is < 1 ||
            Phase is not null and not ("Prepare" or "Test" or "Cleanup") ||
            (ExecutionPath?.Length ?? 0) > 4096 ||
            (Unit?.Length ?? 0) > 128 ||
            (SignalStatus?.Length ?? 0) > 128 ||
            (FailureCode?.Length ?? 0) > 128 ||
            (Detail?.Length ?? 0) > 4096 ||
            (RequirementId?.Length ?? 0) > 128 ||
            (ReportNote?.Length ?? 0) > 2048 ||
            (AdditionalResultsJson?.Length ?? 0) > 32768 ||
            StartedUtc is { } started && (started.Offset != TimeSpan.Zero ||
                CompletedUtc is { } ended && ended < started) ||
            CompletedUtc is { } completed && completed.Offset != TimeSpan.Zero ||
            ObservationUtc is { } observation && observation.Offset != TimeSpan.Zero ||
            Duration is { } duration && duration < TimeSpan.Zero)
            throw new InvalidDataException("Invalid step occurrence or measurement metadata.");
        if (Run.Value == Guid.Empty || EventId == Guid.Empty || Step.Value == Guid.Empty ||
            Attempt < 1 || string.IsNullOrWhiteSpace(Disposition) ||
            Time.Utc == default || Time.Utc.Offset != TimeSpan.Zero || Evidence.IsDefault ||
            (Expected?.Length ?? 0) > 32768 || (Actual?.Length ?? 0) > 32768 ||
            !Enum.IsDefined(Time.Source) || !Enum.IsDefined(Time.Quality) || !Enum.IsDefined(Quality))
            throw new InvalidDataException("Invalid structured run step fact.");
    }
}
