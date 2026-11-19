using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace AutoTest.Desktop.Rebuild;

public static partial class UiToolBuilders
{
    public static UIElement Inputs(UiStudyStore store, Action<string> notify)
    {
        if (store.Current.InputNames.Count == 0)
        {
            // The K labels are switch positions, not ECU function mappings.
            for (int index = 1; index <= 20; index++)
                store.Current.InputNames.Add(new UiInputName { Index = index,
                    DisplayName = "K" + index.ToString("00"), Channel = "未指定" });
            store.Save();
        }
        void Save() { store.Save(); notify("输入定义已保存为 UI 草稿"); }
        var layout = UiVisual.Columns(new GridLength(540), UiVisual.Star());
        var left = UiVisual.Stack();
        left.Children.Add(UiVisual.Text("电阻网络开关定义", 17, true));
        left.Children.Add(UiVisual.Note("20 Key 为可同时闭合的内部电阻组合。不同 ECU 的按键功能、电压通道及有效区间需要单独配置，不做预设映射。"));
        var data = GridWith(store.Current.InputNames, Save,
            TextColumn("Index","内部开关编号",110),
            TextColumn("DisplayName","显示名称",170),
            TextColumn("Channel","目标通道 / 待校验",180));
        data.Height = 540;
        left.Children.Add(data);
        left.Children.Add(UiVisual.Note("编辑名称和通道仅改变 UI 草稿，不改变实际电阻网络和模拟电压。"));
        UiVisual.At(layout, UiVisual.Box(UiVisual.Scroll(left)), 0);
        var right = UiVisual.Stack();
        right.Children.Add(UiVisual.Text("外部输入动作", 17, true));
        right.Children.Add(UiVisual.Note("配置输入组合与保持时间，形成后续可供测试步骤引用的动作。"));
        var chooser = new UiKeySelector();
        chooser.Margin = new Thickness(0, 15, 0, 14);
        right.Children.Add(chooser);
        var hold = UiVisual.Input("500", 125);
        right.Children.Add(UiVisual.Field("保持时间 (ms)", hold));
        var actions = GridWith(store.Current.KeySequences, Save,
            TextColumn("Name","动作名称",150), TextColumn("MaskHex","20 Key 掩码",125),
            TextColumn("HoldMs","保持 (ms)",100));
        actions.Height = 240;
        right.Children.Add(UiVisual.Toolbar(
            UiVisual.Button("＋ 保存当前组合", () =>
            {
                if (!int.TryParse(hold.Text, out var ms) || ms < 0)
                { notify("请输入合法非负保持时间"); return; }
                store.Current.KeySequences.Add(new UiKeySequence
                { Name = "组合 " + (store.Current.KeySequences.Count + 1), MaskHex = chooser.MaskHex, HoldMs = ms });
                Save(); actions.ItemsSource = null; actions.ItemsSource = store.Current.KeySequences;
            }, true),
            UiVisual.Button("读取所选组合", () =>
            {
                if (actions.SelectedItem is UiKeySequence selected)
                {
                    chooser.SetMask(selected.MaskHex);
                    hold.Text = selected.HoldMs.ToString(CultureInfo.InvariantCulture);
                }
            }),
            UiVisual.Button("删除动作", () =>
            {
                if (actions.SelectedItem is not UiKeySequence selected) return;
                store.Current.KeySequences.Remove(selected); Save();
                actions.ItemsSource = null; actions.ItemsSource = store.Current.KeySequences;
            })
        ));
        right.Children.Add(actions);
        right.Children.Add(UiVisual.Note("输出为 5 路组合电压通道；当前仅显示设计入口，不假设任一路开关对应某个特定电压数值。"));
        UiVisual.At(layout, UiVisual.Box(UiVisual.Scroll(right)), 1);
        return layout;
    }

