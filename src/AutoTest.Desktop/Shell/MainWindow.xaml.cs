using System.Windows;
using System.Windows.Controls;
using AutoTest.Desktop.G4;
using AutoTest.Desktop.Tools.Wave;
using AutoTest.Desktop.Shell.R1;
namespace AutoTest.Desktop.Shell;
public partial class MainWindow : Window
{
    // Non-owning session binding for the single R1 document host. No USB/CAN is
    // constructed here; DEVICE must explicitly provide the already-active Hub.
    private readonly R1SharedCanSource _sharedCan;

    public MainWindow(R1SharedCanSource sharedCan)
    {
        _sharedCan = sharedCan ?? throw new ArgumentNullException(nameof(sharedCan));
        InitializeComponent();
        var documents = new R1SpecialistDocumentHost(_sharedCan);
        var workspace = new R1WorkspaceViewModel(documents)
        {
            // The approved R1 interactive prototype defaults to a light IDE.
            Theme = R1Theme.Light
        };
        foreach (var id in documents.AvailableWorkspaceIds)
            workspace.UpdateAvailability(id, WorkspaceAvailability.Available);
        var view = new R1WorkspaceView(workspace);
        R1WorkspaceHost.Content = view;
        workspace.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(R1WorkspaceViewModel.ActiveTab))
                view.ShowProfessionalContent(workspace.ActiveTab is { } tab
                    ? documents.GetDocument(tab.Id) : null);
        };
        view.ShowProfessionalContent(null);
        Closed += (_, _) => documents.Dispose();
    }
    private G4ToolsWindow? _tools;
    private ModuleToolsWindow? _modules;
    private AutoTest.DeviceLab.DeviceLabWindow? _deviceLab;
    private WaveOfflineLabWindow? _waveOfflineLab;
    private void G4Tools_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || vm.G4 is null)
        {
            MessageBox.Show("G4 Application 协调服务未注册，禁止执行", "MOCK ONLY"); return;
        }
        if (_tools is { IsVisible: true }) { _tools.Activate(); return; }
        _tools = new G4ToolsWindow(vm) { Owner = this };
        _tools.Show();
    }
    private void Modules_Click(object sender, RoutedEventArgs e) => OpenModules(null);

    private void SpecialistTool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string key })
        {
            MessageBox.Show("无效的专业工具导航键，已拒绝打开。", "INTEGRATION");
            return;
        }
        OpenModules(key);
    }

    private void OpenModules(string? toolKey)
    {
        if (DataContext is not MainWindowViewModel vm || vm.G4 is null)
        {
            MessageBox.Show("G4 Application 协调服务未注册，禁止执行", "MOCK ONLY");
            return;
        }

        if (_modules is { IsVisible: true })
        {
            if (toolKey is not null)
                _modules.NavigateTo(toolKey);
        }
        else
        {
            // The requested page is selected before Show/Loaded, so the first
            // audit entry describes the user's actual tool, not the default tab.
            _modules = new ModuleToolsWindow(vm, toolKey) { Owner = this };
            _modules.Show();
        }
        _modules.Activate();
    }
    private void DeviceLab_Click(object sender, RoutedEventArgs e)
    {
        if (_deviceLab is { IsVisible: true }) { _deviceLab.Activate(); return; }
        _deviceLab = new AutoTest.DeviceLab.DeviceLabWindow { Owner = this };
        _deviceLab.Show();
    }
    private void WaveOffline_Click(object sender, RoutedEventArgs e)
    {
        if (_waveOfflineLab is { IsVisible: true })
        {
            _waveOfflineLab.Activate();
            return;
        }
        try
        {
            _waveOfflineLab = new WaveOfflineLabWindow { Owner = this };
            _waveOfflineLab.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "WAVE 离线窗口启动失败: " + ex.Message,
                "AutoTest · WAVE", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
