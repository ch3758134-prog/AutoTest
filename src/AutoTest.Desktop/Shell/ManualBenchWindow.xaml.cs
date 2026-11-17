using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using AutoTest.Desktop.Shell;

namespace AutoTest.Desktop.Shell;

public partial class ManualBenchWindow : Window
{
    private readonly ManualBenchViewModel _model;

    public ManualBenchWindow(ManualBenchViewModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        DataContext = model;
        InitializeComponent();
        Closed += (_, _) => _model.Dispose();
    }

    private void ExportLog_Click(object sender, RoutedEventArgs e)
    {
        var chooser = new SaveFileDialog
        {
            Title = "导出真实手动台架操作记录",
            Filter = "JSON Lines (*.jsonl)|*.jsonl",
            FileName = "AutoTest_Bench_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".jsonl"
        };
        if (chooser.ShowDialog(this) != true) return;
        try
        {
            var lines = _model.Logs.Select(entry => JsonSerializer.Serialize(entry)).ToArray();
            File.WriteAllLines(chooser.FileName, lines, new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "导出失败",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CopyCanConfirmation_Click(object sender, RoutedEventArgs e) =>
        Clipboard.SetText(_model.CanExpectedConsent);

    private void CopyKeyConfirmation_Click(object sender, RoutedEventArgs e) =>
        Clipboard.SetText(_model.KeyExpectedConsent);

    private void CopyClampConfirmation_Click(object sender, RoutedEventArgs e) =>
        Clipboard.SetText(_model.ClampExpectedConsent);

    private async void IgnOn_Click(object sender, RoutedEventArgs e) =>
        await _model.ApplyIgnSelectionAsync(true);
    private async void IgnOff_Click(object sender, RoutedEventArgs e) =>
        await _model.ApplyIgnSelectionAsync(false);

    private async void ApplyNamedOperation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button
            { Tag: ManualBenchViewModel.NamedOperation action })
            await _model.ApplyNamedOperationAsync(action);
    }

    private void EditNamedOperation_RightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.Button
            { Tag: ManualBenchViewModel.NamedOperation action })
        {
            _model.SelectNamedOperation(action);
            e.Handled = true;
        }
    }

    private async void Clamp1Press_Click(object sender, RoutedEventArgs e) =>
        await _model.ApplyClampSelectionAsync(1, true);
    private async void Clamp1Release_Click(object sender, RoutedEventArgs e) =>
        await _model.ApplyClampSelectionAsync(1, false);
    private async void Clamp2Press_Click(object sender, RoutedEventArgs e) =>
        await _model.ApplyClampSelectionAsync(2, true);
    private async void Clamp2Release_Click(object sender, RoutedEventArgs e) =>
        await _model.ApplyClampSelectionAsync(2, false);

    private void SelectNamedOperation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button
            { Tag: ManualBenchViewModel.NamedOperation operation })
            _model.SelectNamedOperation(operation);
    }
}
