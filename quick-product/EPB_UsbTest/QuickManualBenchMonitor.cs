using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace EPB_UsbTest;

public partial class QuickManualBenchWindow
{
    private readonly ObservableCollection<string> _benchLogs = new();
    private readonly ObservableCollection<QuickBenchMonitorRow> _monitorRows = new();
    private readonly Dictionary<(byte Channel, bool Ide, uint Id), QuickBenchMonitorRow> _monitorIndex = new();
    private readonly ConcurrentQueue<CanFrame> _rxQueue = new();
    // A real frame history, independent of the grouped display; bounded for QUICK memory safety.
    private readonly List<QuickBenchRxCapture> _rxCaptured = new();
    private readonly DispatcherTimer _rxTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private const int MaxRxHistory = 100_000;
    private const int MaxRxQueue = 20_000;
    private bool _monitorPaused;
    private long _overflowedRawFrames;

    private void InitializeMonitor()
    {
        MonitorGrid.ItemsSource = _monitorRows;
        BenchLogList.ItemsSource = _benchLogs;
        _rxTimer.Tick += (_, _) => FlushBenchRx();
        _rxTimer.Start();
    }

    private void StopMonitor() => _rxTimer.Stop();

    private void HandleBenchRx(CanFrame frame)
    {
        if (frame.Channel > 1 || _closed) return;
        if (_rxQueue.Count >= MaxRxQueue)
        {
            Interlocked.Increment(ref _overflowedRawFrames);
            return;
        }
        _rxQueue.Enqueue(frame);
    }

    private void FlushBenchRx()
    {
        if (_closed) return;
        var count = 0;
        while (count++ < 500 && _rxQueue.TryDequeue(out var frame))
        {
            if (_rxCaptured.Count >= MaxRxHistory)
            {
                _overflowedRawFrames++;
                _rxCaptured.RemoveAt(0);
            }
            _rxCaptured.Add(new QuickBenchRxCapture(
                DateTime.Now, frame.Channel, frame.Ide, frame.Id, frame.Dlc, frame.DataText));
            if (_monitorPaused) continue;
            var key = (frame.Channel, frame.Ide, frame.Id);
            if (!_monitorIndex.TryGetValue(key, out var row))
            {
                row = new QuickBenchMonitorRow(frame);
                _monitorIndex.Add(key, row);
                _monitorRows.Add(row);
            }
            else row.Apply(frame);
        }
    }

    private void PauseMonitor_Click(object sender, RoutedEventArgs e)
    {
        _monitorPaused = !_monitorPaused;
        PauseMonitorButton.Content = _monitorPaused ? "恢复显示" : "暂停显示";
        Log(_monitorPaused
            ? "CAN 聚合显示已暂停；原始帧仍进入接收历史。"
            : "CAN 聚合显示恢复；从新帧起更新当前列表。");
    }

    private void ClearMonitor_Click(object sender, RoutedEventArgs e)
    {
        _monitorIndex.Clear();
        _monitorRows.Clear();
        Log("已清空实时聚合显示，原始 CAN 帧历史保留。");
    }

    private void ClearLogs_Click(object sender, RoutedEventArgs e)
    {
        _benchLogs.Clear();
        _benchLogs.Add(DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) +
                      "  已清空显示日志；接收历史没有清除。");
    }

    private static string Csv(string? value)
        => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";

    private async void ExportLogs_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出手动台架真实记录",
            Filter = "CSV 日志 (*.csv)|*.csv",
            FileName = "AutoTest-QUICK-ManualBench-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".csv"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            FlushBenchRx();
            var sb = new StringBuilder();
            sb.AppendLine("类型,时间,通道,IDE,ID,DLC,数据,说明");
            foreach (var frame in _rxCaptured)
                sb.Append("RX,").Append(Csv(frame.CapturedAt.ToString("O", CultureInfo.InvariantCulture)))
                    .Append(',').Append(Csv("CAN" + frame.Channel.ToString(CultureInfo.InvariantCulture)))
                    .Append(',').Append(frame.Extended ? "1" : "0")
                    .Append(',').Append(Csv("0x" + frame.Id.ToString("X", CultureInfo.InvariantCulture)))
                    .Append(',').Append(frame.Dlc.ToString(CultureInfo.InvariantCulture))
                    .Append(',').Append(Csv(frame.Data))
                    .Append(",").AppendLine(Csv("0x41 主控上报帧"));
            foreach (var log in _benchLogs)
                sb.Append("LOG,,,,,,,").AppendLine(Csv(log));
            sb.Append("META,,,,,,,").AppendLine(Csv(
                "当前会话只缓存最近 " + MaxRxHistory +
                " 条原始 RX；超出缓存/接收队列丢弃数量=" +
                Interlocked.Read(ref _overflowedRawFrames).ToString(CultureInfo.InvariantCulture)));
            await File.WriteAllTextAsync(dialog.FileName, sb.ToString(), new UTF8Encoding(true));
            Log("真实日志已导出；RX=" + _rxCaptured.Count +
                "，缓存溢出=" + Interlocked.Read(ref _overflowedRawFrames));
        }
        catch (Exception ex) { Log("日志导出失败：" + ex.Message); }
    }
}
