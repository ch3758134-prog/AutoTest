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
            status => ScanStatus = status,
            () => !_realTest.IsRunning &&
                !(_automaticTestHome?.IsOperatorRunning ?? false) &&
                !_hardwareBusy && !IsBusy);
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

    private void CloseSerialScannerWindow()
    {
        if (_serialScannerWindow is { IsVisible: true } window)
            window.Close();
    }
}
