using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace EPB_UsbTest;

public partial class QuickManualBenchWindow
{
    private readonly ObservableCollection<QuickBenchKeyChoice> _keys =
        new(Enumerable.Range(1, 20).Select(x => new QuickBenchKeyChoice(x)));
    private readonly ObservableCollection<QuickBenchKeyAction> _actions = new();
    private bool _keyUiSync;
    private bool _keyRunning;
    private uint? _lastAppliedKeyMask;
    private CancellationTokenSource? _keyStop;

    private void InitializeKeyEditor()
    {
        KeySwitches.ItemsSource = _keys;
        KeyActionList.ItemsSource = _actions;
    }

    private void RestoreKeyActions()
    {
        try
        {
            var path = StoragePath("key");
            if (File.Exists(path))
            {
                var items = JsonSerializer.Deserialize<List<QuickBenchActionData>>(File.ReadAllText(path));
                if (items is not null)
                {
                    foreach (var action in items.Take(100))
                    {
                        var editor = new QuickBenchKeyAction { Name = action.Name };
                        foreach (var step in (action.Steps ?? new List<QuickBenchStepData>()).Take(250))
                        {
                            if (step.Mask <= 0xFFFFF && step.HoldMs is >= 0 and <= 3_600_000)
                                editor.Steps.Add(new QuickBenchKeyStep(step.Mask, step.HoldMs));
                        }
                        _actions.Add(editor);
                    }
                }
            }
        }
        catch (Exception ex) { Log("读取 QUICK 按键动作失败：" + ex.Message); }
        if (_actions.Count == 0)
            _actions.Add(new QuickBenchKeyAction { Name = "自定义动作 1" });
        KeyActionList.SelectedIndex = 0;
    }

    private void PersistKeyActions()
    {
        try
        {
            CommitKeyEditor();
            var payload = _actions.Select(action =>
                new QuickBenchActionData(action.Name,
                    action.Steps.Select(step => new QuickBenchStepData(step.Mask, step.HoldMs)).ToList()))
                .ToList();
            File.WriteAllText(StoragePath("key"),
                JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
            Log("KEY 动作已保存到本 QUICK 配置的本地操作表；不会自动施加到实体板。");
        }
        catch (Exception ex) { Log("保存 KEY 动作失败：" + ex.Message); }
    }

    private void KeyActionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (KeyActionList.SelectedItem is QuickBenchKeyAction action)
        {
            KeyStepsList.ItemsSource = action.Steps;
            KeyStepsList.SelectedIndex = action.Steps.Count == 0 ? -1 : 0;
            if (KeyActionNameBox is not null)
                KeyActionNameBox.Text = action.Name;
        }
        else
        {
            KeyStepsList.ItemsSource = null;
            if (KeyActionNameBox is not null) KeyActionNameBox.Text = "";
        }
    }

    private void KeyActionName_Changed(object sender, TextChangedEventArgs e)
    {
        if (_keyUiSync || KeyActionList.SelectedItem is not QuickBenchKeyAction action) return;
        action.Name = KeyActionNameBox.Text;
    }

    private void NewKeyAction_Click(object sender, RoutedEventArgs e)
    {
        if (_keyRunning) { Log("运行 KEY 动作时不能修改动作表。"); return; }
        var action = new QuickBenchKeyAction { Name = "自定义动作 " + (_actions.Count + 1) };
        _actions.Add(action);
        KeyActionList.SelectedItem = action;
    }

    private void CopyKeyAction_Click(object sender, RoutedEventArgs e)
    {
        if (_keyRunning || KeyActionList.SelectedItem is not QuickBenchKeyAction current) return;
        var copy = new QuickBenchKeyAction { Name = current.Name + " 副本" };
        foreach (var step in current.Steps) copy.Steps.Add(step.Clone());
        _actions.Insert(_actions.IndexOf(current) + 1, copy);
        KeyActionList.SelectedItem = copy;
    }

