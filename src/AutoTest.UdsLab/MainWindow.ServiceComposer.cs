using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AutoTest.Specialists.Uds.Workbench;

namespace AutoTest.UdsLab;

public partial class MainWindow
{
    private readonly ObservableCollection<DiagnosticAction> _manualQueue = [];

    private void InitializeServiceComposer()
    {
        ServiceCombo.ItemsSource = UdsOperationTemplates.All;
        QueueList.ItemsSource = _manualQueue;
        WatchIntervalBox.Text = "2000";
    }

    private void LoadServiceEditor(DiagnosticAction? action)
    {
        if (action is null)
        {
            ServiceCombo.SelectedItem = null;
            BindSubfunctionPresets(null);
            PrimaryBox.Text = AuxiliaryBox.Text = "";
            ServiceHelp.Text = "先创建或选择一个诊断操作。";
            ComposerStatus.Text = "";
            RawModeCheck.IsChecked = true;
            RequestBox.IsReadOnly = false;
            return;
        }

        UdsOperationTemplate? template = null;
        if (byte.TryParse(action.ServiceSidHex, NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out byte sid))
            template = UdsOperationTemplates.Find(sid);

        byte[] raw = [];
        if (!string.IsNullOrWhiteSpace(action.RequestHex))
        {
            try { raw = UdsHex.Parse(action.RequestHex); }
            catch (FormatException) { }
        }
        if (template is null && raw.Length > 0)
            template = UdsOperationTemplates.Find(raw[0]);

        ServiceCombo.SelectedItem = template;
        if (template is not null)
        {
            var split = raw.Length > 0
                ? UdsOperationTemplates.SplitRequest(template, raw)
                : (template.PrimaryDefault, template.AuxiliaryDefault);
            PrimaryBox.Text = !string.IsNullOrWhiteSpace(action.PrimaryHex) ? action.PrimaryHex : split.Item1;
            AuxiliaryBox.Text = !string.IsNullOrWhiteSpace(action.AuxiliaryHex) ? action.AuxiliaryHex : split.Item2;
            BindSubfunctionPresets(template, PrimaryBox.Text);
            PrimaryLabel.Text = template.PrimaryLabel;
            AuxiliaryLabel.Text = template.AuxiliaryLabel;
            ServiceHelp.Text = template.Guidance;
            ComposerStatus.Text = template.PhysicalEnabled
                ? "已选择标准 UDS 模板；发送仍需真实设备授权。"
                : "可配置并组帧；此服务尚未获准在真实 ECU 上发送。";
        }
        else
        {
            BindSubfunctionPresets(null);
            PrimaryBox.Text = AuxiliaryBox.Text = "";
            PrimaryLabel.Text = "服务主参数";
            AuxiliaryLabel.Text = "附加数据";
            ServiceHelp.Text = "自由请求：直接编辑 UDS HEX；不要求先创建标准服务模板。";
            ComposerStatus.Text = "当前 PDU 将由 UDS 风险目录检查，未知服务不会实体发送。";
        }
        bool rawMode = action.RawMode || template is null;
        RawModeCheck.IsChecked = rawMode;
        RequestBox.IsReadOnly = !rawMode;
        // Response expectations and decoding remain visible while tuning requests.
        AdvancedEditExpander.IsExpanded = true;
        PrimaryBox.IsEnabled = AuxiliaryBox.IsEnabled = template is not null && !rawMode;
        RefreshSendButton();
    }

    private void SaveServiceEditorState()
    {
        if (_editing is null) return;
        var template = ServiceCombo.SelectedItem as UdsOperationTemplate;
        _editing.ServiceSidHex = template is null ? "" : template.Sid.ToString("X2");
        _editing.PrimaryHex = PrimaryBox.Text;
        _editing.AuxiliaryHex = AuxiliaryBox.Text;
        _editing.RawMode = RawModeCheck.IsChecked == true;
    }

