using AutoTest.Desktop.Hardware;
using System.IO;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Application.TestRecord;
using AutoTest.Records.Exports;
using AutoTest.Records.Model;
using AutoTest.Records.Storage;
using AutoTest.Desktop.Projects;
using AutoTest.Testing.Authoring;
using AutoTest.Testing.Preparation;
using AutoTest.Testing.Runtime;

namespace AutoTest.Desktop.Testing;

/// <summary>
/// Single station execution entry. The source TEST file is the only authored program.
/// No mock fallback. Real writes are permitted only after an operator enters the
/// exact fingerprint confirmation for this prepared TEST snapshot.
/// </summary>
public sealed class RealTestWorkbenchService
{
    private readonly HardwareConnectionService _hardware;
    private readonly ProjectWorkspaceService _projects;
    private readonly LiveDbcSignalService _signals;
    private readonly SemaphoreSlim _station = new(1, 1);
    private RealRunCoordinator? _running;

    public RealTestWorkbenchService(HardwareConnectionService hardware,
        ProjectWorkspaceService projects, LiveDbcSignalService signals)
    {
        _hardware = hardware;
        _projects = projects;
        _signals = signals;
    }

    public bool IsRunning => _running?.IsRunning ?? false;
    public bool IsHardwareConnected => _hardware.IsConnected;
    public long ConnectedStationEpoch => _hardware.CurrentStationEpoch;
    public event Action<RealStepFact>? StepReported;

    public PreparedTestPlan Prepare(TestDocument document) =>
        new PlanCompiler().Prepare(document, allowRealOperations: true);

    public string ExpectedFlashApproval(TestDocument document)
    {
        var root = _projects.Current?.RootDirectory ??
            throw new InvalidOperationException("请先打开 ECU 工程。");
        var jobs = ConfiguredRealTestFlashPort.FreezeJobs(document, root);
        return jobs.Count == 0 ? "" : jobs.Values.Single().Consent;
    }

    public static bool HasPhysicalWrites(TestDocument document) =>
        TestRunStepScope.RequiresPhysicalWrite(document);

    public static string ExpectedApproval(PreparedTestPlan plan) =>
        "AUTHORIZE TEST RUN " + plan.Fingerprint;

    public Task<RealRunResult> ExecuteAsync(TestDocument document, string confirmation,
        CancellationToken ct) => ExecuteAsync(document, confirmation, "", "", ct);

