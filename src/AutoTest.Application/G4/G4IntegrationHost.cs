using AutoTest.Application.V1;
using AutoTest.Records.Storage;

namespace AutoTest.Application.G4;

/// <summary>One explicit offline lifetime; V1Workspace and all four specialists share the same host.</summary>
public sealed class G4IntegrationHost : IDisposable
{
    private readonly MockCanHubRouter _bus;
    public V1Workspace Workspace { get; }
    public MockRecordCoordinator Records { get; }
    public MockProductionWorkbench Production { get; }
    public HubDbcWaveReplay Wave { get; }
    public MockHubDiagnosticWorkbench Diagnostics { get; }
    public MockFlashWorkbench Flash { get; }
    public R0LabAuditRuntime OperationAudit { get; }

    public G4IntegrationHost(V1Workspace workspace) : this(workspace,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AutoTest", "G4-MockOnly"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AutoTest", "Operations")) { }

    public G4IntegrationHost(V1Workspace workspace, string storageRoot)
        : this(workspace, storageRoot, Path.Combine(storageRoot, "operations")) { }

    private G4IntegrationHost(V1Workspace workspace, string storageRoot, string operationsRoot)
    {
        Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        ArgumentException.ThrowIfNullOrWhiteSpace(storageRoot);
        var database = new RecordDatabase(Path.Combine(storageRoot, "records", "autotest-g4-mock.sqlite"));
        Records = new MockRecordCoordinator(workspace, database);
        Production = new MockProductionWorkbench(workspace, Records);
        _bus = new MockCanHubRouter();
        Wave = new HubDbcWaveReplay(workspace, _bus.Hub, Path.Combine(storageRoot, "wave"));
        Diagnostics = new MockHubDiagnosticWorkbench(_bus);
        Flash = new MockFlashWorkbench();
        OperationAudit = new R0LabAuditRuntime(operationsRoot);
    }
    public void Dispose() => _bus.Dispose();
}
