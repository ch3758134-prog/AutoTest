using System.Collections.Immutable;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using AutoTest.Application.V1;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Devices.Diagnostics;
using AutoTest.Projects.Project;
using AutoTest.Records.Model;
using AutoTest.Records.Storage;
using AutoTest.Testing.Lab;
using CoreVerdict = AutoTest.Core.Verdict;

namespace AutoTest.Desktop.Tools.Production;

/// <summary>
/// Stores an ACTUAL scanner-bound physical read-only TEST run in the same
/// append-only SQLite/Artifact store used by R1 Records (P13).
/// Classification is always Diagnostic: this adapter cannot authorize a
/// production release. No synthetic identity, measurements or ACKs.
/// </summary>
public sealed class R1PhysicalRunRecordBridge
{
    private readonly RecordDatabase _database;
    private readonly ArtifactRepository _artifacts;
    public R1PhysicalRunRecordBridge(RecordDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _artifacts = new ArtifactRepository(Path.Combine(
            Path.GetDirectoryName(database.DatabasePath)!, "artifacts"));
    }

    /// <summary>
    /// Production statistics are opted into by the PUBLISHED physical recipe,
    /// not inferred from ACKs, a UI label or a synthetic observation.
    /// A PASS must include a real observed ECU assertion with trace provenance.
    /// Failed/inconclusive physical runs remain production attempts in that mode.
    /// </summary>
    public static RecordRunMode ClassifyMode(
        AutoTest.Desktop.Tools.Testing.R1PhysicalScanBinding scan, LabRunReport report)
    {
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(report);
        if (report.Source != LabSource.Physical ||
            report.RunId == Guid.Empty || report.Facts.IsDefaultOrEmpty ||
            report.Facts.Any(f => f.Source != LabSource.Physical || f.RunId != report.RunId))
            throw new InvalidOperationException("PHYSICAL_PRODUCTION_FACTS_REQUIRED");
        var mode = scan.ProductionTestEnabled
            ? RecordRunMode.Production : RecordRunMode.Diagnostic;
        if (mode == RecordRunMode.Production &&
            report.Verdict == LabVerdict.Pass &&
            !report.Stopped &&
            !report.Facts.Any(f => f.NodeType == "Assert" &&
                f.Verdict == LabVerdict.Pass &&
                f.Quality == LabQuality.Observed &&
                Guid.TryParse(f.TraceReference, out var trace) && trace != Guid.Empty))
            throw new InvalidOperationException("PRODUCTION_PASS_WITHOUT_PHYSICAL_ASSERTION");
        if (mode == RecordRunMode.Production && report.Stopped &&
            report.Verdict == LabVerdict.Pass)
            throw new InvalidOperationException("STOPPED_RUN_CANNOT_PASS");
        return mode;
    }

