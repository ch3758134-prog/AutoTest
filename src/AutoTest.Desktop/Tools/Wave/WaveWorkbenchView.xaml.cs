using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.IO;
using System.Text.Json;
using AutoTest.Application.V1;
using AutoTest.Devices.Diagnostics;
using AutoTest.Devices.Usb;
using AutoTest.Projects.Resources;

namespace AutoTest.Desktop.Tools.Wave;

public partial class WaveWorkbenchView : UserControl
{
    public const string NavigationKey = "WaveWorkbench";
    private Func<HardwareBenchController?>? _deviceSource;
    private Func<V1Workspace>? _workspaceSource;
    private CancellationTokenSource? _archiveStop;

    public void AttachArchiveCapture(Func<HardwareBenchController?> device,
        Func<V1Workspace> workspace)
    {
        _deviceSource = device ?? throw new ArgumentNullException(nameof(device));
        _workspaceSource = workspace ?? throw new ArgumentNullException(nameof(workspace));
    }

    public void CancelArchiveRecording() => _archiveStop?.Cancel();

    public WaveWorkbenchView()
    {
        InitializeComponent();
        DataContext = new WaveWorkbenchViewModel();
    }

    public WaveWorkbenchViewModel Model => (WaveWorkbenchViewModel)DataContext;

    private async void RecordCanArchive_Click(object sender, RoutedEventArgs e)
    {
        if (_archiveStop is not null) return;
        CancellationTokenSource? cancel = null;
        try
        {
            var controller = _deviceSource?.Invoke();
            var workspace = _workspaceSource?.Invoke();
            if (controller is null || controller.State != UsbSessionState.Connected ||
                !controller.ProtocolVersionVerified || controller.Hub is null ||
                workspace?.DbcAssetId is null)
                throw new InvalidOperationException("先在设备页连接并验证主控，再在工程中登记 DBC。");
            var asset = workspace.Assets.Single(x => x.Id == workspace.DbcAssetId.Value);
            var revision = asset.ToReference();
            var generation = controller.Generation;
            var hub = controller.Hub;
            var picker = new Microsoft.Win32.SaveFileDialog
            {
                Title = "保存真实 CAN1 RX 帧档案（5 秒）",
                Filter = "AutoTest CAN RX JSON (*.json)|*.json",
                FileName = "autotest-can1-rx-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json",
                AddExtension = true,
                OverwritePrompt = true
            };
            if (picker.ShowDialog(Window.GetWindow(this)) != true) return;
            cancel = new CancellationTokenSource();
            _archiveStop = cancel;
            RecordCanButton.IsEnabled = false;
            ArchiveCaptureStatus.Text = "正在记录真实 CAN1 RX，最多 5 秒 / 50,000 帧；不发送 CAN。";
            bool StillCurrent()
            {
                if (controller.State != UsbSessionState.Connected ||
                    controller.Generation != generation ||
                    workspace.DbcAssetId != asset.Id)
                    return false;
                var current = workspace.Assets.SingleOrDefault(x => x.Id == asset.Id);
                return current is not null && current.Revision == asset.Revision &&
                    string.Equals(current.Sha256, asset.Sha256, StringComparison.OrdinalIgnoreCase);
            }
            var archive = await R1RecordedCanArchiveCapture.CaptureAsync(
                hub, StillCurrent, revision, generation, TimeSpan.FromSeconds(5),
                50_000, cancel.Token);
            // Only complete archives get written. The downstream loader revalidates
            // the DBC identity, ordered offsets and actual frame format.
            var bytes = JsonSerializer.SerializeToUtf8Bytes(archive,
                new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllBytesAsync(picker.FileName, bytes, cancel.Token);
            Model.ArchivePath = picker.FileName;
            await Model.OpenArchiveSelectedAsync();
            ArchiveCaptureStatus.Text = "真实 CAN1 RX 已保存 " + archive.Frames.Length +
                " 帧；可执行独立 Replay。任何丢帧均保留在档案 Loss 字段。";
        }
        catch (OperationCanceledException)
        {
            ArchiveCaptureStatus.Text = "记录已取消；不导出未完成的 CAN RX 档案。";
        }
        catch (Exception ex)
        {
            ArchiveCaptureStatus.Text = "CAN1 记录失败：" + ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_archiveStop, cancel)) _archiveStop = null;
            RecordCanButton.IsEnabled = true;
            cancel?.Dispose();
        }
    }

    private void CancelCanArchive_Click(object sender, RoutedEventArgs e)
        => CancelArchiveRecording();

    private void ChooseArchive_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "AutoTest CAN RX trace (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog() == true)
        {
            Model.ArchivePath = dialog.FileName;
            if (Model.OpenArchiveCommand.CanExecute(null))
                Model.OpenArchiveCommand.Execute(null);
        }
    }

    private void ChartCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var x = e.GetPosition(ChartCanvas).X;
        var second = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        Model.SetCursorFraction((x - 48) / 852, second);
        e.Handled = true;
    }
}
