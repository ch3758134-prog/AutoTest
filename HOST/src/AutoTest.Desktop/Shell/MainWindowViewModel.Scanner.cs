using System.IO;
using System.Windows;
using Prism.Commands;

namespace AutoTest.Desktop.Shell;

public sealed partial class MainWindowViewModel
{
    private SerialScannerWorkbenchViewModel _scannerWorkbench = null!;
    private SerialScannerWindow? _serialScannerWindow;

    internal SerialScannerWorkbenchViewModel ScannerSetupModel => _scannerWorkbench;

    public DelegateCommand OpenSerialScannerCommand { get; private set; } = null!;

    private void InitializeScannerWorkbench()
    {
        _scannerWorkbench = new SerialScannerWorkbenchViewModel(
            _workspace,
            status => ScanStatus = status,
            () => !_realTest.IsRunning &&
                !(_automaticTestHome?.IsOperatorRunning ?? false) &&
                !_hardwareBusy && !IsBusy,
            OpenRegisteredProjectFromScanAsync);
        OpenSerialScannerCommand = new DelegateCommand(OpenSerialScannerWindow);
    }

    private void OpenSerialScannerWindow()
    {
        if (_serialScannerWindow is { IsVisible: true } existing)
        {
            existing.Activate();
            return;
        }
        var window = new SerialScannerWindow(_scannerWorkbench);
        if (global::System.Windows.Application.Current?.MainWindow is { IsLoaded: true } main)
            window.Owner = main;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_serialScannerWindow, window))
                _serialScannerWindow = null;
        };
        _serialScannerWindow = window;
        window.Show();
    }

    private async Task<bool> OpenRegisteredProjectFromScanAsync(string manifestPath)
    {
        if (_realTest.IsRunning || _hardwareBusy || _hardware.HasSession || IsBusy ||
            _hardware.IsManualCanBatchApproved || _hardware.IsManualKeyActionApproved)
        {
            StatusMessage = "工位仍有连接或执行许可，不能扫码切换型号。请先安全停止并断开主控。";
            return false;
        }
        if (_manualBenchWindow is { IsVisible: true })
        {
            StatusMessage = "请先关闭手动台架再切换 ECU 型号工程。";
            return false;
        }
        if (!await PrepareEditorsForCloseAsync()) return false;
        // A scan switches only the managed ECU project. Independent DBC
        // windows retain their own files; live wave evidence must finish
        // finalization before the project / USB ownership can change.
        if (!await PrepareDbcWindowsForCloseAsync(exitingApplication: false)) return false;
        if (_waveWorkbenchWindow is { IsVisible: true } wave &&
            !await wave.PrepareForParentShutdownAsync()) return false;
        try
        {
            await _workspace.OpenAsync(manifestPath);
            StatusMessage = "已明确切换到扫码型号的工程：" +
                _workspace.Current?.Definition.Name + "；仍未执行设备动作。";
            return true;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or
                                      UnauthorizedAccessException or ArgumentException or
                                      AutoTest.Projects.Project.ProjectDataException)
        {
            StatusMessage = "扫码型号工程切换失败：" + error.Message;
            return false;
        }
    }

    private void CloseSerialScannerWindow()
    {
        if (_serialScannerWindow is { IsVisible: true } window)
            window.Close();
    }
}