    public async Task<RunId> PersistAsync(V1Workspace workspace,
        HardwareBenchController controller,
        AutoTest.Desktop.Tools.Testing.R1PhysicalScanBinding scan,
        LabRunReport report, string originalEvidenceFile,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(report);
        scan.Verify(workspace);
        if (report.Source != LabSource.Physical || report.RunId == Guid.Empty ||
            report.Facts.IsDefaultOrEmpty || string.IsNullOrWhiteSpace(originalEvidenceFile) ||
            !File.Exists(originalEvidenceFile))
            throw new InvalidOperationException("PHYSICAL_RUN_EVIDENCE_REQUIRED");
        var mode = ClassifyMode(scan, report);
        if (controller.State != AutoTest.Devices.Usb.UsbSessionState.Connected ||
            !controller.ProtocolVersionVerified || controller.Generation <= 0 ||
            string.IsNullOrWhiteSpace(controller.SelectedInstanceId))
            throw new InvalidOperationException("PHYSICAL_DEVICE_SESSION_NOT_VERIFIED");
        var projectPath = workspace.ProjectPath ??
            throw new InvalidOperationException("PROJECT_NOT_SAVED");
        var saved = await ProjectRepository.OpenDefinitionAsync(projectPath, ct);
        if (saved.Id != workspace.Project.Id || saved.Revision != workspace.Project.Revision ||
            saved.Assets.Length != workspace.Project.Assets.Length ||
            saved.Assets.Any(asset => !workspace.Project.Assets.Any(current =>
                asset.Id == current.Id && asset.Revision == current.Revision &&
                string.Equals(asset.Sha256, current.Sha256, StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException("UNSAVED_OR_CHANGED_PROJECT_ASSETS");
        var projectBytes = await File.ReadAllBytesAsync(projectPath, ct);
        var projectSnapshot = new ProjectSnapshot(saved.Id,
            new ProjectRevision(saved.Revision), RevisionFingerprint.FromBytes(projectBytes),
            saved.Assets.Select(x => x.ToReference()).ToImmutableArray(), saved.SchemaVersion);
        var roleRevision = RevisionFingerprint.FromBytes(
            Encoding.UTF8.GetBytes("MainController:CAN1=DUT;CAN0=KEY"));
        var station = new StationProfile(StationId.Create(controller.SelectedInstanceId),
            new BusRoleMap(roleRevision, 1, 0),
            "NOT_MEASURED_READ_ONLY_TEST", RevisionFingerprint.FromSha256(
                scan.StationProfileSha256));
        var locked = workspace.Assets.Where(x =>
            x.Id == workspace.LabProgramAssetId || x.Id == workspace.DbcAssetId)
            .Select(x => x.ToReference()).ToImmutableArray();
        if (locked.Length != 2)
            throw new InvalidOperationException("TEST_DBC_ASSET_LOCK_REQUIRED");

        var runId = RunId.Create(report.RunId);
        var snapshot = new RunSnapshot(runId, projectSnapshot, station,
            RevisionFingerprint.FromSha256(report.PlanFingerprint), locked);
        var ruleFingerprint = RevisionFingerprint.FromBytes(Encoding.UTF8.GetBytes(
            scan.RuleId + "|" + scan.CatalogRevision + "|" + scan.RecipeId +
            "|" + scan.RecipeRevision));
        var start = report.Facts.Min(f => f.AtUtc).ToUniversalTime();
        var record = RunRecord.CreateFromSnapshot(snapshot,
            new SessionIdentity(scan.UnitKey, ruleFingerprint.Sha256,
                "PhysicalSerialScanner"), mode,
            start, typeof(R1PhysicalRunRecordBridge).Assembly
                .GetName().Version?.ToString() ?? "AutoTest-R1-unknown-build");
        var writer = new RunRecordWriter(_database);
        await writer.BeginAsync(record, ct);
        foreach (var fact in report.Facts)
        {
            ct.ThrowIfCancellationRequested();
            var physicalRef = Guid.TryParse(fact.TraceReference, out var observedId) &&
                observedId != Guid.Empty
                    ? ImmutableArray.Create(new EvidenceRef(observedId))
                    : ImmutableArray<EvidenceRef>.Empty;
            var quality = fact.Quality == LabQuality.Observed
                ? physicalRef.IsEmpty ? EvidenceQuality.Derived : EvidenceQuality.HostObserved
                : EvidenceQuality.Unknown;
            // The LAB fact supplies host UTC but not its original monotonic
            // counter. Preserve the UTC and explicitly leave that counter
            // unavailable rather than inventing a measurement timestamp.
            var time = new TimePoint(fact.AtUtc.ToUniversalTime(), 0,
                TimeSource.HostArrival, TimeQuality.HostArrivalOnly);
            var narrative = string.Join(" | ",
                fact.Phase, fact.NodeType, "Expected=" + fact.Expected,
                "Actual=" + (fact.Actual ?? "(unavailable)"),
                "Result=" + fact.Verdict,
                "Diagnostic=" + (fact.Diagnostic ?? ""));
            if (narrative.Length > 32768) narrative = narrative[..32768];
            await writer.AppendAsync(new RunFact(runId, fact.OperationId,
                fact.OperationId, fact.Verdict == LabVerdict.Error
                    ? RunFactKind.Rejection : RunFactKind.Judgement,
                narrative, time, quality, physicalRef), ct);
            await writer.AppendStepAsync(new RunStepFact(runId,
                fact.OperationId, StepId.Create(fact.StepId), 1,
                fact.Verdict.ToString(), fact.Expected, fact.Actual,
                time, quality, physicalRef), ct);
        }
        await using (var source = new FileStream(originalEvidenceFile, FileMode.Open,
                         FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous))
        {
            var artifact = await _artifacts.PutAsync(source,
                "PhysicalCAN1TestRun", "application/json",
                "PhysicalSerialScanner+ProjectTEST+CAN1", ct: ct);
            await writer.AttachArtifactAsync(runId, artifact, ct);
        }
        var terminal = report.Stopped ? RunState.Aborted :
            report.Verdict == LabVerdict.Error ? RunState.Failed :
            RunState.Completed;
        var finalVerdict = Enum.TryParse<CoreVerdict>(report.Verdict.ToString(),
            out var mapped) ? mapped : CoreVerdict.Inconclusive;
        var completion = new RunCompletion(runId, DateTimeOffset.UtcNow,
            terminal, finalVerdict,
            terminal == RunState.Completed ? null : "TEST_" + report.Verdict);
        await writer.CompleteAsync(completion, ct);
        return runId;
    }
}
