using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using System.Windows.Input;

namespace AutoTest.Desktop.Tools.Production;

public partial class LabProductionToolView : UserControl
{
    public LabProductionToolView()
    {
        InitializeComponent();
        DataContext = new LabProductionToolViewModel();
    }

    // Integration can inject the shared TEST/RECORD bridge-backed VM instead of the demo VM.
    public LabProductionToolView(LabProductionToolViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void CopyDiagnostics_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is LabProductionToolViewModel vm)
            Clipboard.SetText(vm.GetDiagnosticText());
    }

    private async void ExportDiagnostics_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not LabProductionToolViewModel vm) return;
        var dialog = new SaveFileDialog
        {
            Title = "导出 AutoTest 生产模拟诊断 JSONL（扫码默认脱敏）",
            FileName = "production-ui-" + System.DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + ".jsonl",
            DefaultExt = ".jsonl",
            Filter = "JSON Lines (*.jsonl)|*.jsonl"
        };
        if (dialog.ShowDialog() == true)
            await vm.ExportDiagnosticsAsync(dialog.FileName);
    }

    private void BarcodeInput_OnGotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (DataContext is LabProductionToolViewModel vm) vm.ScannerFieldFocused();
        if (sender is TextBox input) input.SelectAll();
    }

    private void BarcodeInput_OnLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (DataContext is LabProductionToolViewModel vm) vm.ScannerFieldLostFocus();
    }

    private void BarcodeInput_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (DataContext is LabProductionToolViewModel vm && sender is TextBox input)
            vm.ScannerTextChanged(input.Text, input.IsKeyboardFocused);
    }

    private void BarcodeInput_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        if (DataContext is LabProductionToolViewModel vm && sender is TextBox input)
        {
            vm.PreviewScannerEnter(input.Text, input.IsKeyboardFocused);
            // Selection prepares for next wedge scan without clearing the current preview.
            input.SelectAll();
        }
    }
}
