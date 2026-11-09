using System.IO;
using System.Windows;
using AutoTest.Desktop.Shell;
using AutoTest.Desktop.Tools.ControllerConfig;
using AutoTest.Desktop.Tools.Dbc;
using AutoTest.Desktop.Tools.Flash;
using AutoTest.Desktop.Tools.Operations;
using AutoTest.Desktop.Tools.Production;
using AutoTest.Desktop.Tools.Records;
using AutoTest.Desktop.Tools.Testing;
using AutoTest.Desktop.Tools.Uds;
using AutoTest.Desktop.Tools.Wave;
using AutoTest.Records.Exports;
using AutoTest.Records.Operations;
using AutoTest.Records.Queries;
using AutoTest.Records.Storage;

namespace AutoTest.Desktop.G4;

public partial class ModuleToolsWindow : Window
{
    public ModuleToolsWindow(MainWindowViewModel main)
    {
        ArgumentNullException.ThrowIfNull(main);
        var host = main.G4 ?? throw new InvalidOperationException("G4 services are not registered.");
        InitializeComponent();

        ControllerConfigHost.Content = new ControllerConfigToolView(new ControllerConfigToolViewModel());
        DbcHost.Content = new DbcToolView(new DbcToolViewModel());
        TestHost.Content = new LabTestWorkspaceView();
        ProductionHost.Content = new LabProductionToolView();

        var database = host.Records.Database;
        var artifactRoot = Path.Combine(Path.GetDirectoryName(database.DatabasePath)!, "artifacts");
        var reports = new RunReportExporter(database, new ArtifactRepository(artifactRoot));
        var history = new RecordHistoryToolSource(new RunHistoryQuery(database),
            new ProductionStatistics(database), reports);
        RecordsHost.Content = new RecordsView(new RecordsViewModel(history));

        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoTest");
        var operationsRoot = Path.Combine(appData, "Operations");
        var audit = new SqliteOperationAuditStore(Path.Combine(operationsRoot, "operation-audit.sqlite"));
        var traces = new OperationTraceArchive(Path.Combine(operationsRoot, "traces"));
        OperationsHost.Content = new OperationsView(new OperationsViewModel(audit, traces,
            new OperationAuditExporter(audit)));

        UdsHost.Content = new UdsToolView { DataContext = new UdsToolViewModel() };
        FlashHost.Content = new FlashToolView { DataContext = new FlashToolViewModel() };
        var wave = new WaveWorkbenchView();
        wave.Model.BindBackend(new G4WaveReplayBackend(host,
            Path.Combine(appData, "G4-MockOnly", "wave-workbench")));
        WaveHost.Content = wave;
    }
}
