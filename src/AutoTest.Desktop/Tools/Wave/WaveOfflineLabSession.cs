using System.IO;
using System.Text;
using AutoTest.Projects.Dbc;
using System.Windows;
using System.Windows.Controls;
using AutoTest.Application.G4;
using AutoTest.Application.V1;
using AutoTest.Devices.Can;

namespace AutoTest.Desktop.Tools.Wave;

/// <summary>
/// R0 specialist-only, offline diagnostic composition. It uses the existing CAN Hub and DBC
/// decoder but owns no USB, physical Rx, physical Tx delegate, Station or production session.
/// Do NOT register this temporary composition as the single production CAN Hub.
/// </summary>
public sealed class WaveOfflineLabSession : IDisposable
{
    private readonly CanFrameHub _replayHub;
    private readonly G4IntegrationHost _mockHost;
    private bool _disposed;

    public WaveOfflineLabSession(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var directory = Path.GetFullPath(root);
        Directory.CreateDirectory(directory);
        Workspace = new V1Workspace();
        _mockHost = new G4IntegrationHost(Workspace, Path.Combine(directory, "mock-only"));
        // No transmit delegate: this hub cannot issue a physical CAN frame.
        _replayHub = new CanFrameHub();
        var replay = new RecordedCanWaveReplayProvider(_replayHub, _replayHub.Publish,
            () => !_disposed, Workspace, Path.Combine(directory, "recorded-replay"));
        Backend = new G4WaveReplayBackend(_mockHost, Path.Combine(directory, "wave"),
            recorded: replay);
    }

    public V1Workspace Workspace { get; }
    public G4WaveReplayBackend Backend { get; }
    public bool PhysicalTransmitAvailable => false;

    public void ImportDbcText(string text, string name = "local.dbc")
    {
        EnsureOpen();
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        // Parse and validate BEFORE replacing the current workspace: a malformed .dbc
        // must never discard a previously valid project or reference selection.
        var source = DbcSource.Load(Encoding.UTF8.GetBytes(text));
        _ = DbcParserAdapter.Parse(source.Text, Path.GetFileNameWithoutExtension(name));
        Workspace.CreateProject("WAVE 离线 DBC 诊断");
        Workspace.ImportDbc(text, name);
    }

    public void ImportDbcFile(string path)
    {
        EnsureOpen();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!string.Equals(Path.GetExtension(path), ".dbc", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("WAVE_DBC_FILE_REQUIRED: 请选择 .dbc 文件");
        ImportDbcText(File.ReadAllText(path), Path.GetFileName(path));
    }

    public Task OpenProjectAsync(string path, CancellationToken ct = default)
    {
        EnsureOpen();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!path.EndsWith(".autotest-project.json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("WAVE_PROJECT_FILE_REQUIRED: 请选择 AutoTest 工程文件");
        return Workspace.OpenProjectAsync(path, ct);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _replayHub.Dispose();
        _mockHost.Dispose();
    }

    private void EnsureOpen()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WaveOfflineLabSession));
    }
}

/// <summary>
/// Independent professional WPF window for offline WAVE Replay. It can be opened by a
/// minimal host launcher and later discarded when the unified Prism Shell owns navigation.
/// The full WaveWorkbenchView stays unchanged and can also be used by the formal host.
/// </summary>
public sealed class WaveOfflineLabWindow : Window
{
    private readonly WaveOfflineLabSession _session;
    private readonly WaveWorkbenchView _waveView;
    private readonly TextBlock _resourceStatus;
    private bool _closed;

    public WaveOfflineLabWindow() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AutoTest", "R0-WaveOfflineLab")) { }

    public WaveOfflineLabWindow(string root)
    {
        _session = new WaveOfflineLabSession(root);
        Title = "AutoTest · WAVE R0-LAB · 仅离线 Replay / Mock";
        Width = 1500;
        Height = 950;
        MinWidth = 1120;
        MinHeight = 720;
        Background = System.Windows.Media.Brushes.DarkSlateGray;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _waveView = new WaveWorkbenchView();
        _waveView.Model.BindBackend(_session.Backend);
        _resourceStatus = new TextBlock
        {
            Text = "DBC 未载入 · 本窗口无 USB/CAN 物理发送能力",
            Margin = new Thickness(8, 8, 8, 8),
            Foreground = System.Windows.Media.Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 10, 12, 6) };
        var loadDbc = new Button { Content = "导入本地 DBC", Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 0, 8, 0) };
        loadDbc.Click += LoadDbc_Click;
        var loadProject = new Button { Content = "打开 AutoTest 工程", Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 0, 8, 0) };
        loadProject.Click += LoadProject_Click;
        header.Children.Add(loadDbc);
        header.Children.Add(loadProject);
        header.Children.Add(_resourceStatus);
        var dock = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        dock.Children.Add(header);
        dock.Children.Add(_waveView);
        Content = dock;
        Closed += (_, _) => DisposeSession();
    }

    public WaveWorkbenchViewModel Model => _waveView.Model;

    private void LoadDbc_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "CAN database (*.dbc)|*.dbc",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (Model.Busy) throw new InvalidOperationException("WAVE_RESOURCE_BUSY: 请先取消或完成正在进行的采集");
            _session.ImportDbcFile(dialog.FileName);
            Model.RefreshSignals();
            _resourceStatus.Text = $"DBC: {System.IO.Path.GetFileName(dialog.FileName)} · " +
                $"Rev {_session.Workspace.Database?.Revision} · SOFTWARE_ONLY";
            Model.ReportExternalOperation("LoadDBC", "Completed", _resourceStatus.Text);
        }
        catch (Exception ex)
        {
            Model.ReportExternalOperation("LoadDBC", "Failed", ex.Message, "WAVE_DBC_LOAD_FAILED");
            _resourceStatus.Text = "DBC 导入失败: " + ex.Message;
        }
    }

    private async void LoadProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "AutoTest project (*.autotest-project.json)|*.autotest-project.json",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (Model.Busy) throw new InvalidOperationException("WAVE_RESOURCE_BUSY: 请先取消或完成正在进行的采集");
            await _session.OpenProjectAsync(dialog.FileName);
            Model.RefreshSignals();
            _resourceStatus.Text = $"工程: {_session.Workspace.Project.Name} · " +
                $"DBC Rev {_session.Workspace.Database?.Revision} · SOFTWARE_ONLY";
            Model.ReportExternalOperation("OpenProject", "Completed", _resourceStatus.Text);
        }
        catch (Exception ex)
        {
            Model.ReportExternalOperation("OpenProject", "Failed", ex.Message, "WAVE_PROJECT_LOAD_FAILED");
            _resourceStatus.Text = "工程加载失败: " + ex.Message;
        }
    }

    private void DisposeSession()
    {
        if (_closed) return;
        _closed = true;
        if (Model.StopCommand.CanExecute(null)) Model.StopCommand.Execute(null);
        _session.Dispose();
    }
}
