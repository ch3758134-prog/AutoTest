using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using EPB.Core.Communication;

namespace EPB_UsbTest;

public partial class QuickManualBenchWindow : Window
{
    private readonly UsbLink _usb;
    private readonly Func<CanDatabase?> _database;
    private readonly Func<string?> _configurationIdentity;
    private readonly Func<bool> _manualAllowed;
    private readonly Action<string> _mainLog;
    private readonly SemaphoreSlim _deviceGate = new(1, 1);
    private readonly ObservableCollection<QuickBenchCanRow> _canRows = new();
    private readonly ObservableCollection<QuickBenchDbcChoice> _dbcChoices = new();
    private readonly ObservableCollection<QuickBenchSignalValue> _signalValues = new();
    private bool _loadingCan, _verified, _deviceBusy, _periodicOwned, _periodicUncertain, _lostOwnedPeriodic, _closed;
    private string _knownFirmware = "";
    public bool HasActiveManualOperation => _periodicOwned || _lostOwnedPeriodic || _deviceBusy || _keyRunning;

    public QuickManualBenchWindow(UsbLink usb, Func<CanDatabase?> database,
        Func<string?> configurationIdentity, Func<bool> manualAllowed, Action<string> mainLog)
    {
        _usb = usb;
        _database = database;
        _configurationIdentity = configurationIdentity;
        _manualAllowed = manualAllowed;
        _mainLog = mainLog;
        InitializeComponent();
        CanTxGrid.ItemsSource = _canRows;
        CanDbcCombo.ItemsSource = _dbcChoices;
        CanSignalsGrid.ItemsSource = _signalValues;
        InitializeKeyEditor();
        InitializeMonitor();
        _usb.CanFrameReceived += HandleBenchRx;
        _usb.ConnectionChanged += HandleConnectionChanged;
    }