    private void DeleteKeyAction_Click(object sender, RoutedEventArgs e)
    {
        if (_keyRunning || KeyActionList.SelectedItem is not QuickBenchKeyAction action) return;
        if (MessageBox.Show(this, "删除动作“" + action.Name + "”？不会操作真实 KEY。",
                "删除动作", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _actions.Remove(action);
        KeyActionList.SelectedItem = _actions.FirstOrDefault();
    }

    private void NewKeyStep_Click(object sender, RoutedEventArgs e)
    {
        if (_keyRunning || KeyActionList.SelectedItem is not QuickBenchKeyAction action) return;
        var step = new QuickBenchKeyStep(0, 1000);
        action.Steps.Add(step);
        KeyStepsList.SelectedItem = step;
    }

    private void CopyKeyStep_Click(object sender, RoutedEventArgs e)
    {
        if (_keyRunning || KeyActionList.SelectedItem is not QuickBenchKeyAction action ||
            KeyStepsList.SelectedItem is not QuickBenchKeyStep original) return;
        var index = action.Steps.IndexOf(original);
        var copy = original.Clone();
        action.Steps.Insert(index + 1, copy);
        KeyStepsList.SelectedItem = copy;
    }

    private void DeleteKeyStep_Click(object sender, RoutedEventArgs e)
    {
        if (_keyRunning || KeyActionList.SelectedItem is not QuickBenchKeyAction action ||
            KeyStepsList.SelectedItem is not QuickBenchKeyStep step) return;
        var index = action.Steps.IndexOf(step);
        action.Steps.Remove(step);
        KeyStepsList.SelectedItem = action.Steps.Count == 0 ? null :
            action.Steps[Math.Min(index, action.Steps.Count - 1)];
    }

    private void MoveKeyStepUp_Click(object sender, RoutedEventArgs e) => MoveKeyStep(-1);
    private void MoveKeyStepDown_Click(object sender, RoutedEventArgs e) => MoveKeyStep(1);
    private void MoveKeyStep(int offset)
    {
        if (_keyRunning || KeyActionList.SelectedItem is not QuickBenchKeyAction action ||
            KeyStepsList.SelectedItem is not QuickBenchKeyStep step) return;
        var from = action.Steps.IndexOf(step);
        var to = from + offset;
        if (from < 0 || to < 0 || to >= action.Steps.Count) return;
        action.Steps.Move(from, to);
        KeyStepsList.SelectedItem = step;
    }

    private void KeyStepsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _keyUiSync = true;
        try
        {
            var step = KeyStepsList.SelectedItem as QuickBenchKeyStep;
            KeyMaskBox.Text = step is null ? "" :
                "0x" + step.Mask.ToString("X5", CultureInfo.InvariantCulture);
            KeyHoldBox.Text = step?.HoldMs.ToString(CultureInfo.InvariantCulture) ?? "";
            foreach (var key in _keys) key.Checked = step is not null &&
                (step.Mask & (1u << (key.Number - 1))) != 0;
            KeyMaskInfo.Foreground = Brushes.SlateGray;
            KeyMaskInfo.Text = step is null
                ? "新建 KEY 步骤后配置完整 20-bit Mask 和保持时间。当前硬件状态 UNKNOWN。"
                : MaskText(step.Mask) + "；仅表示编辑值，不等于设备当前状态。";
        }
        finally { _keyUiSync = false; }
    }

    private static bool TryMask(string input, out uint mask)
    {
        var value = input.Trim();
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) value = value[2..];
        return uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out mask)
               && mask <= 0xFFFFFu;
    }

    private static string MaskText(uint mask)
    {
        var names = Enumerable.Range(1, 20).Where(n => (mask & (1u << (n - 1))) != 0)
            .Select(n => "K" + n.ToString("00", CultureInfo.InvariantCulture));
        return "0x" + mask.ToString("X5", CultureInfo.InvariantCulture) +
               " · " + (mask == 0 ? "全部断开" : string.Join(" + ", names));
    }

    private void KeyMask_Changed(object sender, TextChangedEventArgs e)
    {
        if (_keyUiSync || KeyStepsList.SelectedItem is not QuickBenchKeyStep step) return;
        if (!TryMask(KeyMaskBox.Text, out var mask))
        {
            KeyMaskInfo.Text = "掩码格式错误：必须为 0x00000～0xFFFFF（HEX），未修改原步骤。";
            KeyMaskInfo.Foreground = Brushes.Firebrick;
            return;
        }
        step.Mask = mask;
        _keyUiSync = true;
        try
        {
            foreach (var key in _keys)
                key.Checked = (mask & (1u << (key.Number - 1))) != 0;
        }
        finally { _keyUiSync = false; }
        KeyMaskInfo.Foreground = Brushes.SlateGray;
        KeyMaskInfo.Text = MaskText(mask) + "；当前仅修改编辑值，未发送到 CAN0。";
    }

    private void KeyHold_Changed(object sender, TextChangedEventArgs e)
    {
        if (_keyUiSync || KeyStepsList.SelectedItem is not QuickBenchKeyStep step) return;
        if (!int.TryParse(KeyHoldBox.Text.Trim(), NumberStyles.None,
                CultureInfo.InvariantCulture, out var duration) ||
            duration is < 0 or > 3_600_000)
        {
            KeyMaskInfo.Text = "保持时间必须是 0～3600000 ms；无效输入未保存到步骤。";
            KeyMaskInfo.Foreground = Brushes.Firebrick;
            return;
        }
        step.HoldMs = duration;
        KeyMaskInfo.Foreground = Brushes.SlateGray;
    }

    private void KeySwitch_Changed(object sender, RoutedEventArgs e)
    {
        if (_keyUiSync || KeyStepsList.SelectedItem is not QuickBenchKeyStep step) return;
        uint mask = 0;
        foreach (var key in _keys)
            if (key.Checked) mask |= 1u << (key.Number - 1);
        _keyUiSync = true;
        try { KeyMaskBox.Text = "0x" + mask.ToString("X5", CultureInfo.InvariantCulture); }
        finally { _keyUiSync = false; }
        step.Mask = mask;
        KeyMaskInfo.Foreground = Brushes.SlateGray;
        KeyMaskInfo.Text = MaskText(mask) + "；当前未发送，实际 KEY 状态未改变。";
    }

    private void ClearKeys_Click(object sender, RoutedEventArgs e)
    {
        if (_keyRunning || KeyStepsList.SelectedItem is not QuickBenchKeyStep step) return;
        step.Mask = 0;
        KeyMaskBox.Text = "0x00000";
        KeyStepsList_SelectionChanged(KeyStepsList, new SelectionChangedEventArgs(
            System.Windows.Controls.Primitives.Selector.SelectionChangedEvent,
            Array.Empty<object>(), Array.Empty<object>()));
    }

    private void CommitKeyEditor()
    {
        if (KeyActionList.SelectedItem is QuickBenchKeyAction action)
            action.Name = KeyActionNameBox.Text.Trim();
        if (KeyStepsList.SelectedItem is not QuickBenchKeyStep step) return;
        if (!TryMask(KeyMaskBox.Text, out var mask))
            throw new InvalidDataException("KEY Mask 无效（必须是 20-bit HEX）。");
        if (!int.TryParse(KeyHoldBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var ms)
            || ms is < 0 or > 3_600_000)
            throw new InvalidDataException("KEY 保持时间须为 0～3600000 ms。");
        step.Mask = mask;
        step.HoldMs = ms;
    }

    private async void ApplyKeyMask_Click(object sender, RoutedEventArgs e)
    {
        if (_keyRunning) { Log("已有 KEY 动作运行，请先停止。"); return; }
        try
        {
            CommitKeyEditor();
            if (KeyStepsList.SelectedItem is not QuickBenchKeyStep step)
                throw new InvalidDataException("请先选择或新建一个 KEY 步骤。");
            var mask = step.Mask;
            await RunPhysicalAsync("CAN0 KEY Mask", async () => await SendMaskAsync(mask));
        }
        catch (Exception ex) { Log("KEY 操作预检失败：" + ex.Message); }
    }

    private async Task<bool> SendMaskAsync(uint mask)
    {
        var response = await CommandAsync(Frame.CMD_CAN_SEND,
            Frame.KeypadOutputPayload(mask), "CAN0 KEY_SIM 完整 Mask " + MaskText(mask));
        if (!response.ok) return false;
        _lastAppliedKeyMask = mask;
        KeyMaskInfo.Foreground = Brushes.DarkGreen;
        KeyMaskInfo.Text = "本次 USB 已接受： " + MaskText(mask) +
            "。尚无 KEY_SIM 独立 ACK/五路模拟电压实测，不能声称 ECU 已识别。";
        return true;
    }

    private void SaveKeyActions_Click(object sender, RoutedEventArgs e) => PersistKeyActions();

    private async void RunKeyAction_Click(object sender, RoutedEventArgs e)
    {
        if (_keyRunning) { Log("KEY 动作已在运行。"); return; }
        if (_deviceBusy) { Log("另一个 USB 硬件操作正在执行，KEY 动作暂不能开始。"); return; }
        try
        {
            CommitKeyEditor();
            if (KeyActionList.SelectedItem is not QuickBenchKeyAction action)
                throw new InvalidDataException("请先选择动作。");
            var plan = action.Steps.Select(step => (step.Mask, step.HoldMs)).ToArray();
            if (plan.Length is < 1 or > 250)
                throw new InvalidDataException("KEY 动作必须包含 1～250 段。");
            foreach (var (mask, hold) in plan)
                if (mask > 0xFFFFF || hold is < 0 or > 3_600_000)
                    throw new InvalidDataException("KEY 步骤包含无效 Mask 或保持时间。");
            if (!CanWrite("KEY 动作")) return;
            _keyStop?.Dispose();
            _keyStop = new CancellationTokenSource();
            _keyRunning = true;
            var stop = _keyStop.Token;
            KeyRunStateText.Text = "正在运行：" + action.Name;
            await RunPhysicalAsync("KEY 动作 " + action.Name, async () =>
            {
                for (var i = 0; i < plan.Length; i++)
                {
                    stop.ThrowIfCancellationRequested();
                    if (!_usb.IsOpen) throw new IOException("USB 已断开，实体 KEY 状态 UNKNOWN。");
                    var (mask, hold) = plan[i];
                    KeyRunStateText.Text = "第 " + (i + 1) + "/" + plan.Length +
                        " 段：" + MaskText(mask) + " · " + hold + " ms";
                    if (!await SendMaskAsync(mask)) throw new IOException("KEY Mask 下发没有获得主控 ACK。");
                    if (hold > 0) await Task.Delay(hold, stop);
                    // G3 requirement: hold expiry never sends ReleaseAll.
                }
                Log("KEY 动作完成；最后的完整 Mask 保持，不自动释放。");
            });
            KeyRunStateText.Text = (stop.IsCancellationRequested ? "动作已取消；" : "动作已结束；") +
                "最后发送掩码=" +
                (_lastAppliedKeyMask.HasValue ? MaskText(_lastAppliedKeyMask.Value) : "UNKNOWN") +
                "。未发送 ReleaseAll。";
        }
        catch (Exception ex)
        {
            KeyRunStateText.Text = "KEY 动作失败：" + ex.Message;
            Log(KeyRunStateText.Text);
        }
        finally
        {
            _keyRunning = false;
            _keyStop?.Dispose();
            _keyStop = null;
        }
    }

    private void StopKeyAction_Click(object sender, RoutedEventArgs e)
    {
        if (_keyStop is null || !_keyRunning)
        {
            Log("当前没有正在执行的 KEY 动作；未下发任何按键释放指令。");
            return;
        }
        _keyStop.Cancel();
        Log("请求取消 KEY 动作；不会自动释放最后的完整 Mask。");
    }
}
