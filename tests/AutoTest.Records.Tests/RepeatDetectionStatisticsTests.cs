using AutoTest.Core;
using AutoTest.Records.Model;
using AutoTest.Records.Queries;
using Xunit;

namespace AutoTest.Records.Tests;

/// <summary>Production statistics count history; no test equates historical PASS to physical shipment.</summary>
public sealed class RepeatDetectionStatisticsTests
{
    [Fact]
    public async Task ThreeRunsWithOneRepeat_KeepAttemptAndDutCountsSeparate()
    {
        using var f = new TempFixture();
        await SaveAtAsync(f, "ECU-A", 1);
        await SaveAtAsync(f, "ECU-B", 2);
        await SaveAtAsync(f, "ECU-A", 3);
        var summary = await f.Statistics.GetRepeatDetectionSummaryAsync();
        Assert.Equal(3, summary.RunCount);
        Assert.Equal(2, summary.DistinctDutCount);
        Assert.Equal(2, summary.FirstSeenAttempts);
        Assert.Equal(1, summary.RepeatAttempts);
        Assert.Equal(1, summary.RepeatedDutCount);
        Assert.Equal("NOT_EVALUATED_NO_RELEASE_CONTRACT", summary.ReleaseAssessment);
    }

    [Fact]
    public async Task ReportingWindow_DoesNotTurnOlderRepeatIntoFirstSeen()
    {
        using var f = new TempFixture();
        await SaveAtAsync(f, "ECU-A", 1);
        await SaveAtAsync(f, "ECU-A", 3);
        var start = UtcDay(3);
        var day = await f.Statistics.GetRepeatDetectionSummaryAsync(
            fromUtc: start, toUtc: start.AddDays(1).AddTicks(-1));
        Assert.Equal(1, day.RunCount);
        Assert.Equal(1, day.DistinctDutCount);
        Assert.Equal(0, day.FirstSeenAttempts);
        Assert.Equal(1, day.RepeatAttempts);
        Assert.Equal(1, day.RepeatedDutCount);

        var daily = await f.Statistics.GetDailyRepeatDetectionSummaryAsync(DateOnly.FromDateTime(start.UtcDateTime));
        Assert.Equal(day, daily);
    }

    [Fact]
    public async Task DifferentRunModes_DoNotCrossContaminateProductionStatistics()
    {
        using var f = new TempFixture();
        await SaveAtAsync(f, "ECU-A", 1, RecordRunMode.Development);
        await SaveAtAsync(f, "ECU-A", 2, RecordRunMode.Production);
        var production = await f.Statistics.GetRepeatDetectionSummaryAsync();
        var development = await f.Statistics.GetRepeatDetectionSummaryAsync(RecordRunMode.Development);
        Assert.Equal(1, production.FirstSeenAttempts);
        Assert.Equal(0, production.RepeatAttempts);
        Assert.Equal(1, development.FirstSeenAttempts);
        Assert.Equal(0, development.RepeatAttempts);
    }

    [Fact]
    public async Task EmptyDay_ReturnsZerosNotAnInferredPass()
    {
        using var f = new TempFixture();
        await SaveAtAsync(f, "ECU-A", 1);
        var empty = await f.Statistics.GetDailyRepeatDetectionSummaryAsync(new DateOnly(2026, 12, 25));
        Assert.Equal(0, empty.RunCount);
        Assert.Equal(0, empty.DistinctDutCount);
        Assert.Equal(0, empty.RepeatAttempts);
        Assert.Equal("NOT_EVALUATED_NO_RELEASE_CONTRACT", empty.ReleaseAssessment);
    }

    [Fact]
    public async Task NonUtcOrReversedReportingRange_IsRejected()
    {
        using var f = new TempFixture();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            f.Statistics.GetRepeatDetectionSummaryAsync(fromUtc:
                new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.FromHours(8))));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            f.Statistics.GetRepeatDetectionSummaryAsync(fromUtc: UtcDay(5), toUtc: UtcDay(4)));
    }


    [Fact]
    public async Task MonthlySnapshotKeepsHistoryAndRepeatCountsInOneResult()
    {
        using var f = new TempFixture();
        await SaveAtAsync(f, "ECU-A", 1);
        await SaveAtAsync(f, "ECU-B", 2);
        await SaveAtAsync(f, "ECU-A", 3);
        var month = await f.Statistics.GetMonthlyPeriodSummaryAsync(2026, 11);
        var baseline = await f.Statistics.GetSummaryAsync();
        Assert.Equal(baseline.RunCount, month.RunCount);
        Assert.Equal(baseline.DistinctDutCount, month.DistinctDutCount);
        Assert.Equal(baseline.CommittedRuns, month.CommittedRuns);
        Assert.Equal(baseline.PassedRuns, month.PassedRuns);
        Assert.Equal(2, month.FirstSeenAttempts);
        Assert.Equal(1, month.RepeatAttempts);
        Assert.Equal(1, month.RepeatedDutCount);
        Assert.Equal("NOT_EVALUATED_NO_RELEASE_CONTRACT", month.ReleaseAssessment);
    }

    [Fact]
    public async Task MonthlyRepeatLooksBackBeforeTheFirstDayOfTheMonth()
    {
        using var f = new TempFixture();
        var old = f.CreateRun("ECU-A") with
        {
            StartedUtc = new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero)
        };
        await f.Writer.BeginAsync(old);
        await f.Writer.CompleteAsync(new RunCompletion(old.RunId, old.StartedUtc.AddMinutes(1),
            RunState.Completed, Verdict.Pass, null));
        await SaveAtAsync(f, "ECU-A", 1);
        var nov = await f.Statistics.GetMonthlyPeriodSummaryAsync(2026, 11);
        Assert.Equal(1, nov.RunCount);
        Assert.Equal(0, nov.FirstSeenAttempts);
        Assert.Equal(1, nov.RepeatAttempts);
        Assert.Equal(1, nov.RepeatedDutCount);
        var oct = await f.Statistics.GetMonthlyPeriodSummaryAsync(2026, 10);
        Assert.Equal(1, oct.FirstSeenAttempts);
        Assert.Equal(0, oct.RepeatAttempts);
    }

    [Fact]
    public async Task PeriodSummaryRejectsNonUtcAndInvalidCalendarMonth()
    {
        using var f = new TempFixture();
        await Assert.ThrowsAsync<ArgumentException>(() => f.Statistics.GetPeriodSummaryAsync(
            fromUtc: new DateTimeOffset(2026, 11, 4, 8, 0, 0, TimeSpan.FromHours(8))));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            f.Statistics.GetMonthlyPeriodSummaryAsync(2026, 13));
        var empty = await f.Statistics.GetMonthlyPeriodSummaryAsync(2026, 11);
        Assert.Equal(0, empty.RunCount);
        Assert.Equal(0, empty.RepeatAttempts);
        Assert.Equal("NOT_EVALUATED_NO_RELEASE_CONTRACT", empty.ReleaseAssessment);
    }

    private static DateTimeOffset UtcDay(int day) =>
        new(2026, 11, day, 12, 0, 0, TimeSpan.Zero);

    private static async Task SaveAtAsync(TempFixture f, string dut, int day,
        RecordRunMode mode = RecordRunMode.Production)
    {
        var record = f.CreateRun(dut, mode) with { StartedUtc = UtcDay(day) };
        await f.Writer.BeginAsync(record);
        await f.Writer.CompleteAsync(new RunCompletion(record.RunId, record.StartedUtc.AddMinutes(1),
            RunState.Completed, Verdict.Pass, null));
    }
}