    private void BindSubfunctionPresets(UdsOperationTemplate? template, string? primary = null)
    {
        var choices = template is null
            ? Array.Empty<UdsSubfunctionPreset>()
            : UdsOperationTemplates.PresetsFor(template.Sid).ToArray();
        SubfunctionPresetCombo.ItemsSource = choices;
        SubfunctionPresetCombo.Visibility = choices.Length == 0
            ? Visibility.Collapsed : Visibility.Visible;
        NoSubfunctionHint.Visibility = choices.Length == 0
            ? Visibility.Visible : Visibility.Collapsed;
        SubfunctionPresetCombo.SelectedItem = choices.FirstOrDefault(p =>
            string.Equals(p.PrimaryHex, primary, StringComparison.OrdinalIgnoreCase));
    }

    private void SubfunctionPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingEditor || _editing is null ||
            SubfunctionPresetCombo.SelectedItem is not UdsSubfunctionPreset choice)
            return;
        _loadingEditor = true;
        try
        {
            PrimaryBox.Text = choice.PrimaryHex;
            // Routine ID / Seed-Key parameters are user-specified. Switching
            // between subfunctions must not invent or erase OEM data.
            if (choice.Sid is 0x19 or 0x10 or 0x11 or 0x3E)
                AuxiliaryBox.Text = choice.AuxiliaryHex;
        }
        finally { _loadingEditor = false; }
        ComposeCurrent();
    }

    private void ServiceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingEditor || _editing is null) return;
        var template = ServiceCombo.SelectedItem as UdsOperationTemplate;
        if (template is null) return;
        _loadingEditor = true;
        try
        {
            PrimaryBox.Text = template.PrimaryDefault;
            AuxiliaryBox.Text = template.AuxiliaryDefault;
            BindSubfunctionPresets(template, PrimaryBox.Text);
            PrimaryLabel.Text = template.PrimaryLabel;
            AuxiliaryLabel.Text = template.AuxiliaryLabel;
            ServiceHelp.Text = template.Guidance;
            RawModeCheck.IsChecked = false;
            RequestBox.IsReadOnly = true;
            AdvancedEditExpander.IsExpanded = true;
            PrimaryBox.IsEnabled = AuxiliaryBox.IsEnabled = true;
        }
        finally { _loadingEditor = false; }
        ComposeCurrent();
    }

    private void ServiceParameterChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loadingEditor && RawModeCheck.IsChecked != true)
            ComposeCurrent();
    }

    private void RawModeChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingEditor) return;
        bool rawMode = RawModeCheck.IsChecked == true;
        if (!rawMode && ServiceCombo.SelectedItem is null)
        {
            RawModeCheck.IsChecked = true;
            return;
        }
        RequestBox.IsReadOnly = !rawMode;
        PrimaryBox.IsEnabled = AuxiliaryBox.IsEnabled = !rawMode;
        if (rawMode)
        {
            ComposerStatus.Text = "自由 HEX 编辑已启用；改动原始 PDU 不会被服务模板自动覆盖。";
            ComposerStatus.Foreground = Brushes.SteelBlue;
            SaveServiceEditorState();
            AdvancedEditExpander.IsExpanded = true;
            RefreshSendButton();
        }
        else
            ComposeCurrent();
    }

    private void ComposeCurrent()
    {
        if (_loadingEditor || _editing is null || RawModeCheck.IsChecked == true) return;
        if (ServiceCombo.SelectedItem is not UdsOperationTemplate template) return;
        SaveServiceEditorState();
        try
        {
            var composed = UdsOperationTemplates.Compose(template, PrimaryBox.Text, AuxiliaryBox.Text);
            _loadingEditor = true;
            try
            {
                RequestBox.Text = composed.RequestHex;
                ExpectedBox.Text = composed.ExpectedPrefixHex;
                // Select a useful default decoder for the service. Preserve
                // explicitly configured numeric decoders when parameters change.
                if (_editing.Decoder is "Hex" or "Ascii" or "Dtc")
                    DecodeBox.SelectedItem = DecodeBox.Items.OfType<ComboBoxItem>()
                        .FirstOrDefault(i => (string?)i.Tag == composed.Decoder);
                OffsetBox.Text = composed.DecoderOffset.ToString(CultureInfo.InvariantCulture);
            }
            finally { _loadingEditor = false; }
            UpdateActionFromEditor();
            ComposerStatus.Text = template.PhysicalEnabled
                ? "请求已自动生成 · 当前服务属于受限只读候选；按下发送仍需逐帧实体授权。"
                : "请求已自动生成 · 实体发送暂未开放；可先编辑参数与响应解析规则。";
            ComposerStatus.Foreground = template.PhysicalEnabled ? Brushes.SeaGreen : Brushes.DarkOrange;
            RefreshSendButton();
        }
        catch (FormatException ex)
        {
            _loadingEditor = true;
            try { RequestBox.Text = ""; ExpectedBox.Text = ""; }
            finally { _loadingEditor = false; }
            UpdateActionFromEditor();
            ComposerStatus.Text = "参数待补充：" + ex.Message;
            ComposerStatus.Foreground = Brushes.DarkOrange;
            RefreshSendButton();
        }
    }

    private void QueueAdd_Click(object sender, RoutedEventArgs e)
    {
        if (_editing is null) return;
        // A manual sequence can only reference persisted actions. When a quick
        // request is added, first promote it to a named action so the saved
        // workspace never contains dangling sequence IDs.
        if (!_actions.Contains(_editing))
        {
            SaveCurrent_Click(sender, e);
            if (_editing is null || !_actions.Contains(_editing)) return;
        }
        if (_manualQueue.Count >= 100)
        {
            MessageBox.Show(this, "交互清单最多保存 100 项。");
            return;
        }
        _manualQueue.Add(_editing);
        RefreshSendButton();
        Audit("QUEUE", "加入手动连续交互清单：" + _editing.Name);
    }

    private void QueueRemove_Click(object sender, RoutedEventArgs e)
    {
        if (QueueList.SelectedItem is DiagnosticAction action)
        {
            int index = QueueList.SelectedIndex;
            _manualQueue.RemoveAt(index);
            RefreshSendButton();
            Audit("QUEUE", "从清单移除一项：" + action.Name);
        }
    }

    // This is presentation/preflight only. The shared UdsClient and the USB
    // PhysicalCommandFence remain the final security authorities.
    private void RefreshSendButton()
    {
        bool maySend = false;
        if (_editing is not null && _hardware?.IsReady == true &&
            !_requiresReconnect && _operation is null && _pendingRequest is null)
        {
            try
            {
                byte[] pdu = UdsHex.Parse(_editing.RequestHex);
                maySend = pdu.Length > 0 && pdu[0] is 0x19 or 0x22 or 0x23 or 0x24;
            }
            catch (FormatException) { }
        }
        SendButton.IsEnabled = maySend;
        RefreshSequenceButtons();
    }

    private void QueueNext_Click(object sender, RoutedEventArgs e)
    {
        if (_manualQueue.Count == 0) return;
        if (_operation is not null || _pendingRequest is not null)
        {
            MessageBox.Show(this, "当前请求尚未结束，不能叠加多个 UDS 请求。");
            return;
        }
        if (_hardware?.IsReady != true || _requiresReconnect)
        {
            MessageBox.Show(this, "请先建立有效的真实 ECU 诊断通路；中断后需要重新连接。");
            return;
        }
        var next = _manualQueue[0];
        _manualQueue.RemoveAt(0);
        _manualQueue.Add(next); // preserve actions for later replay; queue is cyclic
        ActionsList.SelectedItem = next;
        Audit("QUEUE", "执行下一项：" + next.Name);
        Send_Click(sender, e);
    }
}
