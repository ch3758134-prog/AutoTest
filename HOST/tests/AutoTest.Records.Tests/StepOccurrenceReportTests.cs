using System.Collections.Immutable;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Records.Exports;
using AutoTest.Records.Model;
using Xunit;

namespace AutoTest.Records.Tests;

/// <summary>SQLite contract tests, not real ECU verification.</summary>
public sealed class StepOccurrenceReportTests
{
    [Fact]
    public async Task TwoOccurrencesOfOneStepRoundTripAndExportFromOneDatabase()
    {
        using var fixture = new TempFixture();
        var record = fixture.CreateRun("TEST-FIXTURE", RecordRunMode.Development);
        await fixture.Writer.BeginAsync(record);
        var stepId = StepId.New();
        var parent = Guid.NewGuid();
        var t0 = record.StartedUtc;
        for (var i = 0; i < 2; i++)
        {
            var step = new RunStepFact(record.RunId, Guid.NewGuid(), stepId, i + 1,
                "Completed", "2", "2", TimePoint.FromSimulation(t0.AddSeconds(i + 1), i + 1),
                EvidenceQuality.Derived, ImmutableArray<EvidenceRef>.Empty)
            {
                StepOccurrenceId = Guid.NewGuid(),
                ParentOccurrenceId = parent,
                Phase = "Test",
                ExecutionPath = "/loop/" + (i + 1),
                LoopIteration = i + 1,
                StartedUtc = t0.AddSeconds(i),
                CompletedUtc = t0.AddSeconds(i + 1),
                Duration = TimeSpan.FromSeconds(1),
                ObservationUtc = t0.AddSeconds(i),
                Unit = "count",
                SignalStatus = "Valid",
                RequirementId = "REQ-SIGNAL-01",
                ReportNote = "<script>not executable</script>",
                AdditionalResultsJson = "{\"measured\":{\"value\":2}}",
                ShowEvidence = true
            };
            await fixture.Writer.AppendStepAsync(step);
        }
        await fixture.Writer.CompleteAsync(new RunCompletion(record.RunId, t0.AddSeconds(4),
            RunState.Completed, Verdict.Inconclusive, "Synthetic unit test: no DUT judgement"));
        var reloaded = await fixture.Db.ReadStepFactsAsync(record.RunId);
        Assert.Equal(2, reloaded.Length);
        Assert.Equal(stepId, reloaded[0].Step);
        Assert.Equal(stepId, reloaded[1].Step);
        Assert.NotEqual(reloaded[0].StepOccurrenceId, reloaded[1].StepOccurrenceId);
        Assert.Equal(new[] { 1, 2 }, reloaded.Select(x => x.LoopIteration!.Value));
        Assert.Equal("REQ-SIGNAL-01", reloaded[0].RequirementId);
        Assert.Equal(TimeSpan.FromSeconds(1), reloaded[0].Duration);

        var exporter = new RunReportExporter(fixture.Db, fixture.Artifacts);
        var report = await exporter.ExportSummaryAsync(record.RunId);
        Assert.Contains("Occurrence:", report.Html);
        Assert.Contains("REQ-SIGNAL-01", report.Html);
        Assert.Contains("&lt;script&gt;", report.Html);
        Assert.DoesNotContain("<script>not executable</script>", report.Html);
        var json = await exporter.ExportJsonAsync(record.RunId);
        Assert.Contains("occurrenceId", json);
        Assert.Contains("durationMilliseconds", json);
        var csv = await exporter.ExportStepFactsAsync(record.RunId);
        Assert.Contains("occurrence_id", csv);
        Assert.Contains("REQ-SIGNAL-01", csv);

        var paths = await exporter.ExportFilesAsync(record.RunId,
            Path.Combine(fixture.ArtifactFolder, "exports"));
        Assert.True(File.Exists(paths.HtmlPath));
        Assert.True(File.Exists(paths.JsonPath));
        Assert.True(File.Exists(paths.CsvPath));
        Assert.Contains("REQ-SIGNAL-01", await File.ReadAllTextAsync(paths.HtmlPath));
    }

    [Fact]
    public async Task LegacyStepFactsRemainValidWithoutInventedOccurrenceId()
    {
        using var fixture = new TempFixture();
        var record = fixture.CreateRun("LEGACY-FIXTURE", RecordRunMode.Development);
        await fixture.Writer.BeginAsync(record);
        var fact = new RunStepFact(record.RunId, Guid.NewGuid(), StepId.New(), 1,
            "Executed", "yes", "unknown", TimePoint.FromSimulation(record.StartedUtc, 1),
            EvidenceQuality.Unknown, ImmutableArray<EvidenceRef>.Empty);
        await fixture.Writer.AppendStepAsync(fact);
        await fixture.Writer.AppendStepAsync(fact);
        var reloaded = Assert.Single(await fixture.Db.ReadStepFactsAsync(record.RunId));
        Assert.Null(reloaded.StepOccurrenceId);
        Assert.Null(reloaded.StartedUtc);
        Assert.Null(reloaded.Phase);
        Assert.Null(reloaded.SignalProvenance);
        Assert.Null(reloaded.JudgementEvidenceKind);
        Assert.Equal(fact, reloaded);
    }

    [Fact]
    public void InvalidOccurrenceIntervalsAreRejected()
    {
        var at = new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
        var fact = new RunStepFact(RunId.New(), Guid.NewGuid(), StepId.New(), 1,
            "Completed", "", "", TimePoint.FromSimulation(at, 1),
            EvidenceQuality.Unknown, ImmutableArray<EvidenceRef>.Empty)
        {
            StepOccurrenceId = Guid.NewGuid(),
            StartedUtc = at,
            CompletedUtc = at.AddSeconds(-1)
        };
        Assert.Throws<InvalidDataException>(() => fact.Validate());
        Assert.Throws<InvalidDataException>(() => (fact with
            { CompletedUtc = at.AddSeconds(1), StepOccurrenceId = Guid.Empty }).Validate());
    }
    [Fact]
    public void CannotClaimDirectJudgementWithoutOwnObservationOrEvidence()
    {
        var now = new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
        var fact = new RunStepFact(RunId.New(), Guid.NewGuid(), StepId.New(), 1,
            "Completed", "2", "2", TimePoint.FromHostArrival(now, 12),
            EvidenceQuality.HostObserved, ImmutableArray<EvidenceRef>.Empty)
        {
            StepOccurrenceId = Guid.NewGuid(),
            CaseAttemptId = Guid.NewGuid(),
            Phase = "Test",
            ExecutionPath = "/assert",
            StartedUtc = now.AddSeconds(-1),
            CompletedUtc = now,
            Duration = TimeSpan.FromSeconds(1),
            SignalStatus = "Valid",
            JudgementEvidenceKind = "DirectDbcSignal"
        };
        Assert.Throws<InvalidDataException>(() => fact.Validate());
        Assert.Throws<InvalidDataException>(() => (fact with
        {
            JudgementEvidenceKind = "UnsupportedFutureEvidence",
            Evidence = ImmutableArray.Create(EvidenceRef.New())
        }).Validate());
    }

}
