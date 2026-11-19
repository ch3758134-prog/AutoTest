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
        NewServiceCombo.ItemsSource = UdsOperationTemplates.All;
        ServiceCombo.ItemsSource = UdsOperationTemplates.All;
        NewServiceCombo.SelectedIndex = 0;
        QueueList.ItemsSource = _manualQueue;
    }

    private void LoadServiceEditor(DiagnosticAction? action)
    {
        if (action is null)
        {
            ServiceCombo.SelectedItem = null;
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
            PrimaryLabel.Text = template.PrimaryLabel;
            AuxiliaryLabel.Text = template.AuxiliaryLabel;
            ServiceHelp.Text = template.Guidance;
            ComposerStatus.Text = template.PhysicalEnabled
                ? "已选择标准 UDS 模板；发送仍需真实设备授权。"
                : "可配置并组帧；此服务尚未获准在真实 ECU 上发送。";
        }
        else
        {
            PrimaryBox.Text = AuxiliaryBox.Text = "";
            PrimaryLabel.Text = "服务主参数";
            AuxiliaryLabel.Text = "附加数据";
            ServiceHelp.Text = "自由请求：直接编辑 UDS HEX；不要求先创建标准服务模板。";
            ComposerStatus.Text = "当前 PDU 将由 UDS 风险目录检查，未知服务不会实体发送。";
        }
        bool rawMode = action.RawMode || template is null;
        RawModeCheck.IsChecked = rawMode;
        RequestBox.IsReadOnly = !rawMode;
        PrimaryBox.IsEnabled = AuxiliaryBox.IsEnabled = template is not null && !rawMode;
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
            PrimaryLabel.Text = template.PrimaryLabel;
            AuxiliaryLabel.Text = template.AuxiliaryLabel;
            ServiceHelp.Text = template.Guidance;
            RawModeCheck.IsChecked = false;
            RequestBox.IsReadOnly = true;
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
                if (template.Sid == 0x22)
                {
                    bool vin = composed.Decoder == "Ascii";
                    // Use raw HEX for newly selected non-VIN DID; keep custom
                    // decoder choices for explicit manual edits after composing.
                    if (vin && _editing.Decoder == "Hex")
                        DecodeBox.SelectedItem = DecodeBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == "Ascii");
                    else if (!vin && _editing.Decoder == "Ascii")
                        DecodeBox.SelectedItem = DecodeBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == "Hex");
                }
                OffsetBox.Text = composed.DecoderOffset.ToString(CultureInfo.InvariantCulture);
            }
            finally { _loadingEditor = false; }
            UpdateActionFromEditor();
            ComposerStatus.Text = template.PhysicalEnabled
                ? "请求已自动生成 · 当前服务属于受限只读候选；按下发送仍需逐帧实体授权。"
                : "请求已自动生成 · 实体发送暂未开放；可先编辑参数与响应解析规则。";
            ComposerStatus.Foreground = template.PhysicalEnabled ? Brushes.SeaGreen : Brushes.DarkOrange;
        }
        catch (FormatException ex)
        {
            _loadingEditor = true;
            try { RequestBox.Text = ""; ExpectedBox.Text = ""; }
            finally { _loadingEditor = false; }
            UpdateActionFromEditor();
            ComposerStatus.Text = "参数待补充：" + ex.Message;
            ComposerStatus.Foreground = Brushes.DarkOrange;
        }
    }

    private void CreateFromService_Click(object sender, RoutedEventArgs e)
    {
        if (NewServiceCombo.SelectedItem is not UdsOperationTemplate template) return;
        var action = new DiagnosticAction
        {
            Name = template.Name,
            Group = template.Group,
            ServiceSidHex = template.Sid.ToString("X2"),
            PrimaryHex = template.PrimaryDefault,
            AuxiliaryHex = template.AuxiliaryDefault,
            RawMode = false,
            DataOffset = 3
        };
        _actions.Add(action);
        ActionsList.SelectedItem = action;
        ComposeCurrent();
        Audit("EDIT", "从内置服务创建诊断操作：" + template.Display);
    }

    private void QueueAdd_Click(object sender, RoutedEventArgs e)
    {
        if (_editing is null) return;
        _manualQueue.Add(_editing);
        Audit("QUEUE", "加入手动连续交互清单：" + _editing.Name);
    }

    private void QueueRemove_Click(object sender, RoutedEventArgs e)
    {
        if (QueueList.SelectedItem is DiagnosticAction action)
            _manualQueue.Remove(action);
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