    public static UIElement Uds(UiStudyStore store, Action<string> notify)
    {
        void Save() { store.Save(); notify("UDS UI 请求草稿已保存，未发送诊断请求"); }
        var layout = UiVisual.Columns(new GridLength(560), UiVisual.Star());
        var left = UiVisual.Stack();
        left.Children.Add(UiVisual.Text("诊断请求编辑", 17, true));
        left.Children.Add(UiVisual.Note("支持服务字段、HEX 请求、超时、顺序编排；真实 ISO-TP 发送尚未接入。"));
        var presets = UiVisual.Combo(350);
        foreach (var name in new[]
        {
            "0x10 · DiagnosticSessionControl", "0x11 · ECUReset",
            "0x22 · ReadDataByIdentifier", "0x27 · SecurityAccess",
            "0x2E · WriteDataByIdentifier", "0x31 · RoutineControl",
            "0x34 · RequestDownload", "0x36 · TransferData", "0x37 · RequestTransferExit"
        }) presets.Items.Add(name);
        presets.SelectedIndex = 2;
        var requestName = UiVisual.Input("读取标识", 210);
        var request = UiVisual.Input("22 F1 90", 300);
        var timeout = UiVisual.Input("1000", 100);
        left.Children.Add(UiVisual.Field("服务模板", presets));
        left.Children.Add(UiVisual.Field("请求名称", requestName));
        left.Children.Add(UiVisual.Field("请求 HEX（完整负载）", request));
        left.Children.Add(UiVisual.Field("超时时间 (ms)", timeout));
        var grid = GridWith(store.Current.UdsRequests, Save,
            CheckColumn("Enabled","启用",58), TextColumn("Name","请求名称",155),
            TextColumn("Service","服务",75), TextColumn("RequestHex","请求 HEX",185),
            TextColumn("TimeoutMs","超时 ms",92), TextColumn("ExpectedResponse","期望响应",160));
        grid.Height = 330;
        void Refresh() { grid.ItemsSource = null; grid.ItemsSource = store.Current.UdsRequests; }
        left.Children.Add(UiVisual.Toolbar(
            UiVisual.Button("＋ 加入请求序列", () =>
            {
                if (!int.TryParse(timeout.Text, out var ms) || ms <= 0)
                { notify("超时时间必须大于 0"); return; }
                var parts = (presets.SelectedItem?.ToString() ?? "0x22").Split(' ');
                store.Current.UdsRequests.Add(new UiUdsRequest {
                    Name = requestName.Text, Service = parts[0],
                    RequestHex = request.Text.Trim(), TimeoutMs = ms
                });
                Save(); Refresh();
            }, true),
            UiVisual.Button("移除选中", () =>
            {
                if (grid.SelectedItem is not UiUdsRequest item) return;
                store.Current.UdsRequests.Remove(item); Save(); Refresh();
            }),
            Disabled("发送请求", "纯 UI 阶段未接入真实 ISO-TP / UDS")
        ));
        left.Children.Add(grid);
        UiVisual.At(layout, UiVisual.Box(UiVisual.Scroll(left)), 0);
        var right = UiVisual.Stack();
        right.Children.Add(UiVisual.Text("诊断会话与响应", 17, true));
        right.Children.Add(UiVisual.Note("未来关联请求 ID、响应 ID、NRC、响应时间、原始 ISO-TP 帧和取消状态。"));
        right.Children.Add(UiVisual.Empty("未建立诊断会话", "当前没有真实 ECU 响应。配置请求不等于已发送。"));
        UiVisual.At(layout, UiVisual.Box(right), 1);
        return layout;
    }

    public static UIElement Flash(UiStudyStore store, Action<string> notify)
    {
        void Save() { store.Save(); notify("烧录配置仅保存为 UI 草稿"); }
        var layout = UiVisual.Columns(new GridLength(460), UiVisual.Star());
        var left = UiVisual.Stack();
        left.Children.Add(UiVisual.Text("烧录配置", 18, true));
        left.Children.Add(UiVisual.Note("基于已研究的诊断烧录工作方式设计输入及状态，暂不接 CAN / UDS。"));
        var firmware = UiVisual.Input(store.Current.SelectedFirmware);
        firmware.IsReadOnly = true;
        left.Children.Add(UiVisual.Field("固件文件（仅文件路径）", firmware));
        left.Children.Add(UiVisual.Button("选择固件", () =>
        {
            var dialog = new OpenFileDialog { Title = "选择供界面体验的固件文件",
                Filter = "固件 (*.hex;*.s19;*.s28;*.s37;*.bin)|*.hex;*.s19;*.s28;*.s37;*.bin|所有文件|*.*" };
            if (dialog.ShowDialog(Window.GetWindow(layout)) != true) return;
            firmware.Text = dialog.FileName;
            store.Current.SelectedFirmware = firmware.Text;
            Save();
        }, true));
        var profile = UiVisual.Input(store.Current.FlashProfile);
        profile.Margin = new Thickness(0, 15, 0, 10);
        left.Children.Add(UiVisual.Field("烧录配置名称 / Profile", profile));
        left.Children.Add(UiVisual.Button("保存配置草稿", () =>
        { store.Current.FlashProfile = profile.Text; Save(); }));
        left.Children.Add(UiVisual.Rule());
        left.Children.Add(UiVisual.Text("执行安全", 15, true));
        left.Children.Add(UiVisual.Note("真实烧录需要实体权限、完整预检、分阶段请求/ACK 记录和故障恢复。UI 草稿不得触发下载、擦除、复位。"));
        left.Children.Add(UiVisual.Toolbar(
            Disabled("开始烧录", "未接入真实 FlashLab 和 UDS 服务，不可执行"),
            Disabled("请求停止", "当前没有执行中的烧录任务")));
        UiVisual.At(layout, UiVisual.Box(UiVisual.Scroll(left)), 0);
        var right = UiVisual.Stack();
        right.Children.Add(UiVisual.Text("烧录过程", 18, true));
        right.Children.Add(UiVisual.Note("以下为流程阶段名称，不是已发生的烧录日志或进度。"));
        string[] stages =
        [
            "01  文件与配置检查", "02  诊断会话 / 安全访问",
            "03  请求下载与擦除", "04  数据块传输", "05  完整性验证",
            "06  ECU 复位与恢复"
        ];
        foreach (var stage in stages)
        {
            var card = UiVisual.Stack(true);
            card.Children.Add(UiVisual.Text(stage, 14, true));
            card.Children.Add(UiVisual.Text("   未运行", 12, false, UiVisual.Muted));
            var line = UiVisual.Box(card, new Thickness(14));
            line.Margin = new Thickness(0, 7, 0, 0);
            right.Children.Add(line);
        }
        right.Children.Add(UiVisual.Alert("当前无真实进度、无 ECU 响应、无烧录结果。"));
        UiVisual.At(layout, UiVisual.Box(UiVisual.Scroll(right)), 1);
        return layout;
    }

