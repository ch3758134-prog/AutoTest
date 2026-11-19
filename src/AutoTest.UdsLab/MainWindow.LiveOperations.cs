using System.Globalization;
using System.Windows;
using AutoTest.Specialists.Uds;
using AutoTest.Specialists.Uds.Workbench;

namespace AutoTest.UdsLab;

public partial class MainWindow
{
    private const int WatchMaxAttempts = 30;
    private const int WatchMaxSeconds = 90;
    private bool _seriesRunning;

    /// <summary>
    /// Shared read-only preflight for an immediate request, manual sequence
    /// and bounded repeat. This is NOT physical permission; UdsClient and the
    /// DEVICE PhysicalCommandFence still independently validate every request/frame.
    /// </summary>
    private static byte[] ValidateLiveRead(DiagnosticAction action, bool repeated)
    {
        var bytes = UdsHex.Parse(action.RequestHex);
        var catalog = new UdsServiceCatalog();
        var descriptor = catalog.Get(bytes[0]);
        if (descriptor.Risk != UdsRiskLevel.ReadOnly || bytes[0] == 0x2A)
            throw new UnauthorizedAccessException(
                "当前只允许 0x19/0x22/0x23/0x24 的只读实体诊断。会话切换、TesterPresent、例程、清故障、写入和烧录尚未批准实体发送。");
        if (bytes.Length < descriptor.MinimumRequestLength)
            throw new FormatException("UDS 请求数据不足：" + descriptor.Name);
        if (bytes[0] == 0x22 && (bytes.Length - 1) % 2 != 0)
            throw new FormatException("0x22 请求 DID 必须按两个字节成组。");
        if (repeated && !(bytes[0] == 0x22 && bytes.Length == 3))
            throw new UnauthorizedAccessException(
                "受控重复读取目前仅允许单个 DID 的 0x22 请求；其他服务请使用手动交互清单。");
        if (!string.IsNullOrWhiteSpace(action.ExpectedPrefixHex))
            _ = UdsHex.Parse(action.ExpectedPrefixHex);
        return bytes;
    }

    private void RefreshSequenceButtons()
    {
        bool free = _operation is null && _pendingRequest is null && !_seriesRunning;
        bool ready = free && _hardware?.IsReady == true && !_requiresReconnect;
        QueueNextButton.IsEnabled = ready && _manualQueue.Count > 0;
        QueueRunButton.IsEnabled = ready && _manualQueue.Count > 0;
        WatchStartButton.IsEnabled = ready && _editing is not null;
    }

    private async void QueueRun_Click(object sender, RoutedEventArgs e)
    {
        var plans = _manualQueue.ToArray();
        await ExecuteSeriesAsync(plans, watch: false, intervalMs: 0);
    }

    private async void WatchStart_Click(object sender, RoutedEventArgs e)
    {
        if (_editing is null) return;
        if (!int.TryParse(WatchIntervalBox.Text, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int interval) ||
            interval is < 1000 or > 60000)
        {
            MessageBox.Show(this, "读取间隔必须为 1000 至 60000 毫秒。", "重复读取");
            return;
        }
        await ExecuteSeriesAsync([_editing], watch: true, intervalMs: interval);
    }

    private async Task ExecuteSeriesAsync(
        IReadOnlyList<DiagnosticAction> actions, bool watch, int intervalMs)
    {
        if (_operation is not null || _pendingRequest is not null || _seriesRunning)
        {
            MessageBox.Show(this, "已有 UDS 请求正在执行；同一 ECU 不能同时执行多项诊断。");
            return;
        }
        if (_hardware?.IsReady != true || _requiresReconnect)
        {
            MessageBox.Show(this, "先连接真实主控。失联、超时或取消后，需先断开并重新连接。");
            return;
        }
        if (actions.Count == 0 || actions.Count > 100)
        {
            MessageBox.Show(this, "交互清单须包含 1 至 100 个已定义操作。");
            return;
        }

        (DiagnosticAction Action, byte[] Request)[] plans;
        try
        {
            plans = actions.Select(a =>
            {
                var copy = a.Duplicate();
                copy.Name = a.Name;
                return (copy, ValidateLiveRead(copy, repeated: watch));
            }).ToArray();

            if (_hardware.LeaseExpiresUtc is null)
                throw new InvalidOperationException("未取得真实 CAN 租约。");
        }
        catch (Exception ex)
        {
            ShowFailure("交互执行预检失败", ex);
            return;
        }

        // Nothing is sent by adding an action to the list. The user explicitly
        // starts this run; the physical gate still demands human consent
        // separately for every 15-byte CAN frame.
        _operation = new CancellationTokenSource();
        _seriesRunning = true;
        var token = _operation.Token;
        var begun = DateTimeOffset.UtcNow;
        int complete = 0, attempts = watch ? WatchMaxAttempts : plans.Length;
        RefreshSendButton();
        CancelButton.IsEnabled = true;
        string label = watch ? "重复读取" : "顺序交互";
        SeriesStatus.Text = label + "已开始（逐帧人工确认依然有效）。";
        Audit("SERIES_BEGIN", label + " · 操作数=" + plans.Length +
            (watch ? " · 间隔=" + intervalMs + "ms · 最多30次/90秒" : " · 每项执行一次"));
        try
        {
            for (int index = 0; index < attempts; index++)
            {
                token.ThrowIfCancellationRequested();
                if (watch && DateTimeOffset.UtcNow - begun >= TimeSpan.FromSeconds(WatchMaxSeconds))
                    break;
                if (_requiresReconnect || _hardware?.IsReady != true)
                {
                    SeriesStatus.Text = "运行已停止：诊断链路失效或需重新连接。";
                    Audit("SERIES_STOP", "设备失效或需要重新连接");
                    break;
                }
                var current = plans[watch ? 0 : index];
                SeriesStatus.Text = label + " " + (index + 1) + "/" + attempts +
                    " · " + current.Action.Name;
                Audit("SERIES_ITEM", SeriesStatus.Text);

                Task<bool> attempt = RunRequestAsync(current.Action,
                    current.Request, Guid.NewGuid(), token);
                _pendingRequest = attempt;
                bool accepted = await attempt;
                _pendingRequest = null;
                if (!accepted)
                {
                    SeriesStatus.Text = label + "因 ECU 否定响应、数据不匹配或通信异常停止，已完成 " +
                        complete + " 项。";
                    Audit("SERIES_STOP", SeriesStatus.Text);
                    break;
                }
                complete++;
                SeriesStatus.Text = label + "已确认 " + complete + "/" + attempts + " 次";
                if (watch && index < attempts - 1)
                {
                    token.ThrowIfCancellationRequested();
                    // No backlog: the next request starts only after the last
                    // request has received a final valid response.
                    await Task.Delay(intervalMs, token);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            SeriesStatus.Text = label + "由用户停止；已确认 " + complete + " 次。";
            Audit("SERIES_CANCEL", SeriesStatus.Text);
        }
        catch (Exception ex)
        {
            _requiresReconnect = true;
            SeriesStatus.Text = label + "异常终止：" + ex.Message;
            Audit("SERIES_ERROR", ex.ToString());
        }
        finally
        {
            Audit("SERIES_END", SeriesStatus.Text);
            _pendingRequest = null;
            _operation?.Dispose();
            _operation = null;
            _seriesRunning = false;
            CancelButton.IsEnabled = false;
            RefreshSendButton();
        }
    }
}
