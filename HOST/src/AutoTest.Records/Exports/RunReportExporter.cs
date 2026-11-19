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

public sealed record ExportedRunReportFiles(string HtmlPath, string JsonPath, string CsvPath);

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
        var archived = report.Artifacts.Where(a => a.Integrity == ArtifactIntegrity.Present)
            .Select(a => a.ArtifactId).ToHashSet();
        var output = new StringBuilder("sequence,run_id,event_id,step_id,attempt,disposition,expected,actual,utc,time_source,time_quality,evidence_quality,evidence_ids,occurrence_id,parent_occurrence_id,phase,loop_iteration,path,started_utc,completed_utc,duration_ms,observation_utc,unit,signal_status,requirement_id,report_note,extra_results_json,failure_code,detail,unarchived_evidence_ids,dbc_asset_id,dbc_revision,dbc_sha256,bus_role,frame_id,signal_name,source_ticks,source_clock,source_time_quality\r\n");
        for (var i = 0; i < report.StepFacts.Count; i++)
        {
            var step = report.StepFacts[i];
            var cells = new[] { (i + 1).ToString(), step.Run.ToString(),
                step.EventId.ToString("D"), step.Step.ToString(), step.Attempt.ToString(),
                step.Disposition, step.Expected ?? "", step.Actual ?? "", step.Time.Utc.ToString("O"),
                step.Time.Source.ToString(), step.Time.Quality.ToString(), step.Quality.ToString(),
                string.Join(";", step.Evidence.Select(e => e.ToString())),
                step.StepOccurrenceId?.ToString("D") ?? "",
                step.ParentOccurrenceId?.ToString("D") ?? "",
                step.Phase ?? "", step.LoopIteration?.ToString() ?? "",
                step.ExecutionPath ?? "", step.StartedUtc?.ToString("O") ?? "",
                step.CompletedUtc?.ToString("O") ?? "",
                step.Duration is { } elapsed
                    ? elapsed.TotalMilliseconds.ToString("F3",
                        System.Globalization.CultureInfo.InvariantCulture) : "",
                step.ObservationUtc?.ToString("O") ?? "", step.Unit ?? "",
                step.SignalStatus ?? "", step.RequirementId ?? "",
                step.ReportNote ?? "", step.AdditionalResultsJson ?? "",
                step.FailureCode ?? "", step.Detail ?? "",
                string.Join(";", step.Evidence.Where(e => !archived.Contains(e.Value))
                    .Select(e => e.ToString())),
                step.SignalProvenance?.DbcAssetId.ToString("D") ?? "",
                step.SignalProvenance?.DbcRevision.ToString() ?? "",
                step.SignalProvenance?.DbcSha256 ?? "",
                step.SignalProvenance?.BusRole ?? "",
                step.SignalProvenance is { } origin ? "0x" + origin.FrameId.ToString("X") : "",
                step.SignalProvenance?.SignalName ?? "",
                step.SignalProvenance?.SourceMonotonicTicks.ToString() ?? "",
                step.SignalProvenance?.TimeSource ?? "",
                step.SignalProvenance?.TimeQuality ?? "" };
            output.Append(string.Join(',', cells.Select(Csv))).Append("\r\n");
        }
        return output.ToString();
    }

    public async Task<string> ExportJsonAsync(RunId run, CancellationToken ct = default)
    {
        var report = await ExportSummaryAsync(run, ct).ConfigureAwait(false);
        using var snapshotDocument = JsonDocument.Parse(report.Header.SnapshotJson);
        var archived = report.Artifacts.Where(a => a.Integrity == ArtifactIntegrity.Present)
            .Select(a => a.ArtifactId).ToHashSet();
        var missingArchive = report.StepFacts.SelectMany(f => f.Evidence)
            .Concat(report.Facts.SelectMany(f => f.Evidence))
            .Where(e => !archived.Contains(e.Value))
            .Select(e => e.Value).Distinct().ToArray();
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
            evidenceArchiveIncomplete = missingArchive.Length > 0,
            unarchivedEvidenceIds = missingArchive,
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
                occurrenceId = f.StepOccurrenceId, parentOccurrenceId = f.ParentOccurrenceId,
                phase = f.Phase, loopIteration = f.LoopIteration, path = f.ExecutionPath,
                startedUtc = f.StartedUtc, completedUtc = f.CompletedUtc,
                durationMilliseconds = f.Duration?.TotalMilliseconds,
                observationUtc = f.ObservationUtc, unit = f.Unit, signalStatus = f.SignalStatus,
                failureCode = f.FailureCode, detail = f.Detail,
                signalProvenance = f.SignalProvenance,
                unarchivedEvidenceIds = f.Evidence.Where(e => !archived.Contains(e.Value))
                    .Select(e => e.Value),
                requirementId = f.RequirementId, reportNote = f.ReportNote,
                additionalResultsJson = f.AdditionalResultsJson, showEvidence = f.ShowEvidence,
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

    /// <summary>Writes all human-readable outputs from one committed source, atomically per file.</summary>
    public async Task<ExportedRunReportFiles> ExportFilesAsync(RunId run, string directory,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var report = await ExportSummaryAsync(run, ct).ConfigureAwait(false);
        if (report.Header.Persistence == RecordPersistenceState.Writing)
            throw new InvalidOperationException("A live Writing run cannot produce final report files.");
        var json = await ExportJsonAsync(run, ct).ConfigureAwait(false);
        var csv = await ExportStepFactsAsync(run, ct).ConfigureAwait(false);
        var root = Path.GetFullPath(directory);
        Directory.CreateDirectory(root);
        var stem = run.Value.ToString("N");
        var htmlPath = Path.Combine(root, stem + ".html");
        var jsonPath = Path.Combine(root, stem + ".json");
        var csvPath = Path.Combine(root, stem + "-steps.csv");
        await AtomicWriteAsync(htmlPath, report.Html, ct).ConfigureAwait(false);
        await AtomicWriteAsync(jsonPath, json, ct).ConfigureAwait(false);
        await AtomicWriteAsync(csvPath, csv, ct).ConfigureAwait(false);
        return new ExportedRunReportFiles(htmlPath, jsonPath, csvPath);
    }

    private static async Task AtomicWriteAsync(string path, string contents, CancellationToken ct)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                var bytes = Encoding.UTF8.GetBytes(contents);
                await output.WriteAsync(bytes, ct).ConfigureAwait(false);
                await output.FlushAsync(ct).ConfigureAwait(false);
                output.Flush(true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
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
        var archived = artifacts.Where(a => a.Integrity == ArtifactIntegrity.Present)
            .Select(a => a.ArtifactId).ToHashSet();
        var unresolved = stepFacts.SelectMany(step => step.Evidence)
            .Concat(facts.SelectMany(f => f.Evidence))
            .Where(e => !archived.Contains(e.Value))
            .Select(e => e.Value).Distinct().ToArray();
        html.Append("</tbody></table>");
        if (unresolved.Length > 0)
            html.Append("<p><strong>证据归档不完整：</strong>本次运行有 ")
                .Append(unresolved.Length)
                .Append(" 个证据 ID 未绑定可验证的归档附件；这些 ID 不能作为完整原始 CAN/DBC 证据。</p>");
        html.Append("<h2>逐步执行事实</h2><table><thead><tr><th>StepId / Occurrence</th><th>阶段 / 循环</th><th>结果 / 质量</th><th>预期</th><th>实测值 / 单位</th><th>时间范围 / 耗时</th><th>附加结果 / 备注</th></tr></thead><tbody>");
        foreach (var step in stepFacts)
        {
            html.Append("<tr><td>").Append(H(step.Step.ToString())).Append("<div>Occurrence: ")
                .Append(H(step.StepOccurrenceId?.ToString("D") ?? "Legacy (not recorded)"))
                .Append("</div><div>Parent: ").Append(H(step.ParentOccurrenceId?.ToString("D")))
                .Append("</div></td><td>").Append(H(step.Phase))
                .Append(" / ").Append(H(step.LoopIteration?.ToString()))
                .Append("<div>").Append(H(step.ExecutionPath)).Append("</div></td><td>")
                .Append(H(step.Disposition)).Append(" / ").Append(H(step.Quality.ToString()))
                .Append("<div>Signal: ").Append(H(step.SignalStatus)).Append("</div>")
                .Append("<div>Failure: ").Append(H(step.FailureCode)).Append("</div>")
                .Append("<div>").Append(H(step.Detail)).Append("</div></td><td>")
                .Append(H(step.Expected)).Append("</td><td>").Append(H(step.Actual))
                .Append(" ").Append(H(step.Unit));
            if (step.SignalProvenance is { } source)
                html.Append("<div>DBC: ").Append(H(source.DbcAssetId.ToString("D")))
                    .Append(" @ ").Append(source.DbcRevision)
                    .Append(" / ").Append(H(source.DbcSha256))
                    .Append("</div><div>CAN: ").Append(H(source.BusRole))
                    .Append(" 0x").Append(source.FrameId.ToString("X"))
                    .Append(" / ").Append(H(source.SignalName))
                    .Append("</div><div>观测时钟: ").Append(H(source.TimeSource))
                    .Append(" / ").Append(H(source.TimeQuality)).Append("</div>");
            html.Append("</td><td>")
                .Append(H(step.StartedUtc?.ToString("O"))).Append(" — ")
                .Append(H(step.CompletedUtc?.ToString("O"))).Append("<div>")
                .Append(H(step.Duration is { } duration
                    ? duration.TotalMilliseconds.ToString("F3",
                        System.Globalization.CultureInfo.InvariantCulture) : null))
                .Append(" ms</div></td><td>")
                .Append(H(step.RequirementId)).Append("<div>")
                .Append(H(step.ReportNote)).Append("</div><pre>")
                .Append(H(step.AdditionalResultsJson)).Append("</pre>")
                .Append(step.ShowEvidence == false ? "" :
                    "<div>Evidence IDs: " + H(string.Join("; ",
                        step.Evidence.Select(e => e.ToString()))) + "</div>")
                .Append(step.Evidence.Any(e => !archived.Contains(e.Value))
                    ? "<div>证据归档：未验证</div>" : "")
                .Append("</td></tr>");
        }
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