    public static UIElement Wave(UiStudyStore store, Action<string> notify)
    {
        var layout = UiVisual.Columns(new GridLength(270), UiVisual.Star());
        var left = UiVisual.Stack();
        left.Children.Add(UiVisual.Text("波形与信号", 17, true));
        left.Children.Add(UiVisual.Note("选择需要观察的 DBC 信号；真正样本和参考由业务接线后提供。"));
        var combo = UiVisual.Combo(235);
        combo.Items.Add("未选择信号");
        foreach (var msg in store.Current.DbcMessages)
            foreach (var signal in msg.Signals) combo.Items.Add(msg.Name + " / " + signal.Name);
        combo.SelectedIndex = Math.Max(0, combo.Items.IndexOf(store.Current.SelectedWaveSignal));
        combo.SelectionChanged += (_, _) =>
        {
            store.Current.SelectedWaveSignal = combo.SelectedItem?.ToString() ?? "";
            store.Save(); notify("已保存波形信号选择草稿");
        };
        left.Children.Add(UiVisual.Field("信号选择", combo));
        left.Children.Add(UiVisual.Text("分析设置", 15, true));
        left.Children.Add(UiVisual.Note("参考波形、判定窗口、容差和有效覆盖率将在后续完整工作流中关联。"));
        left.Children.Add(Disabled("开始真实采集", "无真实测量和总线采集接口"));
        left.Children.Add(Disabled("保存参考", "没有实际采集的样本"));
        UiVisual.At(layout, UiVisual.Box(UiVisual.Scroll(left)), 0);

        var right = UiVisual.Stack();
        right.Children.Add(UiVisual.Text("时间序列工作区", 17, true));
        right.Children.Add(UiVisual.Note("缩放、游标与时间定位视图区；没有真实数据时不绘制假波形。"));
        var plot = new Grid { Background = UiVisual.Brush("#FCFEFF"), Height = 400,
            Margin = new Thickness(0, 13, 0, 10) };
        var canvas = new Canvas { ClipToBounds = true, Background = UiVisual.Brush("#FCFEFF") };
        plot.Children.Add(canvas);
        void Redraw(double width, double height)
        {
            canvas.Children.Clear();
            if (width <= 0 || height <= 0) return;
            for (int i = 1; i < 10; i++)
            {
                double x = width * i / 10;
                canvas.Children.Add(new Line { X1 = x, X2 = x, Y1 = 0, Y2 = height,
                    Stroke = UiVisual.Line, StrokeThickness = 1 });
            }
            for (int i = 1; i < 6; i++)
            {
                double y = height * i / 6;
                canvas.Children.Add(new Line { X1 = 0, X2 = width, Y1 = y, Y2 = y,
                    Stroke = UiVisual.Line, StrokeThickness = 1 });
            }
            var marker = UiVisual.Text("暂无真实波形样本", 16, true, UiVisual.Muted);
            Canvas.SetLeft(marker, Math.Max(10, width / 2 - 90));
            Canvas.SetTop(marker, height / 2 - 14);
            canvas.Children.Add(marker);
        }
        canvas.SizeChanged += (_, _) => Redraw(canvas.ActualWidth, canvas.ActualHeight);
        right.Children.Add(plot);
        var cursor = UiVisual.Note("双游标：等待时间序列数据");
        right.Children.Add(cursor);
        right.Children.Add(UiVisual.Toolbar(
            UiVisual.Button("重置视图", () => cursor.Text = "视图已重置；没有实际时间数据"),
            Disabled("进行真实判定", "尚无参考波形与采集证据")
        ));
        UiVisual.At(layout, UiVisual.Box(UiVisual.Scroll(right)), 1);
        return layout;
    }
}
