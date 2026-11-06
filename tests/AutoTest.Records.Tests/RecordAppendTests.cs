using System.Collections.Immutable;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Records.Model;
using AutoTest.Records.Storage;
using Xunit;

namespace AutoTest.Records.Tests;

public sealed class RecordAppendTests
{
    [Fact]
    public async Task SameEventIdSamePayloadIsIdempotentButCollisionIsRejected()
    {
        using var fixture = new TempFixture();
        var run = fixture.CreateRun("ECU-A");
        await fixture.Writer.BeginAsync(run);
        var fact = fixture.Fact(run.RunId, 1);
        Assert.True((await fixture.Writer.AppendAsync(fact, default)).Durable);
        Assert.Equal("AlreadyDurable", (await fixture.Writer.AppendAsync(fact, default)).PersistenceStatus);
        var changed = fact with { Description = "conflicting duplicate" };
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Writer.AppendAsync(changed, default));
        var read = await fixture.Writer.QueryAsync(new RecordQuery(run.RunId, 10), default);
        Assert.Single(read);
        Assert.Equal(fact.EventId, read[0].EventId);
    }

    [Fact]
    public async Task CommittedRunCannotBeRewrittenOrAppended()
    {
        using var fixture = new TempFixture();
        var run = fixture.CreateRun("ECU-A");
        await fixture.Writer.BeginAsync(run);
        var completion = new RunCompletion(run.RunId, run.StartedUtc.AddMinutes(1), RunState.Completed, Verdict.Fail, null);
        Assert.True((await fixture.Writer.CompleteAsync(completion)).Durable);
        Assert.Equal("AlreadyCommitted", (await fixture.Writer.CompleteAsync(completion)).PersistenceStatus);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Writer.AppendAsync(fixture.Fact(run.RunId, 2), default));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Writer.BeginAsync(run));
    }

    [Fact]
    public async Task StructuredStepFactKeepsExpectedActualSeparate()
    {
        using var fixture = new TempFixture();
        var run = fixture.CreateRun("ECU-A");
        await fixture.Writer.BeginAsync(run);
        var step = new RunStepFact(run.RunId, Guid.NewGuid(), StepId.New(), 2, "Executed",
            "Voltage >= 10", "Unknown (Mock input not observed)",
            TimePoint.FromSimulation(run.StartedUtc, 2), EvidenceQuality.Unknown,
            ImmutableArray<EvidenceRef>.Empty);
        await fixture.Writer.AppendStepAsync(step);
        await fixture.Writer.AppendStepAsync(step); // retry is idempotent
        await fixture.Writer.CompleteAsync(new RunCompletion(run.RunId, run.StartedUtc.AddMinutes(1),
            RunState.Completed, Verdict.Inconclusive, null));
        var stored = await fixture.Db.ReadStepFactsAsync(run.RunId);
        Assert.Single(stored);
        Assert.Equal(step, stored[0]);
        var csv = await new AutoTest.Records.Exports.RunReportExporter(fixture.Db, fixture.Artifacts)
            .ExportStepFactsAsync(run.RunId);
        Assert.Contains("Voltage >= 10", csv);
        Assert.Contains("Unknown (Mock input not observed)", csv);
        Assert.Equal(1, (await fixture.History.FindByRunAsync(run.RunId))!.StepFactCount);
    }

    [Fact]
    public async Task MissingRunDoesNotProduceDurableReceipt()
    {
        using var fixture = new TempFixture();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fixture.Writer.AppendAsync(fixture.Fact(RunId.New(), 1), default));
    }

    [Fact]
    public async Task PublicFactSinkCommitDoesNotGuessTestOutcome()
    {
        using var fixture = new TempFixture();
        var run = fixture.CreateRun("ECU-A");
        await fixture.Writer.BeginAsync(run);
        IRunFactSink sink = fixture.Writer;
        var receipt = await sink.CommitAsync(run.RunId, default);
        Assert.True(receipt.Durable);
        var history = await fixture.History.FindByRunAsync(run.RunId);
        Assert.Equal("Unknown", history!.TerminalState);
        Assert.Null(history.Verdict);
    }

    [Fact]
    public async Task IncompleteSnapshotAndNonUtcFactAreRejected()
    {
        using var fixture = new TempFixture();
        var record = fixture.CreateRun("ECU-A");
        Assert.Throws<InvalidDataException>(() => (record with
            { Identity = new SessionIdentity("", "rule", "MOCK") }).Validate());
        await fixture.Writer.BeginAsync(record);
        var fact = fixture.Fact(record.RunId, 1);
        var nonUtc = fact with { Time = fact.Time with { Utc = DateTimeOffset.Now.ToOffset(TimeSpan.FromHours(3)) } };
        await Assert.ThrowsAsync<InvalidDataException>(async () => await fixture.Writer.AppendAsync(nonUtc, default));
    }
}
