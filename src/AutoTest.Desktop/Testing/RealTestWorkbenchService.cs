using AutoTest.Desktop.Hardware;
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

    public PreparedTestPlan Prepare(TestDocument document) =>
        new PlanCompiler().Prepare(document, allowRealOperations: true);

    public static bool HasPhysicalWrites(TestDocument document) =>
        document.Walk().Any(n => n.Type == "Write");

    public static string ExpectedApproval(PreparedTestPlan plan) =>
        "AUTHORIZE TEST RUN " + plan.Fingerprint;

    public async Task<RealRunResult> ExecuteAsync(TestDocument document, string confirmation,
        CancellationToken ct = default)
    {
        if (!await _station.WaitAsync(0, ct))
            throw new InvalidOperationException("Another test is already running.");
        ConfiguredStationActionPort? actions = null;
        try
        {
            if (!_hardware.IsConnected)
                throw new InvalidOperationException("真实测试要求 USB 主控已经连接。");
            var plan = Prepare(document);
            if (!plan.Diagnostics.IsEmpty)
                throw new InvalidOperationException(string.Join(Environment.NewLine, plan.Diagnostics));
            var mappings = document.Mappings.ToArray();
            await _signals.BindAsync(mappings, ct);
            actions = new ConfiguredStationActionPort(_hardware, _projects);
            await actions.PrepareAsync(mappings, ct);
            if (HasPhysicalWrites(document))
                await actions.AuthorizeAsync(plan.Fingerprint, confirmation, mappings, ct);
            var wave = new ConfiguredWavePort(_projects, _signals);
            var run = new RealRunCoordinator(_signals, actions, wave);
            _running = run;
            return await run.StartAsync(plan, ct);
        }
        finally
        {
            _running = null;
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

    public void RequestStop() => _running?.RequestStop();
}
