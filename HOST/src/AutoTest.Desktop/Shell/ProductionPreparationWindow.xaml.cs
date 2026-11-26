using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using AutoTest.Desktop.Projects;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// One device-only station editor. Project packages and barcode routing are
/// managed in ProductionProjectLibraryWindow, never duplicated here.
/// All edits are saved as an inactive draft, without granting physical START.
/// </summary>
public partial class ProductionPreparationWindow : Window
{
    private readonly SerialScannerWorkbenchViewModel _scanner;
    private readonly Func<bool> _mayEdit;
    private readonly Action _openScannerSettings;
    private readonly Func<string> _hardwareStatus;
    private readonly Action _configurationChanged;
    private readonly DispatcherTimer _autosave;
    private StationProductionConfiguration _draft = StationProductionConfiguration.New();
    private bool _ready;
    private bool _dirty;
    private bool _busy;
    private bool _allowClose;
    private long _editGeneration;

    public ProductionPreparationWindow(
        SerialScannerWorkbenchViewModel scanner, Func<bool> mayEdit,
        Action openScannerSettings, Func<string> hardwareStatus,
        Action configurationChanged)
    {
        _scanner = scanner;
        _mayEdit = mayEdit;
        _openScannerSettings = openScannerSettings;
        _hardwareStatus = hardwareStatus;
        _configurationChanged = configurationChanged;
        InitializeComponent();
        _autosave = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(550) };
        _autosave.Tick += async (_, _) =>
        {
            _autosave.Stop();
            await SaveAsync();
        };
        _scanner.RawBarcodeObserved += OnRawBarcode;
        _scanner.PropertyChanged += OnScannerChanged;
        Loaded += async (_, _) => await LoadAsync();
        Closed += (_, _) =>
        {
            _autosave.Stop();
            _scanner.RawBarcodeObserved -= OnRawBarcode;
            _scanner.PropertyChanged -= OnScannerChanged;
        };
        Closing += OnClosing;
    }

    private void Say(string message) => StatusText.Text = message;

    private void OnRawBarcode(string raw) =>
        Dispatcher.BeginInvoke(new Action(() =>
        {
            ScanRaw.Text = raw;
            ScanObservedAt.Text = "真实扫码 · " + DateTime.Now.ToString("HH:mm:ss");
        }));

    private void OnScannerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SerialScannerWorkbenchViewModel.IsConnected) or
            nameof(SerialScannerWorkbenchViewModel.ConnectedPort) or
            nameof(SerialScannerWorkbenchViewModel.Status))
            Dispatcher.BeginInvoke(new Action(RefreshDevices));
    }

    private void RefreshDevices()
    {
        ScannerState.Text = _scanner.IsConnected
            ? "扫码枪已连接 · " + _scanner.ConnectedPort
            : "扫码枪未连接，请打开连接设置";
        MainBoardState.Text = _hardwareStatus();
        if (ScanRaw.Text == "等待扫码…" && !string.IsNullOrWhiteSpace(_scanner.LastRaw))
        {
            ScanRaw.Text = _scanner.LastRaw;
            ScanObservedAt.Text = "当前设备最近收到的内容";
        }
    }

    private async Task LoadAsync()
    {
        _ready = false;
        try
        {
            _draft = await StationProductionConfigurationStore.LoadDraftAsync();
            var fixture = _draft.Fixture;
            AutoClamp.IsChecked = fixture.AutoClampEnabled;
            Clamp1.IsChecked = fixture.UseClamp1;
            Clamp2.IsChecked = fixture.UseClamp2;
            FirstClampChoice.SelectedIndex = fixture.FirstClamp == 2 ? 1 : 0;
            StartInput.Text = DisplayInput(fixture.StartInput);
            StopInput.Text = DisplayInput(fixture.StopInput);
            InterlockInput.Text = DisplayInput(fixture.InterlockInput);
            ClosedFeedback.Text = DisplayInput(fixture.ClampClosedFeedback);
            OpenFeedback.Text = DisplayInput(fixture.ClampOpenFeedback);
            SettleDelayInput.Text = fixture.SettleDelayMs.ToString();
            _dirty = false;
            _ready = true;
            ResetOldConfig.Visibility = Visibility.Collapsed;
            RefreshFixture();
            RefreshDevices();
            Say("工位设备设置已加载，修改自动保存。");
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            ResetOldConfig.Visibility = Visibility.Visible;
            Say("本机工位设置无法读取。可重新设置，原文件将先备份。");
            System.Diagnostics.Trace.TraceError(
                "Cannot load station device settings: " + error);
        }
    }

    private static string DisplayInput(string value) =>
        value == "NOT_WIRED" ? "" : value;

    private static string InputValue(string value) =>
        string.IsNullOrWhiteSpace(value) ? "NOT_WIRED" : value.Trim();

    private void ConfigChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _dirty = true;
        _editGeneration++;
        RefreshFixture();
        _autosave.Stop();
        _autosave.Start();
    }

    private void RefreshFixture()
    {
        FixtureSummary.Text = AutoClamp.IsChecked != true
            ? "自动夹具未启用。可以在手动台架验证压紧和释放。"
            : Clamp1.IsChecked != true && Clamp2.IsChecked != true
                ? "请选择实际使用的夹具输出。"
                : "自动夹具已设置。机械是否到位仍须由可信反馈或现场检查确认。";
    }

    private StationProductionConfiguration CaptureDraft()
    {
        if (!int.TryParse(SettleDelayInput.Text, out var delay) ||
            delay is < 0 or > 30000)
            throw new InvalidDataException("机械稳定时间应为 0～30000 毫秒。");
        var fixture = _draft.Fixture with
        {
            AutoClampEnabled = AutoClamp.IsChecked == true,
            UseClamp1 = Clamp1.IsChecked == true,
            UseClamp2 = Clamp2.IsChecked == true,
            FirstClamp = FirstClampChoice.SelectedIndex == 1 ? 2 : 1,
            StartInput = InputValue(StartInput.Text),
            StopInput = InputValue(StopInput.Text),
            InterlockInput = InputValue(InterlockInput.Text),
            ClampClosedFeedback = InputValue(ClosedFeedback.Text),
            ClampOpenFeedback = InputValue(OpenFeedback.Text),
            SettleDelayMs = delay
        };
        var next = _draft with { Fixture = fixture, Revision = _draft.Revision + 1 };
        next.ValidateDraft();
        return next;
    }

    private async Task<bool> SaveAsync()
    {
        if (!_ready || !_dirty) return true;
        if (_busy) return false;
        if (!_mayEdit())
        {
            Say("工位正在执行设备操作，请完成后再修改设置。");
            return false;
        }
        StationProductionConfiguration next;
        try { next = CaptureDraft(); }
        catch (Exception error) when (error is ArgumentException or InvalidDataException)
        {
            Say("请检查输入内容：" + error.Message);
            return false;
        }
        var generation = _editGeneration;
        _busy = true;
        try
        {
            await StationProductionConfigurationStore.SaveDraftAsync(next);
            _draft = next;
            _configurationChanged();
            _dirty = generation != _editGeneration;
            if (_dirty)
            {
                _autosave.Stop();
                _autosave.Start();
            }
            else Say("已自动保存设备设置。正式检测仍须完成实体输入和记录验收。");
            return !_dirty;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Say("保存失败，当前修改仍在窗口中：" + error.Message);
            return false;
        }
        finally { _busy = false; }
    }

    private async void ResetOld_Click(object sender, RoutedEventArgs e)
    {
        if (_ready || !_mayEdit()) return;
        try
        {
            await StationProductionConfigurationStore.ResetUnreadableDraftAsync();
            await LoadAsync();
            if (_ready) Say("已重新设置，原文件已备份。请核对实际接线。");
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Say("无法重新设置工位，原文件未主动覆盖：" + error.Message);
        }
    }

    private void ScannerSettings_Click(object sender, RoutedEventArgs e) =>
        _openScannerSettings();

    private async void RetryScanner_Click(object sender, RoutedEventArgs e)
    {
        await _scanner.RetryAutoConnectAsync();
        RefreshDevices();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        if (_busy)
        {
            e.Cancel = true;
            Say("正在保存设备设置，请完成当前操作。");
            return;
        }
        if (!_dirty) return;
        e.Cancel = true;
        if (await SaveAsync())
        {
            _allowClose = true;
            Close();
        }
    }
}
