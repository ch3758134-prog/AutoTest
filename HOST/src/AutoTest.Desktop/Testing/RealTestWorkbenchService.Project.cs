using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoTest.Core;
using AutoTest.Desktop.Projects;
using AutoTest.Projects.Project;
using AutoTest.Testing.Authoring;
using AutoTest.Testing.Runtime;

namespace AutoTest.Desktop.Testing;

public sealed partial class RealTestWorkbenchService
{
    /// <summary>
    /// Acquires ONE physical grant for the complete ordered project. The existing
    /// RealRunCoordinator and RealTestRecordSession execute each case independently.
    /// FLASH must already be completed by the caller under its separate grant.
    /// </summary>
    public async Task<ProjectExecutionSession> OpenProjectSessionAsync(
        IReadOnlyList<TestDocument> orderedCases, Guid projectRunId,
        string dutIdentity, CancellationToken ct = default,
        bool allowOrderedSubset = false)
    {
        if (projectRunId == Guid.Empty || orderedCases.Count == 0 ||
            !allowOrderedSubset && string.IsNullOrWhiteSpace(dutIdentity))
            throw new ArgumentException("整组运行需要项目运行号、DUT 编号和有序 TEST 用例。");
        if (!await _station.WaitAsync(0, ct))
            throw new InvalidOperationException("工位正在由另一测试任务占用。");
        ConfiguredStationActionPort? actions = null;
        ProjectExecutionSession? pending = null;
        try
        {
            var project = _projects.Current ??
                throw new InvalidOperationException("ECU 工程尚未打开。");
            var epoch = _hardware.CurrentStationEpoch;
            if (!_hardware.IsConnected || epoch <= 0)
                throw new IOException("整组 TEST 必须绑定已连接的真实主控 USB 会话。");
            _hardware.RequireConfiguredEcuCanBitrate();
            var pinned = new List<(TestDocumentId Id, string Sha)>();
            // Local mapping aliases are NOT global resource identities.
            var maps = new HashSet<TestTargetMapping>();
            foreach (var source in orderedCases)
            {
                var plan = Prepare(source);
                if (!plan.Diagnostics.IsEmpty)
                    throw new InvalidDataException(source.Name + " 的运行前检查失败：" +
                        string.Join("；", plan.Diagnostics));
                var doc = plan.RestoreDocument();
                // The project may intentionally invoke one TEST asset more than once;
                // separate CaseAttempt/RunIds still identify each invocation.
                if (ConfiguredRealTestFlashPort.FreezeJobs(
                    doc, project.RootDirectory).Count > 0)
                    throw new InvalidDataException("连续 TEST 不能包含互斥的 FLASH 步骤；" +
                        "当前工程应先完成独立的前置 FLASH 工艺。");
                pinned.Add((doc.Id, plan.Fingerprint));
                foreach (var mapping in TestRunStepScope.RequiredMappings(doc))
                {
                    // The development subset is authoritative only for THIS
                    // debugging scope. Check every selected DBC/wave dependency
                    // before opening the shared physical grant; an unrelated
                    // draft case need not participate in developer preflight.
                    await AutomaticTestResourceContract.ValidateMappingAsync(
                        project, mapping, ct).ConfigureAwait(false);
                    maps.Add(mapping);
                }
            }
            var union = maps.ToArray();
            await _signals.BindAsync(union, ct);
            actions = new ConfiguredStationActionPort(_hardware, _projects);
            await actions.PrepareAsync(union, ct);
            foreach (var doc in orderedCases) actions.ValidateRunSteps(doc);
            if (!_hardware.IsConnected || _hardware.CurrentStationEpoch != epoch)
                throw new IOException("整组准备期间主控 USB 连接代次发生变化。");

            var grantMaterial = "AUTOTEST_R1_PROJECT_SCOPE_V1\n" +
                projectRunId.ToString("N") + "\n" + project.Definition.Id.Value +
                "\n" + project.ManifestSha256 + "\n" + dutIdentity + "\n" +
                epoch + "\n" + string.Join("\n", pinned.Select(x => x.Id + "=" + x.Sha));
            var fingerprint = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(grantMaterial)));
            var session = new ProjectExecutionSession(this, actions, project,
                projectRunId, dutIdentity, epoch, fingerprint, pinned,
                allowOrderedSubset);
            pending = session;
            // Durable parent receipt before the first physical TEST write.
            await session.PersistAsync("PREPARED", CancellationToken.None);
            await actions.AuthorizeProjectAsync(fingerprint,
                "AUTHORIZE TEST RUN " + fingerprint, union, orderedCases, ct);
            if (!_hardware.IsConnected || _hardware.CurrentStationEpoch != epoch)
                throw new IOException("整组授权时主控 USB 会话失效。");
            _projectSession = session;
            await session.PersistAsync("RUNNING", CancellationToken.None);
            return session;
        }
        catch
        {
            if (actions is not null)
            {
                using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                try { await actions.CleanupAsync(stop.Token); }
                catch { /* A failed transport cannot be presented as safe cleanup. */ }
                // Even a partially acknowledged grant must be revoked.
                // Otherwise the next run could inherit an unowned USB fence.
                try { await _hardware.RevokeTestRunAsync(); }
                catch { /* Disconnect is UNKNOWN; preserve original failure. */ }
            }
            if (pending is not null)
            {
                pending.DetachTimeline();
                try { await pending.PersistAsync("REJECTED_OR_AUTHORIZATION_FAILED",
                    CancellationToken.None); }
                catch { /* Failure is still raised; no success may be claimed. */ }
            }
            _projectSession = null;
            _station.Release();
            throw;
        }
    }

    public sealed class ProjectExecutionSession : IAsyncDisposable
    {
        private readonly RealTestWorkbenchService _owner;
        private readonly OpenProjectContext _project;
        private readonly long _stationEpoch;
        private readonly string _dut;
        private readonly (TestDocumentId Id, string Sha)[] _pinnedCases;
        private readonly bool _allowOrderedSubset;
        private readonly List<ProjectCaseReceipt> _cases = new();
        private readonly SemaphoreSlim _persistGate = new(1, 1);
        private readonly RealRunEvidenceRecorder _projectTimeline;
        private string? _projectTimelinePath;
        private long _timelineDropped;
        private int _next;
        private bool _markedComplete;
        private bool _disposed;

        private sealed record ProjectCaseReceipt(
            int Sequence, string TestDocumentId, Guid CaseRunId,
            Guid CaseAttemptId, string PlanFingerprint, string Verdict, string? EvidencePath,
            DateTimeOffset CompletedUtc);
        private sealed record ProjectPeriodicCaseBoundary(
            int Sequence, string TestDocumentId, DateTimeOffset CapturedUtc,
            PeriodicCanTaskEvidence[] ActiveBefore,
            PeriodicCanTaskEvidence[] ActiveAfter,
            PeriodicCanTaskEvidence[] StoppedDuring);
        private readonly List<ProjectPeriodicCaseBoundary> _periodicCaseBoundaries = new();

        internal ConfiguredStationActionPort Actions { get; }
        public Guid ProjectRunId { get; }
        public string ScopeFingerprint { get; }
        public int CompletedCases => _next;
        public int PlannedCases => _pinnedCases.Length;
        public IReadOnlyList<PeriodicCanTaskEvidence> PeriodicTasks =>
            Actions.PeriodicEvidenceSnapshot();
        public IReadOnlyList<PeriodicCanTaskEvidence> LastCaseStoppedPeriodic =>
            _periodicCaseBoundaries.Count == 0
                ? Array.Empty<PeriodicCanTaskEvidence>()
                : _periodicCaseBoundaries[^1].StoppedDuring;

        internal ProjectExecutionSession(RealTestWorkbenchService owner,
            ConfiguredStationActionPort actions, OpenProjectContext project,
            Guid projectRunId, string dut, long epoch, string fingerprint,
            IReadOnlyList<(TestDocumentId Id, string Sha)> pinnedCases,
            bool allowOrderedSubset)
        {
            _owner = owner; Actions = actions; _project = project;
            _allowOrderedSubset = allowOrderedSubset;
            ProjectRunId = projectRunId; _dut = dut; _stationEpoch = epoch;
            ScopeFingerprint = fingerprint; _pinnedCases = pinnedCases.ToArray();
            _projectTimeline = new RealRunEvidenceRecorder(owner._hardware, owner._signals);
        }

        internal void ValidateCase(TestDocument document, string fingerprint, long epoch)
        {
            if (_disposed || _markedComplete || _next >= _pinnedCases.Length ||
                epoch != _stationEpoch || !_owner._hardware.IsConnected ||
                _pinnedCases[_next].Id != document.Id ||
                !string.Equals(_pinnedCases[_next].Sha, fingerprint,
                    StringComparison.OrdinalIgnoreCase))
                throw new IOException("本次用例不属于冻结的项目顺序或真实 USB 会话已改变。");
            var active = _owner._projects.Current;
            if (active is null || active.Definition.Id != _project.Definition.Id ||
                !string.Equals(active.ManifestPath, _project.ManifestPath,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(active.ManifestSha256, _project.ManifestSha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new IOException("本次项目运行的 ECU 工程修订发生变化，禁止扩大原授权。");
        }

        public async Task<RealRunResult> ExecuteCaseAsync(TestDocument doc,
            CancellationToken ct = default)
        {
            ValidateCase(doc, _owner.Prepare(doc).Fingerprint,
                _owner._hardware.CurrentStationEpoch);
            // Re-verify real on-disk project closure before each new case.
            // An out-of-process DBC/TEST edit must invalidate this session even
            // if the UI did not publish a CurrentChanged event.
            var bytes = await File.ReadAllBytesAsync(_project.ManifestPath, ct);
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)),
                _project.ManifestSha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("整组运行期间工程清单被外部修改。");
            var actual = await AutomaticTestResourceContract.ResolveAsync(_project, ct);
            if (!_allowOrderedSubset)
            {
                // Production/full project must match the entire ordered tree.
                actual.RequireExecutable();
                var fullSequence = actual.OrderedExecutions();
                if (fullSequence.Count != _pinnedCases.Length ||
                    fullSequence.Where((entry, i) =>
                        entry.Program.Id != _pinnedCases[i].Id ||
                        !string.Equals(_owner.Prepare(entry.Program).Fingerprint,
                            _pinnedCases[i].Sha, StringComparison.OrdinalIgnoreCase)).Any())
                    throw new IOException("整组用例或 DBC/波形/TEST 资源版本在运行中发生变化。");
            }
            else
            {
                // Development selection is an ordered SUBSEQUENCE of one
                // pinned project, never a second persisted production recipe.
                // Unselected authoring drafts cannot expand the USB grant.
                var cursor = 0;
                foreach (var pin in _pinnedCases)
                {
                    while (cursor < actual.Cases.Count)
                    {
                        var current = actual.Cases[cursor++].Program;
                        if (current.Id == pin.Id &&
                            string.Equals(_owner.Prepare(current).Fingerprint,
                                pin.Sha, StringComparison.OrdinalIgnoreCase))
                            break;
                    }
                    if (cursor == 0 || cursor > actual.Cases.Count ||
                        actual.Cases[cursor - 1].Program.Id != pin.Id ||
                        !string.Equals(_owner.Prepare(actual.Cases[cursor - 1].Program).Fingerprint,
                            pin.Sha, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("开发勾选范围已不再属于当前工程冻结的有序 TEST 树。");
                }
                // Run-specific mappings were validated before authorization.
                foreach (var map in TestRunStepScope.RequiredMappings(doc))
                    await AutomaticTestResourceContract.ValidateMappingAsync(
                        _project, map, ct).ConfigureAwait(false);
            }
            await Actions.CheckHealthAsync(ct);
            // Record the physical CAN sender transition at the actual case
            // boundary, not merely as a single end-of-project snapshot.
            var caseBoundaryStartedUtc = DateTimeOffset.UtcNow;
            var activeBefore = Actions.PeriodicEvidenceSnapshot()
                .Where(x => x.StoppedUtc is null).ToArray();
            var result = await _owner.ExecuteAsync(doc, "", "", _dut, ct,
                projectSession: this);
            var after = Actions.PeriodicEvidenceSnapshot();
            var activeAfter = after.Where(x => x.StoppedUtc is null).ToArray();
            // Include tasks started AND stopped inside this same case. Looking
            // only at activeBefore would miss the most common unexpected
            // duration expiry on a setup case.
            var stoppedDuring = after.Where(x => x.StoppedUtc is { } stopped &&
                stopped >= caseBoundaryStartedUtc).ToArray();
            _periodicCaseBoundaries.Add(new ProjectPeriodicCaseBoundary(
                _next + 1, doc.Id.ToString(), DateTimeOffset.UtcNow,
                activeBefore, activeAfter, stoppedDuring));
            // A sender can silently stall during a long case. Do not let the
            // next ECU judgement proceed on a stale periodic environment.
            await Actions.CheckHealthAsync(ct);
            _cases.Add(new ProjectCaseReceipt(_next + 1, doc.Id.ToString(),
                result.Run.Value, result.CaseAttemptId,
                result.PlanFingerprint, result.Verdict.ToString(),
                result.EvidencePath, DateTimeOffset.UtcNow));
            _next++;
            await PersistAsync("RUNNING", CancellationToken.None);
            return result;
        }

        public void MarkCompleted()
        {
            if (_disposed || _next != _pinnedCases.Length)
                throw new InvalidOperationException("仍有尚未执行的冻结用例。");
            _markedComplete = true;
        }

        internal async Task PersistAsync(string state, CancellationToken ct)
        {
            await _persistGate.WaitAsync(ct);
            try
            {
                var folder = Path.Combine(_project.RootDirectory,
                    "test-records", "project-runs");
                Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, ProjectRunId.ToString("N") + ".json");
                var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                var bytes = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    SchemaVersion = 1,
                    Mode = _allowOrderedSubset
                        ? "ENGINEERING_CONTINUOUS_SELECTED_SUBSET_NOT_PRODUCTION"
                        : "ENGINEERING_CONTINUOUS_PROJECT_NOT_PRODUCTION_RELEASE",
                    ProjectRunId, DutSerial = _dut, StationEpoch = _stationEpoch,
                    ProjectId = _project.Definition.Id.Value,
                    _project.ManifestSha256, ScopeFingerprint, State = state,
                    CompletedCases = _next, PlannedCases = _pinnedCases.Length,
                    PinnedCases = _pinnedCases.Select(x => new
                        { TestDocumentId = x.Id.ToString(), Fingerprint = x.Sha }).ToArray(),
                    Cases = _cases.ToArray(),
                    PeriodicCaseBoundaries = _periodicCaseBoundaries.ToArray(),
                    PeriodicCan = Actions.PeriodicEvidenceSnapshot(),
                    PeriodicEvidenceType = "HOST_REQUEST_USB_ACK_NOT_ECU_RX",
                    ProjectCanTimeline = _projectTimelinePath,
                    ProjectCanTimelineDropped = _timelineDropped,
                    Utc = DateTimeOffset.UtcNow
                });
                try
                {
                    using (var stream = new FileStream(temp, FileMode.CreateNew,
                        FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough))
                    {
                        stream.Write(bytes);
                        stream.Flush(flushToDisk: true);
                    }
                    File.Move(temp, path, overwrite: true);
                    if (!SHA256.HashData(File.ReadAllBytes(path)).AsSpan().SequenceEqual(
                            SHA256.HashData(bytes)))
                        throw new IOException("连续 TEST 运行记录 SHA256 回读不一致。");
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
            finally { _persistGate.Release(); }
        }

        internal void DetachTimeline() => _projectTimeline.Dispose();

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            Exception? cleanupError = null;
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                await Actions.CleanupAsync(deadline.Token);
            }
            catch (Exception error) { cleanupError = error; }
            finally
            {
                _owner._projectSession = null;
                _owner._station.Release();
                _projectTimeline.Dispose();
            }
            try
            {
                _timelineDropped = _projectTimeline.CaptureHealth().DroppedEvents;
                _projectTimelinePath = await _projectTimeline.SaveProjectTimelineAsync(
                    _project.RootDirectory, ProjectRunId, CancellationToken.None);
                if (_timelineDropped != 0 && cleanupError is null)
                    cleanupError = new IOException("整组连续 CAN 时间线存在记录溢出或接收丢帧。");
            }
            catch (Exception error) { cleanupError ??= error; }
            await PersistAsync(cleanupError is not null ? "CLEANUP_OR_EVIDENCE_ERROR" :
                _markedComplete
                    ? _cases.Any(x => x.Verdict == nameof(RealTestVerdict.Failed))
                        ? "FINISHED_WITH_FUNCTIONAL_FAILURE"
                        : "EXECUTED_NOT_PRODUCTION_RELEASE"
                    : "INTERRUPTED",
                CancellationToken.None);
            if (cleanupError is not null)
                throw new IOException("整组运行授权撤销或周期 CAN 清理失败；实体状态未知。",
                    cleanupError);
        }
    }
}
