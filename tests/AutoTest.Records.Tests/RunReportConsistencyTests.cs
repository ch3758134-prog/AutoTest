using System.Collections.Immutable;
using System.Text.Json;
using AutoTest.Core;
using AutoTest.Records.Exports;
using AutoTest.Records.Model;
using AutoTest.Records.Storage;
using Xunit;

namespace AutoTest.Records.Tests;

/// <summary>One persisted run must project the same facts/steps/artifact integrity in both reports.</summary>
public sealed class RunReportConsistencyTests
{
    [Fact]
    public async Task JsonHtmlAndStepCsvDescribeTheSameFrozenRun()
    {
        using var f = new TempFixture();
        var run = f.CreateRun("ECU-RPT");
        await f.Writer.BeginAsync(run);
        await f.Writer.AppendAsync(f.Fact(run.RunId, 1), default);
        await f.Writer.AppendAsync(f.Fact(run.RunId, 2), default);
        var step = new RunStepFact(run.RunId, Guid.NewGuid(), StepId.New(), 1, "Executed",
            "=1+1, expected", "\"actual\" from mock",
            TimePoint.FromSimulation(run.StartedUtc, 2), EvidenceQuality.Unknown,
            ImmutableArray<EvidenceRef>.Empty);
        await f.Writer.AppendStepAsync(step);
        await f.Writer.CompleteAsync(new RunCompletion(run.RunId, run.StartedUtc.AddMinutes(1),
            RunState.Completed, Verdict.Inconclusive, "No physical measurement"));

        var exporter = new RunReportExporter(f.Db, f.Artifacts);
        var summary = await exporter.ExportSummaryAsync(run.RunId);
        using var json = JsonDocument.Parse(await exporter.ExportJsonAsync(run.RunId));
        var root = json.RootElement;
        Assert.Equal(run.RunId.ToString(), root.GetProperty("run").GetString());
        Assert.Equal(run.CanonicalSnapshotJson(), summary.Header.SnapshotJson);
        Assert.Equal(2, summary.Header.FactCount);
        Assert.Equal(2, root.GetProperty("facts").GetArrayLength());
        Assert.Equal(1, summary.Header.StepFactCount);
        Assert.Equal(1, root.GetProperty("stepFacts").GetArrayLength());
        Assert.Equal("Inconclusive", root.GetProperty("verdict").GetString());
        Assert.Equal("=1+1, expected",
            root.GetProperty("stepFacts")[0].GetProperty("expected").GetString());
        Assert.Equal("\"actual\" from mock",
            root.GetProperty("stepFacts")[0].GetProperty("actual").GetString());
        Assert.Contains(step.Step.ToString(), summary.Html);
        Assert.Contains("Inconclusive", summary.Html);
        Assert.Contains("No physical measurement", summary.Html);
        var csv = await exporter.ExportStepFactsAsync(run.RunId);
        Assert.Contains("\"'=1+1, expected\"", csv);
        Assert.Contains("\"\"\"actual\"\" from mock\"", csv);
        Assert.Contains(step.Step.ToString(), csv);
    }

    [Fact]
    public async Task MissingEvidenceIsReportedInJsonAndHtmlWithoutRewritingTheVerdict()
    {
        using var f = new TempFixture();
        var run = f.CreateRun("ECU-MISSING");
        await f.Writer.BeginAsync(run);
        await using (var data = new MemoryStream(new byte[] { 0x10, 0x20, 0x30 }))
        {
            var descriptor = await f.Artifacts.PutAsync(data, "CanTrace",
                "application/octet-stream", "Simulation");
            await f.Writer.AttachArtifactAsync(run.RunId, descriptor);
            var fullPath = Path.Combine(f.ArtifactFolder,
                descriptor.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            File.Delete(fullPath);
        }
        await f.Writer.CompleteAsync(new RunCompletion(run.RunId, run.StartedUtc.AddMinutes(1),
            RunState.Completed, Verdict.Pass, null));
        var exporter = new RunReportExporter(f.Db, f.Artifacts);
        var summary = await exporter.ExportSummaryAsync(run.RunId);
        Assert.Equal(ArtifactIntegrity.Missing, Assert.Single(summary.Artifacts).Integrity);
        using var json = JsonDocument.Parse(await exporter.ExportJsonAsync(run.RunId));
        var artifact = Assert.Single(json.RootElement.GetProperty("artifacts").EnumerateArray());
        Assert.Equal("Missing", artifact.GetProperty("integrity").GetString());
        Assert.Equal("Pass", json.RootElement.GetProperty("verdict").GetString());
        Assert.Contains("Missing", summary.Html);
        Assert.Contains("没有物理观测时不推定物理动作成功", summary.Html);
    }
}
