using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AutoTest.Application.G4;
using AutoTest.Desktop.Shell;
using AutoTest.Desktop.Tools.ControllerConfig;
using AutoTest.Desktop.Tools.Dbc;
using AutoTest.Desktop.Tools.Flash;
using AutoTest.Desktop.Tools.Flash.Physical;
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
    private readonly R0LabAuditRuntime _audit;
    private readonly OperationsViewModel _operationsModel;
    private readonly Dictionary<string, Action> _pageFactories;
    private readonly HashSet<string> _loaded = new(StringComparer.Ordinal);
    private string? _initialLoadKey;
    private Exception? _initialLoadError;

    public ModuleToolsWindow(MainWindowViewModel main, string? initialToolKey = null)
    {
        ArgumentNullException.ThrowIfNull(main);
        var host = main.G4 ?? throw new InvalidOperationException("G4 services are not registered.");
        _audit = host.OperationAudit;
        InitializeComponent();

        var database = host.Records.Database;
        var artifactRoot = Path.Combine(Path.GetDirectoryName(database.DatabasePath)!, "artifacts");
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoTest");
        _operationsModel = new OperationsViewModel(_audit.Store, _audit.Traces, _audit.Exporter);

        // Create expensive specialist pages only when selected. One broken specialist
        // must not prevent all other diagnostic tools from opening in the same HOST.
        _pageFactories = new Dictionary<string, Action>(StringComparer.Ordinal)
        {
            ["controller-config"] = () => ControllerConfigHost.Content =
                new ControllerConfigToolView(new ControllerConfigToolViewModel()),
            ["dbc"] = () => DbcHost.Content = new DbcToolView(new DbcToolViewModel()),
            ["test"] = () => TestHost.Content = new LabTestWorkspaceView(),
            ["production"] = () => ProductionHost.Content = new LabProductionToolView(),
            ["records"] = () => RecordsHost.Content = new RecordsView(new RecordsViewModel(
                new RecordHistoryToolSource(new RunHistoryQuery(database),
                    new ProductionStatistics(database),
                    new RunReportExporter(database, new ArtifactRepository(artifactRoot))))),
            ["operations"] = () => OperationsHost.Content = new OperationsView(_operationsModel),
            ["uds"] = () => UdsHost.Content = new UdsToolView { DataContext = new UdsToolViewModel() },
            ["flash"] = () => FlashHost.Content = new FlashToolView { DataContext = new FlashToolViewModel() },
            ["wave"] = () => LoadWave(host, appData),
            ["bench"] = () => { } // Explicit disabled page; no fake IBenchWorkbenchPort.
        };

        if (initialToolKey is not null)
            NavigateTo(initialToolKey);

        SpecialistTabs.SelectionChanged += SpecialistTabs_SelectionChanged;
        if (SpecialistTabs.SelectedItem is TabItem initial && initial.Tag is string initialKey &&
            !TryLoad(initialKey, out var initialFailure))
        {
            _initialLoadKey = initialKey;
            _initialLoadError = initialFailure;
            AuditStatus.Text = "初始专业页加载失败（其余页面仍可单独打开）· " + initialFailure;
            AuditStatus.Foreground = Brushes.OrangeRed;
        }
        Loaded += InitialSelectionLoaded;
    }

    /// <summary>Resolves an existing specialist page; never opens a parallel HOST.</summary>
    public string? SelectedToolKey => (SpecialistTabs.SelectedItem as TabItem)?.Tag as string;

    public void NavigateTo(string toolKey)
    {
        if (string.IsNullOrWhiteSpace(toolKey) || !_pageFactories.ContainsKey(toolKey))
            throw new ArgumentException("Unregistered specialist navigation key.", nameof(toolKey));
        var tab = SpecialistTabs.Items.OfType<TabItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, toolKey, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Registered specialist tool is absent from the window.");
        SpecialistTabs.SelectedItem = tab;
    }

    private async void InitialSelectionLoaded(object sender, RoutedEventArgs args)
    {
        Loaded -= InitialSelectionLoaded;
        if (SpecialistTabs.SelectedItem is not TabItem current || current.Tag is not string key)
            return;

        if (_initialLoadError is not null && _initialLoadKey == key)
        {
            try { await _audit.RecordToolActivationFailureAsync(key, _initialLoadError); }
            catch (Exception auditError)
            {
                AuditStatus.Text += " | 同时，SQLite 审计写入失败: " + auditError.Message;
            }
            return;
        }

        // WPF selects the first tab during InitializeComponent, before SelectionChanged
        // is subscribed. Record the first visible tool just like later tab changes.
        await AuditToolNavigationAsync(key, current.Header?.ToString() ?? key);
    }

    private void LoadWave(G4IntegrationHost host, string appData)
    {
        var wave = new WaveWorkbenchView();
        wave.Model.BindBackend(new G4WaveReplayBackend(host,
            Path.Combine(appData, "G4-MockOnly", "wave-workbench")));
        WaveHost.Content = wave;
    }

    private bool TryLoad(string key, out Exception? failure)
    {
        failure = null;
        if (_loaded.Contains(key)) return true;
        if (!_pageFactories.TryGetValue(key, out var create))
        {
            failure = new InvalidOperationException("Unregistered R0 specialist page: " + key);
            return false;
        }
        try
        {
            create();
            _loaded.Add(key);
            return true;
        }
        catch (Exception exception)
        {
            failure = exception;
            return false;
        }
    }

    private async void SpecialistTabs_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!ReferenceEquals(args.Source, SpecialistTabs) ||
            SpecialistTabs.SelectedItem is not TabItem tab || tab.Tag is not string key)
            return;

        if (!TryLoad(key, out var error))
        {
            AuditStatus.Text = $"专业页加载失败 · {key} · {error}";
            AuditStatus.Foreground = Brushes.OrangeRed;
            if (error != null)
            {
                try { await _audit.RecordToolActivationFailureAsync(key, error); }
                catch (Exception auditError)
                {
                    AuditStatus.Text += " | 同时，SQLite 审计写入失败: " + auditError.Message;
                }
            }
            return;
        }

        await AuditToolNavigationAsync(key, tab.Header?.ToString() ?? key);
    }

    private async Task AuditToolNavigationAsync(string key, string label)
    {
        AuditStatus.Text = "正在记录 UI 导航（尚未确认 SQLite 持久化）…";
        AuditStatus.Foreground = Brushes.LightGoldenrodYellow;
        try
        {
            var receipt = await _audit.RecordToolOpenedAsync(key, label);
            AuditStatus.Text = $"UI_NAVIGATION_ONLY · 已持久化 · EventId {receipt.EventId:D} · 不代表设备操作成功";
            AuditStatus.Foreground = Brushes.LightGreen;
            if (key == "operations") await _operationsModel.RefreshAsync();
        }
        catch (Exception exception)
        {
            AuditStatus.Text = "操作审计保存失败（未宣称已保存）· " + exception.Message;
            AuditStatus.Foreground = Brushes.OrangeRed;
        }
    }

    private void OpenPhysicalFlashLab_Click(object sender, RoutedEventArgs args)
    {
        var window = new FlashLabWindow { Owner = this };
        window.Show();
    }

    private void OpenAudit_Click(object sender, RoutedEventArgs args)
    {
        if (ReferenceEquals(SpecialistTabs.SelectedItem, OperationsTab))
            _ = _operationsModel.RefreshAsync();
        else
            SpecialistTabs.SelectedItem = OperationsTab;
    }
}