    private string StoragePath(string purpose)
    {
        var identity = _configurationIdentity() ?? "quick-no-config";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..14];
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EPB_UsbTest");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "quick-bench-" + purpose + "-" + hash + ".json");
    }

    private async void Bench_Loaded(object sender, RoutedEventArgs e)
    {
        RestoreCanRows();
        RefreshDbcChoices();
        RestoreKeyActions();
        UpdateConnectionText();
        if (_usb.IsOpen) await ReadHardwareStateAsync();
    }

    private void Bench_Closing(object? sender, CancelEventArgs e)
    {
        if (_periodicOwned || _lostOwnedPeriodic || _keyRunning || _deviceBusy)
        {
            Log("手动台架仍有活动或状态未知的真实输出任务。不会通过关闭窗口隐式停止设备。");
            MessageBox.Show(this,
                "当前 KEY 动作正在执行、主控周期广播仍在运行，或断线后其状态未知。\n" +
                "请先停止相应任务、确认设备状态，再关闭窗口。不会自动释放 KEY / IGN / 夹具。",
                "真实设备状态尚未收尾", MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Cancel = true;
        }
    }

    private void Bench_Closed(object? sender, EventArgs e)
    {
        _closed = true;
        _usb.CanFrameReceived -= HandleBenchRx;
        _usb.ConnectionChanged -= HandleConnectionChanged;
        StopMonitor();
        _deviceGate.Dispose();
    }

    private void HandleConnectionChanged(bool connected)
    {
        if (_closed || Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _verified = false;
            if (!connected && _periodicOwned)
            {
                _periodicOwned = false;
                _periodicUncertain = true;
                _lostOwnedPeriodic = true;
                Log("USB 断开，主控周期广播运行情况 UNKNOWN，恢复连接后需人工读状态/停止。");
            }
            if (!connected)
            {
                _lastAppliedKeyMask = null;
                KeyMaskInfo.Text = "USB 已断开：真实 KEY_SIM 开关状态 UNKNOWN，未自动释放。";
            }
            UpdateConnectionText();
        }));
    }

    private void UpdateConnectionText()
    {
        if (_closed) return;
        DeviceStateText.Text = _usb.IsOpen ? "USB 已连接 · CAN1 DUT / CAN0 KEY_SIM" : "USB 未连接";
        FirmwareStateText.Text = _verified ? "协议已核验：" + _knownFirmware : "固件未核验";
        ConnectButton.Content = _usb.IsOpen ? "断开主控" : "连接主控";
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (!_manualAllowed()) { Log("自动测试正在运行，手动台架不能切换共享 USB 会话。"); return; }
        if (_deviceBusy) { Log("当前正在等待硬件命令完成。"); return; }
        if (_usb.IsOpen)
        {
            _usb.Close();
            UpdateConnectionText();
            return;
        }
        var error = _usb.Open();
        if (!string.IsNullOrWhiteSpace(error))
        {
            Log("USB 连接失败：" + error);
            return;
        }
        UpdateConnectionText();
        await ReadHardwareStateAsync();
    }

    private async void ReadHardware_Click(object sender, RoutedEventArgs e)
        => await ReadHardwareStateAsync();

    private async Task ReadHardwareStateAsync()
    {
        if (!_usb.IsOpen) { Log("主控未连接；读取失败。"); UpdateConnectionText(); return; }
        if (!await _deviceGate.WaitAsync(0)) { Log("USB 正在执行其他手动操作，请稍后读状态。"); return; }
        _deviceBusy = true;
        try
        {
            _verified = false;
            var ping = await _usb.SendAndWaitAsync(Frame.CMD_PING, [], 2000);
            if (!ping.ok || ping.status != Frame.STS_OK ||
                Encoding.ASCII.GetString(ping.payload) != "PONG")
            {
                Log("PING 失败，不能执行实体控制：" +
                    (ping.err.Length > 0 ? ping.err : Frame.StatusName(ping.status)));
                return;
            }
            var version = await _usb.SendAndWaitAsync(Frame.CMD_GET_VERSION, [], 2000);
            if (!version.ok || version.status != Frame.STS_OK ||
                version.payload.Length is < 1 or > 80)
            {
                Log("GET_VERSION 失败，不能执行实体控制。");
                return;
            }
            _knownFirmware = Encoding.ASCII.GetString(version.payload);
            _verified = _knownFirmware == Frame.MAIN_CONTROL_V04_VERSION;
            Log("主控 PING 正常，固件=" + _knownFirmware +
                (_verified ? "；匹配当前 v0.4 协议。" : "；版本不匹配，实体控制被阻断。"));
            if (!_verified) return;
            var ign = await _usb.SendAndWaitAsync(Frame.CMD_GET_IGN, [], 2000);
            IgnValueText.Text = ign.ok && ign.status == Frame.STS_OK &&
                                ign.payload.Length == 1 && ign.payload[0] <= 1
                ? "GPIO: " + ign.payload[0] + (ign.payload[0] == 1 ? " (ON)" : " (OFF)")
                : "GPIO: UNKNOWN";
            var clamps = await _usb.SendAndWaitAsync(Frame.CMD_GET_CLAMPS, [], 2000);
            if (clamps.ok && clamps.status == Frame.STS_OK &&
                clamps.payload.Length == 1 && clamps.payload[0] <= 3)
            {
                Clamp1Text.Text = "GPIO: " + (clamps.payload[0] & 1);
                Clamp2Text.Text = "GPIO: " + ((clamps.payload[0] >> 1) & 1);
            }
            else
            {
                Clamp1Text.Text = "GPIO: UNKNOWN";
                Clamp2Text.Text = "GPIO: UNKNOWN";
            }
            Log("已读回 IGN / 夹具 MCU GPIO；不代表 ECU 电压或气缸机械到位。");
            var broadcast = await _usb.SendAndWaitAsync(Frame.CMD_BROADCAST_STATUS, [], 2000);
            if (broadcast.ok && broadcast.status == Frame.STS_OK && broadcast.payload.Length >= 2)
            {
                Log("广播状态：running=" + broadcast.payload[0] +
                    "，count=" + broadcast.payload[1] + "；不自动改变广播。");
                if (broadcast.payload[0] == 0) { _periodicOwned = false; _periodicUncertain = false; _lostOwnedPeriodic = false; }
                else if (!_periodicOwned) _periodicUncertain = true;
            }
            else Log("广播状态未知；不要假定主控周期任务已停止。");
        }
        catch (Exception ex) { Log("只读检查异常：" + ex.Message); }
        finally
        {
            _deviceBusy = false;
            _deviceGate.Release();
            UpdateConnectionText();
        }
    }

    private bool CanWrite(string action)
    {
        if (!_manualAllowed()) { Log(action + " 被阻断：自动测试正在运行。"); return false; }
        if (!_usb.IsOpen) { Log(action + " 被阻断：USB 未连接。"); return false; }
        if (!_verified) { Log(action + " 被阻断：未确认 Main_Control v0.4 USB 协议。"); return false; }
        return true;
    }

    private async Task RunPhysicalAsync(string action, Func<Task> send)
    {
        if (!CanWrite(action)) return;
        if (!await _deviceGate.WaitAsync(0))
        {
            Log(action + " 被阻断：另一个真实硬件请求正在执行。");
            return;
        }
        _deviceBusy = true;
        try { await send(); }
        catch (OperationCanceledException) { Log(action + " 已取消；设备先前的输出不自动清零。"); }
        catch (Exception ex) { Log(action + " 错误：" + ex.Message); }
        finally { _deviceBusy = false; _deviceGate.Release(); }
    }

    private async Task<(bool ok, byte[] data)> CommandAsync(byte cmd, byte[] payload, string desc)
    {
        var result = await _usb.SendAndWaitAsync(cmd, payload, 2000);
        if (!result.ok || result.status != Frame.STS_OK)
        {
            Log(desc + " 失败：" +
                (result.err.Length > 0 ? result.err : Frame.StatusName(result.status)));
            return (false, result.payload);
        }
        Log(desc + " ACK OK（仅代表主控接受/处理请求，不代表 DUT 已执行）。");
        return (true, result.payload);
    }

    private async void IgnOn_Click(object sender, RoutedEventArgs e)
        => await ChangeIgnAsync(true);
    private async void IgnOff_Click(object sender, RoutedEventArgs e)
        => await ChangeIgnAsync(false);
    private Task ChangeIgnAsync(bool enabled)
        => RunPhysicalAsync("IGN " + (enabled ? "ON" : "OFF"), async () =>
        {
            var r = await CommandAsync(Frame.CMD_SET_IGN, new[] { enabled ? (byte)1 : (byte)0 },
                "IGN " + (enabled ? "ON" : "OFF"));
            if (!r.ok) return;
            var state = await _usb.SendAndWaitAsync(Frame.CMD_GET_IGN, [], 2000);
            IgnValueText.Text = state.ok && state.status == Frame.STS_OK &&
                                state.payload.Length == 1
                ? "GPIO: " + state.payload[0]
                : "GPIO: UNKNOWN";
        });

    private async void Clamp1Press_Click(object sender, RoutedEventArgs e) => await ClampAsync(1, true);
    private async void Clamp1Release_Click(object sender, RoutedEventArgs e) => await ClampAsync(1, false);
    private async void Clamp2Press_Click(object sender, RoutedEventArgs e) => await ClampAsync(2, true);
    private async void Clamp2Release_Click(object sender, RoutedEventArgs e) => await ClampAsync(2, false);

    private Task ClampAsync(byte index, bool press)
    {
        var name = "夹具 " + index + (press ? " 压紧" : " 释放");
        if (MessageBox.Show(this,
                "即将向真实主控发送 " + name + " 指令。\n请确认现场夹具区域安全、无人员处于动作范围。\n" +
                "注意：读取 GPIO 并不证明夹具机械到位。",
                "确认真实夹具动作", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return Task.CompletedTask;
        return RunPhysicalAsync(name, async () =>
        {
            var sent = await CommandAsync(Frame.CMD_SET_CLAMP, Frame.ClampPayload(index, press), name);
            if (!sent.ok) return;
            var state = await _usb.SendAndWaitAsync(Frame.CMD_GET_CLAMPS, [], 2000);
            if (state.ok && state.status == Frame.STS_OK &&
                state.payload.Length == 1 && state.payload[0] <= 3)
            {
                Clamp1Text.Text = "GPIO: " + (state.payload[0] & 1);
                Clamp2Text.Text = "GPIO: " + ((state.payload[0] >> 1) & 1);
            }
            else { Clamp1Text.Text = "GPIO: UNKNOWN"; Clamp2Text.Text = "GPIO: UNKNOWN"; }
        });
    }

    private static string DbcIdentity(CanMessageDefinition message)
        => !string.IsNullOrWhiteSpace(message.DefinitionId) ? message.DefinitionId :
            (message.Extended ? "E:" : "S:") +
            message.Id.ToString("X", CultureInfo.InvariantCulture) + ":" + message.Name;

    private void RefreshDbcChoices()
    {
        var current = CanDbcCombo.SelectedItem as QuickBenchDbcChoice;
        _dbcChoices.Clear();
        foreach (var message in _database()?.Messages ?? new List<CanMessageDefinition>())
            _dbcChoices.Add(new QuickBenchDbcChoice(message));
        if (current is not null)
            CanDbcCombo.SelectedItem = _dbcChoices.FirstOrDefault(
                choice => DbcIdentity(choice.Message) == DbcIdentity(current.Message));
    }

    private void ReloadDbc_Click(object sender, RoutedEventArgs e)
    {
        RefreshDbcChoices();
        Log("已刷新当前 QUICK 配置的 DBC 报文选择。" +
            (_dbcChoices.Count == 0 ? "尚无 DBC；可使用原始 HEX。" : ""));
    }

    private void RestoreCanRows()
    {
        try
        {
            var file = StoragePath("can");
            if (File.Exists(file))
            {
                var loaded = JsonSerializer.Deserialize<List<QuickBenchCanRow>>(File.ReadAllText(file));
                if (loaded is not null)
                    foreach (var row in loaded.Take(32)) { row.Status = "未发送"; _canRows.Add(row); }
            }
        }
        catch (Exception ex) { Log("读取 QUICK CAN 发送表失败：" + ex.Message); }
        if (_canRows.Count == 0) _canRows.Add(new QuickBenchCanRow { Name = "CAN 报文 1" });
        CanTxGrid.SelectedItem = _canRows[0];
    }

    private void PersistCanRows()
    {
        try
        {
            var rows = _canRows.ToArray();
            File.WriteAllText(StoragePath("can"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
            Log("CAN 发送列表已保存在 QUICK 当前配置专用的本地工作台设置中。");
        }
        catch (Exception ex) { Log("保存 CAN 列表失败：" + ex.Message); }
    }

    private void AddCan_Click(object sender, RoutedEventArgs e)
    {
        if (_canRows.Count >= 32) { Log("QUICK 最多编辑 32 条 CAN1 报文。"); return; }
        try { SaveCanEditor(); }
        catch (Exception ex) { Log("请先修正当前报文：" + ex.Message); return; }
        var row = new QuickBenchCanRow { Name = "CAN 报文 " + (_canRows.Count + 1) };
        _canRows.Add(row);
        CanTxGrid.SelectedItem = row;
    }
    private void CopyCan_Click(object sender, RoutedEventArgs e)
    {
        try { SaveCanEditor(); }
        catch (Exception ex) { Log("请先修正当前报文：" + ex.Message); return; }
        if (CanTxGrid.SelectedItem is not QuickBenchCanRow row || _canRows.Count >= 32) return;
        var copy = row.Clone();
        _canRows.Insert(_canRows.IndexOf(row) + 1, copy);
        CanTxGrid.SelectedItem = copy;
    }
    private void DeleteCan_Click(object sender, RoutedEventArgs e)
    {
        if (_periodicOwned) { Log("周期广播运行中，先显式停止再修改列表。"); return; }
        if (CanTxGrid.SelectedItem is not QuickBenchCanRow row) return;
        _canRows.Remove(row);
        CanTxGrid.SelectedItem = _canRows.FirstOrDefault();
    }

    private void CanTxGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingCan || CanIdBox is null) return;
        if (e.RemovedItems.OfType<QuickBenchCanRow>().FirstOrDefault() is { } prior)
        {
            try { ReadCanEditor(prior); }
            catch (Exception ex) { Log("原报文编辑未完成：" + ex.Message); }
        }
        PopulateCanEditor(CanTxGrid.SelectedItem as QuickBenchCanRow);
    }

    private void PopulateCanEditor(QuickBenchCanRow? row)
    {
        _loadingCan = true;
        try
        {
            CanIdBox.Text = row?.IdText ?? "";
            CanDlcBox.Text = row?.DlcText ?? "";
            CanPeriodBox.Text = row?.PeriodText ?? "";
            CanDataBox.Text = row?.DataText ?? "";
            CanIdeCheck.IsChecked = row?.Extended ?? false;
            CanPeriodicCheck.IsChecked = row?.Periodic ?? false;
            CanUseDbcCheck.IsChecked = row?.UseDbc ?? false;
            var match = row is null ? null : _dbcChoices.FirstOrDefault(x =>
                DbcIdentity(x.Message) == row.DbcDefinitionId);
            CanDbcCombo.SelectedItem = match;
            RebuildSignalEditor(row, match?.Message);
            CanSignalsGrid.IsEnabled = row?.UseDbc == true;
            CanDbcCombo.IsEnabled = row?.UseDbc == true;
            CanDataBox.IsReadOnly = row?.UseDbc == true;
        }
        finally { _loadingCan = false; }
    }

    private void RebuildSignalEditor(QuickBenchCanRow? row, CanMessageDefinition? message)
    {
        _signalValues.Clear();
        if (row is null || message is null) return;
        foreach (var signal in message.Signals)
            _signalValues.Add(new QuickBenchSignalValue(signal,
                row.SignalValues.GetValueOrDefault(signal.Name, "0")));
    }

    private void ReadCanEditor(QuickBenchCanRow row)
    {
        if (_loadingCan) return;
        row.IdText = CanIdBox.Text.Trim();
        row.DlcText = CanDlcBox.Text.Trim();
        row.PeriodText = CanPeriodBox.Text.Trim();
        row.Extended = CanIdeCheck.IsChecked == true;
        row.Periodic = CanPeriodicCheck.IsChecked == true;
        row.UseDbc = CanUseDbcCheck.IsChecked == true;
        if (row.UseDbc && CanDbcCombo.SelectedItem is QuickBenchDbcChoice choice)
        {
            row.DbcDefinitionId = DbcIdentity(choice.Message);
            row.SignalValues = _signalValues.ToDictionary(x => x.Signal.Name, x => x.Value, StringComparer.Ordinal);
            row.DataText = CanPayloadCodec.Encode(choice.Message, row.SignalValues);
            row.IdText = "0x" + choice.Message.Id.ToString("X", CultureInfo.InvariantCulture);
            row.DlcText = choice.Message.Dlc.ToString(CultureInfo.InvariantCulture);
            row.Extended = choice.Message.Extended;
        }
        else row.DataText = CanDataBox.Text.Trim();
    }

    private void SaveCanEditor()
    {
        if (CanTxGrid.SelectedItem is QuickBenchCanRow row) ReadCanEditor(row);
    }
    private void SaveCan_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveCanEditor();
            if (CanTxGrid.SelectedItem is QuickBenchCanRow row) _ = PrepareCanRow(row, false);
            PersistCanRows();
        }
        catch (Exception ex) { Log("CAN 编辑错误：" + ex.Message); }
    }

    private void CanUseDbc_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingCan || CanTxGrid?.SelectedItem is not QuickBenchCanRow row) return;
        row.UseDbc = CanUseDbcCheck.IsChecked == true;
        CanDbcCombo.IsEnabled = row.UseDbc;
        CanSignalsGrid.IsEnabled = row.UseDbc;
        CanDataBox.IsReadOnly = row.UseDbc;
        if (row.UseDbc && CanDbcCombo.SelectedItem is QuickBenchDbcChoice choice)
            RebuildSignalEditor(row, choice.Message);
    }

    private void CanDbcCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingCan || CanTxGrid?.SelectedItem is not QuickBenchCanRow row) return;
        if (CanDbcCombo.SelectedItem is not QuickBenchDbcChoice selected) return;
        // Selecting another DBC message only clears stale signal values, never period/timeout.
        row.DbcDefinitionId = DbcIdentity(selected.Message);
        row.SignalValues.Clear();
        RebuildSignalEditor(row, selected.Message);
        row.IdText = "0x" + selected.Message.Id.ToString("X", CultureInfo.InvariantCulture);
        row.DlcText = selected.Message.Dlc.ToString(CultureInfo.InvariantCulture);
        row.Extended = selected.Message.Extended;
        CanIdBox.Text = row.IdText;
        CanDlcBox.Text = row.DlcText;
        CanIdeCheck.IsChecked = row.Extended;
    }

    private sealed record PreparedTx(QuickBenchCanRow Row, bool Extended, uint Id, byte Dlc, byte[] Data, ushort Period);

    private PreparedTx PrepareCanRow(QuickBenchCanRow row, bool requirePeriod)
    {
        var text = row.IdText.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
        if (!uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id))
            throw new InvalidDataException(row.Name + ": CAN ID 不是有效十六进制数。");
        if (id > (row.Extended ? 0x1FFFFFFFu : 0x7FFu))
            throw new InvalidDataException(row.Name + ": CAN ID 超出标准帧/扩展帧范围。");
        if (!byte.TryParse(row.DlcText, NumberStyles.None, CultureInfo.InvariantCulture, out var dlc) || dlc > 8)
            throw new InvalidDataException(row.Name + ": 经典 CAN DLC 必须为 0～8。");
        var raw = row.DataText.Trim();
        if (row.UseDbc)
        {
            var dbc = _dbcChoices.FirstOrDefault(x => DbcIdentity(x.Message) == row.DbcDefinitionId);
            if (dbc is null) throw new InvalidDataException(row.Name + ": DBC 报文引用失效，请重新选择。");
            if (dbc.Message.Dlc != dlc || dbc.Message.Id != id || dbc.Message.Extended != row.Extended)
                throw new InvalidDataException(row.Name + ": CAN ID/DLC/IDE 与 DBC 不一致，请重新选择。");
            foreach (var signal in dbc.Message.Signals)
                if (row.SignalValues.TryGetValue(signal.Name, out var v) &&
                    (!double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric) ||
                     !double.IsFinite(numeric)))
                    throw new InvalidDataException(row.Name + ": 信号 " + signal.Name + " 不是有效物理值。");
            raw = CanPayloadCodec.Encode(dbc.Message, row.SignalValues);
        }
        byte[] data;
        if (raw.Length == 0) data = [];
        else
        {
            var cleaned = raw.Replace("0x", "", StringComparison.OrdinalIgnoreCase)
                .Replace(" ", "").Replace("-", "").Replace(",", "").Replace(";", "");
            if (cleaned.Length % 2 != 0 || cleaned.Any(c => !Uri.IsHexDigit(c)))
                throw new InvalidDataException(row.Name + ": HEX 数据格式错误，应为两位一字节。");
            data = Convert.FromHexString(cleaned);
        }
        if (data.Length != dlc)
            throw new InvalidDataException(row.Name + ": 数据长度 " + data.Length + " 不等于 DLC " + dlc + "。");
        ushort period = 0;
        if (requirePeriod && (!ushort.TryParse(row.PeriodText, NumberStyles.None, CultureInfo.InvariantCulture, out period) ||
                              period == 0))
            throw new InvalidDataException(row.Name + ": 周期必须为 1～65535 ms。");
        return new PreparedTx(row, row.Extended, id, dlc, data, period);
    }

    private async Task SendCanAsync(PreparedTx item)
    {
        var sent = await CommandAsync(Frame.CMD_CAN_SEND,
            Frame.CanSendPayload(1, item.Extended, item.Id, item.Dlc, item.Data),
            "CAN1 单发 0x" + item.Id.ToString("X", CultureInfo.InvariantCulture));
        item.Row.Status = sent.ok ? "已接受" : "发送失败";
    }

    private async void SendSelectedCan_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveCanEditor();
            if (CanTxGrid.SelectedItem is not QuickBenchCanRow row) { Log("请先选择一条报文。"); return; }
            var prepared = PrepareCanRow(row, false);
            await RunPhysicalAsync("CAN1 单发", () => SendCanAsync(prepared));
        }
        catch (Exception ex) { Log("CAN1 单发预检失败：" + ex.Message); }
    }

    private async void SendCheckedCan_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveCanEditor();
            var rows = _canRows.Where(x => x.Included).ToArray();
            if (rows.Length == 0) { Log("没有勾选 CAN1 报文。"); return; }
            var plan = rows.Select(row => PrepareCanRow(row, false)).ToArray();
            await RunPhysicalAsync("CAN1 勾选单发", async () =>
            {
                foreach (var item in plan) await SendCanAsync(item);
            });
        }
        catch (Exception ex) { Log("CAN1 批量单发预检失败：" + ex.Message); }
    }

    private async void StartPeriodic_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveCanEditor();
            var rows = _canRows.Where(x => x.Included && x.Periodic).ToArray();
            if (rows.Length is < 1 or > 8)
                throw new InvalidDataException("请勾选 1～8 条周期 CAN1 报文（主控 0x50 最多 8 条）。");
            var plan = rows.Select(row => PrepareCanRow(row, true)).ToArray();
            await RunPhysicalAsync("CAN1 主控周期启动", async () =>
            {
                if (_periodicOwned || _periodicUncertain)
                {
                    Log("现有广播活动或状态未知，请先显式停止并确认，不会自动覆盖。");
                    return;
                }
                var status = await _usb.SendAndWaitAsync(Frame.CMD_BROADCAST_STATUS, [], 2000);
                if (!status.ok || status.status != Frame.STS_OK || status.payload.Length < 2 ||
                    status.payload[0] != 0)
                {
                    _periodicUncertain = true;
                    Log("主控周期广播当前运行或状态未知；为防止覆盖现有 0x50 整表任务，拒绝启动。");
                    return;
                }
                var set = await CommandAsync(Frame.CMD_SET_BROADCAST,
                    Frame.SetBroadcastPayload(plan.Select(p =>
                        (ide: p.Extended ? (byte)1 : (byte)0, id: p.Id, periodMs: p.Period, dlc: p.Dlc, data: p.Data)).ToArray()),
                    "主控 0x50 装载完整广播表");
                if (!set.ok) return;
                var start = await CommandAsync(Frame.CMD_BROADCAST_START, [], "主控 0x51 启动周期");
                if (!start.ok) { _periodicUncertain = true; return; }
                _periodicOwned = true;
                var confirm = await _usb.SendAndWaitAsync(Frame.CMD_BROADCAST_STATUS, [], 2000);
                if (!confirm.ok || confirm.status != Frame.STS_OK || confirm.payload.Length < 2 ||
                    confirm.payload[0] != 1 || confirm.payload[1] != plan.Length)
                {
                    _periodicUncertain = true;
                    Log("广播已请求启动，但主控状态核验不一致；按状态未知处理。");
                    return;
                }
                _periodicUncertain = false;
                foreach (var item in plan) item.Row.Status = "周期中";
                Log("主控周期广播已确认：整表 " + plan.Length + " 条；不使用 PC 定时线程。");
            });
        }
        catch (Exception ex) { Log("周期广播预检失败：" + ex.Message); }
    }

    private async void StopPeriodic_Click(object sender, RoutedEventArgs e)
    {
        if (_periodicUncertain && !_periodicOwned &&
            MessageBox.Show(this,
                "这项主控周期广播不是当前手动台架确认启动的，或设备发生过断连。\n" +
                "发送停止命令会停止主控上整个 CAN1 周期广播任务。确定要执行吗？",
                "确认停止未知广播", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await RunPhysicalAsync("CAN1 主控周期停止", async () =>
        {
            var stop = await CommandAsync(Frame.CMD_BROADCAST_STOP, [], "主控 0x52 停止全部周期广播");
            if (!stop.ok) { _periodicUncertain = true; return; }
            var state = await _usb.SendAndWaitAsync(Frame.CMD_BROADCAST_STATUS, [], 2000);
            if (!state.ok || state.status != Frame.STS_OK || state.payload.Length < 2 || state.payload[0] != 0)
            {
                _periodicUncertain = true;
                Log("STOP 已请求，但未确认广播停止；保持 UNKNOWN 状态。");
                return;
            }
            _periodicOwned = false;
            _periodicUncertain = false;
            _lostOwnedPeriodic = false;
            foreach (var row in _canRows.Where(x => x.Periodic)) row.Status = "已停止";
        });
    }

    private void Log(string message)
    {
        if (_closed || Dispatcher.HasShutdownStarted) return;
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => Log(message))); return; }
        var line = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + "  " + message;
        _benchLogs.Add(line);
        if (_benchLogs.Count > 5000) _benchLogs.RemoveAt(0);
        BenchLogList.ScrollIntoView(line);
        _mainLog("手动台架：" + message);
    }
}
