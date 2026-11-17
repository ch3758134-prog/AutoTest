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

    private void SelectNamedOperation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button
            { Tag: ManualBenchViewModel.NamedOperation operation })
            _model.SelectNamedOperation(operation);
    }
}