    public async Task<RealRunResult> ExecuteAsync(TestDocument document, string confirmation,
        string flashConfirmation = "", string dutIdentity = "", CancellationToken ct = default,
        bool isolatedStep = false)
    {
        if (!await _station.WaitAsync(0, ct))
            throw new InvalidOperationException("Another test is already running.");
        ConfiguredStationActionPort? actions = null;
        RealRunEvidenceRecorder? evidence = null;
        try
        {
            var openingEpoch = _hardware.CurrentStationEpoch;
            if (!_hardware.IsConnected || openingEpoch <= 0)
                throw new InvalidOperationException("真实测试要求 USB 主控已经连接并建立有效会话。");
            _hardware.RequireConfiguredEcuCanBitrate();
            var plan = Prepare(document);
            if (!plan.Diagnostics.IsEmpty)
                throw new InvalidOperationException(string.Join(Environment.NewLine, plan.Diagnostics));
            // An unused or disabled mapping must not widen a physical CAN lease
            // or break the run because its unrelated historical asset is stale.
            var mappings = TestRunStepScope.RequiredMappings(document);
            var tcf = await _projects.ReadSelectedTestConfigurationAsync(ct);
            if (tcf is not null)
                foreach (var mapping in mappings)
                {
                    if (mapping.Kind is TestTargetKind.Signal or TestTargetKind.CanMessage &&
                        (tcf.DbcAssetId != mapping.Asset.Value ||
                         tcf.DbcRevision != mapping.AssetRevision ||
                         !string.Equals(tcf.DbcFingerprint, mapping.AssetSha256,
                             StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidOperationException(
                            "测试步骤引用的 DBC 不属于当前选定 TCF，需重新确认：" + mapping.Name);
                    if (mapping.Kind == TestTargetKind.Button &&
                        (tcf.ButtonAssetId != mapping.Asset.Value ||
                         tcf.ButtonRevision != mapping.AssetRevision ||
                         !string.Equals(tcf.ButtonFingerprint, mapping.AssetSha256,
                             StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidOperationException(
                            "测试步骤引用的 ECU 功能按键不属于当前 TCF：" + mapping.Name);
                }
            // Freeze all destructive resources before the first hardware write.
            var project = _projects.Current ??
                throw new InvalidOperationException("请先打开 ECU 工程。");
            var jobs = ConfiguredRealTestFlashPort.FreezeJobs(document, project.RootDirectory);
            if (jobs.Count > 0 &&
                !string.Equals(flashConfirmation, jobs.Values.Single().Consent,
                    StringComparison.Ordinal))
                throw new UnauthorizedAccessException("实体 FLASH 授权不匹配；必须输入完整指纹。");
            if (dutIdentity.Length > 0 &&
                (string.IsNullOrWhiteSpace(dutIdentity) ||
                 dutIdentity != dutIdentity.Trim() || dutIdentity.Length > 256 ||
                 dutIdentity.Any(char.IsControl)))
                throw new InvalidDataException("ECU 编号如填写，必须是不含控制字符且无首尾空格的真实编号。");
            await _signals.BindAsync(mappings, ct);
            actions = new ConfiguredStationActionPort(_hardware, _projects);
            await actions.PrepareAsync(mappings, ct);
            // Validate literal actions/DBC encoding before requesting physical
            // consent; avoid discovering malformed frames after earlier steps
            // have already changed the DUT.
            actions.ValidateRunSteps(document);
            if (!_hardware.IsConnected ||
                _hardware.CurrentStationEpoch != openingEpoch)
                throw new IOException("测试预检期间主控会话已切换；拒绝使用旧授权执行。");
            if (HasPhysicalWrites(document))
                await actions.AuthorizeAsync(plan.Fingerprint, confirmation, mappings,
                    document, ct);
            if (!_hardware.IsConnected ||
                _hardware.CurrentStationEpoch != openingEpoch)
                throw new IOException("测试授权期间 USB 会话已改变；中止运行并清理。");
            var wave = new ConfiguredWavePort(_projects, _signals);
            // Subscribe to physical evidence before any FLASH workflow can run.
            evidence = new RealRunEvidenceRecorder(_hardware, _signals);
            ITestFlashPort? flash = jobs.Count == 0 ? null :
                new ConfiguredRealTestFlashPort(_hardware, actions, document,
                    mappings, jobs, project.RootDirectory, plan.Fingerprint,
                    confirmation, flashConfirmation, evidence);
            var run = new RealRunCoordinator(_signals, actions, wave, flash);
            run.StepCompleted += fact =>
            {
                try { StepReported?.Invoke(fact); }
                catch { /* Operator visualization must never break evidence or hardware cleanup. */ }
            };
            _running = run;
            // The reviewed RECORD service starts a durable RunRecord before TEST
            // steps; only it can publish a final PASS after checking raw archives.
            var db = new RecordDatabase(Path.Combine(project.RootDirectory,
                "test-records", "r1-records.sqlite"));
            await db.MigrateAsync(ct);
            var writer = new RunRecordWriter(db);
            var artifacts = new ArtifactRepository(Path.Combine(project.RootDirectory,
                "test-records", "artifacts"));
            var reports = new RunReportExporter(db, artifacts);
            var record = FreezeActualRunIdentity(project, plan.Fingerprint, dutIdentity,
                isolatedStep);
            var capture = new RecordedRealRunEvidenceFinalizer(evidence, writer,
                artifacts, project.RootDirectory);
            var session = new RealTestRecordSession(record, writer, reports, capture);
            if (!_hardware.IsConnected ||
                _hardware.CurrentStationEpoch != openingEpoch)
                throw new IOException("RUN RECORD 建立前设备会话改变；拒绝启动 DUT 步骤。");
            var finished = await session.ExecuteAsync(run, plan,
                Path.Combine(project.RootDirectory, "test-records", "reports"), ct);
            var summary = await reports.ExportSummaryAsync(finished.Result.Run, ct);
            var formalVerdict = summary.Header.Verdict switch
            {
                Verdict.Pass => RealTestVerdict.Passed,
                Verdict.Fail => RealTestVerdict.Failed,
                Verdict.Error => RealTestVerdict.Error,
                _ => RealTestVerdict.Inconclusive
            };
            return finished.Result with
            {
                Verdict = formalVerdict,
                EvidencePath = finished.Reports.HtmlPath
            };
        }
        finally
        {
            _running = null;
            evidence?.Dispose();
            // Guard against exceptions before the interpreter began its own finally.
            if (actions is not null)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                try { await actions.CleanupAsync(cleanup.Token); }
                catch { /* Preserve the original run exception; device state may be unknown. */ }
            }
            _station.Release();
        }
    }

    private RunRecord FreezeActualRunIdentity(OpenProjectContext project,
        string planFingerprint, string dutIdentity, bool isolatedStep)
    {
        var assets = project.Definition.Assets.Select(a => a.ToReference())
            .ToImmutableArray();
        var projectSnapshot = new ProjectSnapshot(project.Definition.Id,
            new ProjectRevision(project.Definition.Revision),
            RevisionFingerprint.FromSha256(project.ManifestSha256),
            assets, project.Definition.SchemaVersion);

        // Fingerprint the CONFIGURED pin mapping, not an invented CAN channel.
        var wiring = RevisionFingerprint.FromBytes(Encoding.UTF8.GetBytes(
            "R1_STATION_ECU=" + StationCanWiring.DutConnectorPins + ":PORT" +
            StationCanWiring.DutPort + "_KEY=" + StationCanWiring.KeypadConnectorPins +
            ":PORT" + StationCanWiring.KeypadPort));
        var selectedBench = _projects.SelectedBenchConfiguration;
        var bench = project.Definition.Assets.FirstOrDefault(a => a.Id == selectedBench);
        var benchRevision = bench is null
            ? RevisionFingerprint.FromBytes("BENCH_NOT_SELECTED"u8)
            : RevisionFingerprint.FromSha256(bench.Sha256);
        var station = new StationProfile(
            StationId.Create(_hardware.ConnectedDeviceIdentity ??
                throw new InvalidOperationException("主控身份未确认，不能归档运行。")),
            new BusRoleMap(wiring, StationCanWiring.DutPort, StationCanWiring.KeypadPort),
            bench?.Name ?? "未选择工位配置（开发模式）", benchRevision);
        var snapshot = new RunSnapshot(RunId.New(), projectSnapshot, station,
            RevisionFingerprint.FromSha256(planFingerprint), assets);
        // Diagnostic runs with no DUT serial are deliberately tagged as
        // unbound; the reserved token is NOT an ECU ID and must never appear
        // as a verified scanner result or enter production statistics.
        var unboundDut = dutIdentity.Length == 0;
        var identity = unboundDut
            ? new SessionIdentity("DUT_NOT_IDENTIFIED_DIAGNOSTIC_ONLY",
                RevisionFingerprint.FromBytes("R1_DIAGNOSTIC_NO_DUT_BINDING"u8).Sha256,
                "UNBOUND_DIAGNOSTIC_NOT_A_REAL_DUT_ID")
            : new SessionIdentity(dutIdentity,
                RevisionFingerprint.FromBytes("R1_DEV_TYPED_IDENTITY_RULE_V1"u8).Sha256,
                "OPERATOR_OR_KEYBOARD_WEDGE_UNVERIFIED");
        var location = typeof(RealTestWorkbenchService).Assembly.Location;
        var build = File.Exists(location)
            ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(location)))
            : typeof(RealTestWorkbenchService).Assembly.FullName ?? "HOST_BUILD_UNKNOWN";
        return RunRecord.CreateFromSnapshot(snapshot, identity,
            unboundDut || isolatedStep ? RecordRunMode.Diagnostic : RecordRunMode.Development,
            DateTimeOffset.UtcNow, build);
    }

    public void RequestStop() => _running?.RequestStop();
}
