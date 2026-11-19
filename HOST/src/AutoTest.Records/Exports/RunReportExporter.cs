using System.Net;
using System.Text;
using System.Text.Json;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Records.Queries;
using AutoTest.Records.Model;
using AutoTest.Records.Storage;

namespace AutoTest.Records.Exports;

public sealed record ReportArtifact(Guid ArtifactId, string Kind, string Provenance,
    string Sha256, string RelativePath, ArtifactIntegrity Integrity);

public sealed record RunReportDocument(RunHistoryEntry Header,
    IReadOnlyList<RunFact> Facts, IReadOnlyList<RunStepFact> StepFacts,
    IReadOnlyList<ReportArtifact> Artifacts, string Html);

public sealed class RunReportExporter
{
    private readonly RecordDatabase _db;
    private readonly RunHistoryQuery _history;
    private readonly ArtifactRepository _artifacts;

    public RunReportExporter(RecordDatabase db, ArtifactRepository artifacts)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        _history = new RunHistoryQuery(db);
    }

    public async Task<RunReportDocument> ExportSummaryAsync(RunId run, CancellationToken ct = default)
    {
        var header = await _history.FindByRunAsync(run, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Unknown RunId {run}.");
        if (header.FactCount > 100000 || header.StepFactCount > 100000)
            throw new InvalidOperationException("Export requires paged record reader for this many facts.");
        var facts = await _db.ReadFactsAsync(new RecordQuery(run, (int)Math.Max(1, header.FactCount)), ct)
            .ConfigureAwait(false);
        if (facts.Length != header.FactCount) throw new InvalidDataException("Run history changed during report export.");
        var stepFacts = await _db.ReadStepFactsAsync(run, (int)Math.Max(1, header.StepFactCount), ct)
            .ConfigureAwait(false);
        if (stepFacts.Length != header.StepFactCount)
            throw new InvalidDataException("Step history changed during report export.");
        var refs = await _db.ReadArtifactsAsync(run, ct).ConfigureAwait(false);
        var status = new List<ReportArtifact>(refs.Length);
        foreach (var artifact in refs)
            status.Add(new ReportArtifact(artifact.ArtifactId, artifact.Kind, artifact.Provenance,
                artifact.Sha256, artifact.RelativePath,
                await _artifacts.VerifyAsync(artifact, ct).ConfigureAwait(false)));
        return new RunReportDocument(header, facts, stepFacts, status, RenderHtml(header, facts, stepFacts, status));
    }

    public async Task<string> ExportStepFactsAsync(RunId run, CancellationToken ct = default)
    {
        var report = await ExportSummaryAsync(run, ct).ConfigureAwait(false);
        var output = new StringBuilder("sequence,run_id,event_id,step_id,attempt,disposition,expected,actual,utc,time_source,time_quality,evidence_quality,evidence_ids\r\n");
        for (var i = 0; i < report.StepFacts.Count; i++)
        {
            var step = report.StepFacts[i];
            var cells = new[] { (i + 1).ToString(), step.Run.ToString(),
                step.EventId.ToString("D"), step.Step.ToString(), step.Attempt.ToString(),
                step.Disposition, step.Expected ?? "", step.Actual ?? "", step.Time.Utc.ToString("O"),
                step.Time.Source.ToString(), step.Time.Quality.ToString(), step.Quality.ToString(),
                string.Join(";", step.Evidence.Select(e => e.ToString())) };
            output.Append(string.Join(',', cells.Select(Csv))).Append("\r\n");
        }
        return output.ToString();
    }

    public async Task<string> ExportJsonAsync(RunId run, CancellationToken ct = default)
    {
        var report = await ExportSummaryAsync(run, ct).ConfigureAwait(false);
        using var snapshotDocument = JsonDocument.Parse(report.Header.SnapshotJson);
        return JsonSerializer.Serialize(new
        {
            schema = 1,
            run = report.Header.Run.ToString(),
            dut = report.Header.DutId,
            mode = report.Header.Mode.ToString(),
            persistence = report.Header.Persistence.ToString(),
            terminal = report.Header.TerminalState,
            verdict = report.Header.Verdict?.ToString(),
            started = report.Header.StartedUtc,
            completed = report.Header.CompletedUtc,
            snapshotHash = report.Header.SnapshotSha256,
            snapshot = snapshotDocument.RootElement,
            facts = report.Facts.Select((f, i) => new
            {
                sequence = i + 1, eventId = f.EventId, operationId = f.OperationId,
                kind = f.Kind.ToString(), description = f.Description, timestamp = f.Time.Utc,
                timeSource = f.Time.Source.ToString(), timeQuality = f.Time.Quality.ToString(),
                evidenceQuality = f.Quality.ToString(), evidenceIds = f.Evidence.Select(e => e.ToString())
            }),
            stepFacts = report.StepFacts.Select((f, i) => new
            {
                sequence = i + 1, eventId = f.EventId, stepId = f.Step.ToString(), attempt = f.Attempt,
                disposition = f.Disposition, expected = f.Expected, actual = f.Actual,
                timestamp = f.Time.Utc, timeQuality = f.Time.Quality.ToString(),
                evidenceQuality = f.Quality.ToString(), evidenceIds = f.Evidence.Select(e => e.ToString())
            }),
            artifacts = report.Artifacts.Select(a => new
            {
                id = a.ArtifactId, kind = a.Kind, provenance = a.Provenance,
                sha256 = a.Sha256, relativePath = a.RelativePath, integrity = a.Integrity.ToString()
            })
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string RenderHtml(RunHistoryEntry header, IReadOnlyList<RunFact> facts,
        IReadOnlyList<RunStepFact> stepFacts, IReadOnlyList<ReportArtifact> artifacts)
    {
        static string H(string? text) => WebUtility.HtmlEncode(text ?? "Unknown");
        var html = new StringBuilder("<!doctype html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\">");
        html.Append("<title>AutoTest Run Report</title><style>body{font:14px system-ui,sans-serif;max-width:1100px;margin:2rem auto;color:#242424}");
        html.Append("table{border-collapse:collapse;width:100%}td,th{padding:.5rem;border:1px solid #ccc;text-align:left;vertical-align:top}pre{white-space:pre-wrap;word-break:break-word}</style></head><body>");
        html.Append("<h1>AutoTest 执行报告</h1><p>运行状态、测试结论和归档状态相互独立；没有物理观测时不推定物理动作成功。</p>");
        html.Append("<table><tbody>");
        Row("RunId", header.Run.ToString()); Row("ECU 身份", header.DutId);
        Row("运行模式", header.Mode.ToString()); Row("存储提交状态", header.Persistence.ToString());
        Row("终态", header.TerminalState); Row("Verdict", header.Verdict?.ToString());
        Row("开始 UTC", header.StartedUtc.ToString("O")); Row("结束 UTC", header.CompletedUtc?.ToString("O"));
        Row("失败/中断", header.FailureReason); Row("RunSnapshot SHA-256", header.SnapshotSha256);
        html.Append("</tbody></table><h2>追加事实</h2><table><thead><tr><th>#</th><th>时间 UTC / 来源</th><th>类别</th><th>事件与观测</th><th>证据质量</th></tr></thead><tbody>");
        for (var i = 0; i < facts.Count; i++)
        {
            var fact = facts[i];
            html.Append("<tr><td>").Append(i + 1).Append("</td><td>").Append(H(fact.Time.Utc.ToString("O")))
                .Append(" / ").Append(H(fact.Time.Source.ToString())).Append(" / ").Append(H(fact.Time.Quality.ToString()))
                .Append("</td><td>").Append(H(fact.Kind.ToString())).Append("</td><td>").Append(H(fact.Description))
                .Append("<div>").Append(H(fact.EventId.ToString())).Append("</div></td><td>")
                .Append(H(fact.Quality.ToString())).Append("</td></tr>");
        }
        html.Append("</tbody></table><h2>逐步执行事实</h2><table><thead><tr><th>StepId</th><th>尝试</th><th>Disposition</th><th>预期</th><th>实际</th><th>证据等级</th></tr></thead><tbody>");
        foreach (var step in stepFacts)
            html.Append("<tr><td>").Append(H(step.Step.ToString())).Append("</td><td>").Append(step.Attempt)
                .Append("</td><td>").Append(H(step.Disposition)).Append("</td><td>")
                .Append(H(step.Expected)).Append("</td><td>").Append(H(step.Actual))
                .Append("</td><td>").Append(H(step.Quality.ToString())).Append("</td></tr>");
        html.Append("</tbody></table><h2>证据附件</h2><table><thead><tr><th>类型</th><th>来源</th><th>完整性</th><th>SHA-256</th><th>相对路径</th></tr></thead><tbody>");
        foreach (var a in artifacts)
            html.Append("<tr><td>").Append(H(a.Kind)).Append("</td><td>").Append(H(a.Provenance))
                .Append("</td><td>").Append(H(a.Integrity.ToString())).Append("</td><td>")
                .Append(H(a.Sha256)).Append("</td><td>").Append(H(a.RelativePath)).Append("</td></tr>");
        html.Append("</tbody></table><h2>运行时冻结的快照</h2><pre>").Append(H(header.SnapshotJson))
            .Append("</pre></body></html>");
        return html.ToString();

        void Row(string label, string? value) => html.Append("<tr><th>").Append(H(label))
            .Append("</th><td>").Append(H(value)).Append("</td></tr>");
    }

    private static string Csv(string text)
    {
        // Prevent formula injection on spreadsheet import while preserving data in quotes.
        if (text.Length > 0 && "=+-@".Contains(text[0])) text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
