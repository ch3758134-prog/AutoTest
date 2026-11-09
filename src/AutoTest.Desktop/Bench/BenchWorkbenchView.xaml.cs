using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace AutoTest.Desktop.Bench;

public partial class BenchWorkbenchView : UserControl
{
    // INTEGRATION registers this View + VM in the existing Prism Desktop container.
    public BenchWorkbenchView(BenchWorkbenchViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is BenchWorkbenchViewModel vm)
            await vm.InitializeAsync();
    }

    private void OnCopyEvidence(object sender, RoutedEventArgs e)
    {
        if (DataContext is not BenchWorkbenchViewModel vm) return;
        try
        {
            Clipboard.SetText(vm.ExportEvidenceTsv());
        }
        catch (Exception ex)
        {
            MessageBox.Show($"复制证据失败：{ex.Message}", "手动台架",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnExportEvidence(object sender, RoutedEventArgs e)
    {
        if (DataContext is not BenchWorkbenchViewModel vm) return;
        var dialog = new SaveFileDialog
        {
            Title = "导出本页操作证据（不包含 DEVICE/RECORD 的独立原始 Trace）",
            FileName = "AutoTest_Bench_Evidence",
            Filter = "JSON 证据 (*.json)|*.json|TSV 表格 (*.tsv)|*.tsv",
            FilterIndex = 1,
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog() != true) return;

        var acknowledgement = MessageBox.Show(
            "导出包含原始请求/响应和设备身份。请检查是否含有敏感信息后再对外共享。" +
            "\n此文件是 Bench UI 本地证据，不包含完整 DEVICE/RECORD 原始 Trace；ACK 不证明实际物理动作。继续导出？",
            "确认导出范围", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (acknowledgement != MessageBoxResult.Yes) return;
        try
        {
            var content = dialog.FilterIndex == 2 ? vm.ExportEvidenceTsv() : vm.ExportEvidenceJson();
            File.WriteAllText(dialog.FileName, content,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: dialog.FilterIndex == 2));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导出失败：{ex}", "手动台架",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
