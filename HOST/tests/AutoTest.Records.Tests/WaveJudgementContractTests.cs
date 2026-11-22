using System.IO;
using System.Collections.Immutable;
using AutoTest.Core;
using AutoTest.Records.Model;
using Xunit;

namespace AutoTest.Records.Tests;

public sealed class WaveJudgementContractTests
{
    private static RunStepFact ValidPendingWave()
    {
        var now = DateTimeOffset.UtcNow;
        return new RunStepFact(RunId.New(), Guid.NewGuid(), StepId.New(), 1,
            "Completed", "Pass", "Pass",
            TimePoint.FromHostArrival(now, System.Diagnostics.Stopwatch.GetTimestamp()),
            EvidenceQuality.HostObserved,
            ImmutableArray.Create(EvidenceRef.New(), EvidenceRef.New()))
        {
            StepOccurrenceId = Guid.NewGuid(),
            CaseAttemptId = Guid.NewGuid(),
            Phase = "Test",
            ExecutionPath = "Test/1",
            StartedUtc = now.AddSeconds(-1),
            CompletedUtc = now,
            Duration = TimeSpan.FromSeconds(1),
            JudgementEvidenceKind = "WaveComparisonPendingArchive"
        };
    }

    [Fact]
    public void ValidWavePairCanBePersistedAsPendingButNotClaimVerified()
    {
        var fact = ValidPendingWave();
        fact.Validate();
        Assert.Equal("WaveComparisonPendingArchive", fact.JudgementEvidenceKind);
    }

    [Fact]
    public void SameEvidenceReferencedTwiceCannotBecomeWaveJudgement()
    {
        var fact = ValidPendingWave();
        Assert.Throws<InvalidDataException>(() =>
            (fact with { Evidence = ImmutableArray.Create(fact.Evidence[0], fact.Evidence[0]) })
            .Validate());
    }

    [Theory]
    [InlineData("Failed", "Pass")]
    [InlineData("Completed", "Fail")]
    public void WaveFailureOrNonPassingAssertionMustNotClaimPositiveProof(
        string disposition, string actual)
    {
        var fact = ValidPendingWave();
        Assert.Throws<InvalidDataException>(() =>
            (fact with { Disposition = disposition, Actual = actual }).Validate());
    }

    [Fact]
    public void MissingOccurrenceOrCaseIdentityPreventsPendingWaveClaim()
    {
        var fact = ValidPendingWave();
        Assert.Throws<InvalidDataException>(() =>
            (fact with { StepOccurrenceId = null }).Validate());
        Assert.Throws<InvalidDataException>(() =>
            (fact with { CaseAttemptId = null }).Validate());
    }

    [Fact]
    public void WaveMarkerCannotBeUsedToBypassDirectDbcProofRequirements()
    {
        var fact = ValidPendingWave();
        Assert.Throws<InvalidDataException>(() =>
            (fact with { JudgementEvidenceKind = "DirectDbcSignal" }).Validate());
    }
}
