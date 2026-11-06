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

/// <summary>Record-owned structured step evidence until the shared runtime fact CT carries these fields.</summary>
public sealed record RunStepFact(AutoTest.Core.RunId Run, Guid EventId, AutoTest.Core.StepId Step,
    int Attempt, string Disposition, string? Expected, string? Actual,
    AutoTest.Core.TimePoint Time, AutoTest.Core.EvidenceQuality Quality,
    System.Collections.Immutable.ImmutableArray<AutoTest.Core.EvidenceRef> Evidence)
{
    public void Validate()
    {
        if (Run.Value == Guid.Empty || EventId == Guid.Empty || Step.Value == Guid.Empty ||
            Attempt < 1 || string.IsNullOrWhiteSpace(Disposition) ||
            Time.Utc == default || Time.Utc.Offset != TimeSpan.Zero || Evidence.IsDefault ||
            (Expected?.Length ?? 0) > 32768 || (Actual?.Length ?? 0) > 32768 ||
            !Enum.IsDefined(Time.Source) || !Enum.IsDefined(Time.Quality) || !Enum.IsDefined(Quality))
            throw new InvalidDataException("Invalid structured run step fact.");
    }
}
