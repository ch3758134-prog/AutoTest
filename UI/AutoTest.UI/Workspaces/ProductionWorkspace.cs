using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AutoTest.UI.Design;
using AutoTest.UI.Model;

namespace AutoTest.UI.Workspaces;

/// <summary>Operator-first scan and inspection surface, isolated from real Run.</summary>
public sealed class ProductionWorkspace : Grid
{
    public ProductionWorkspace(UiSession state, Action changed, Action<string> status)
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition());
        Children.Add(UiKit.Header("生产扫码检测", "编号捕获 → 匹配任务 → 工位预检 → 受控运行 → 结果；当前只演示扫码输入与异常提示"));
        var body = UiKit.Columns(0, 400);
        Grid.SetRow(body, 1); Children.Add(body);
        var left = new StackPanel { Margin = new Thickness(0, 0, 13, 0) };
        body.Children.Add(UiKit.Card(UiKit.Scroll(left)));
        left.Children.Add(UiKit.Text("开始一次检测", 21, true));
        left.Children.Add(UiKit.Note("只记录本地输入；不创建正式 DUT 身份绑定，不触发自动测试，不计入出厂统计。", true));
        left.Children.Add(UiKit.Text("扫描或输入编号", 14, true));
        var code = UiKit.Input();
        code.FontSize = 20;
        code.MinHeight = 48;
        code.Margin = new Thickness(0, 8, 0, 14);
        code.ToolTip = "支持扫码枪键盘输入；按 Enter 录入 UI 会话";
        left.Children.Add(code);
        var feedback = UiKit.Text("尚未录入编号", 14, true, UiKit.Muted);
        void Capture()
        {
            var value = code.Text.Trim();
            if (value.Length == 0) { feedback.Text = "请输入编号。"; return; }
            var exists = state.ScanEntries.Any(x => string.Equals(x.Barcode, value, StringComparison.OrdinalIgnoreCase));
            var item = new UiScanEntry
            {
                Barcode = value, Status = exists ? "本地 UI 会话内重复输入（非正式出厂查重）" : "已记录本地输入，尚未匹配工程"
            };
            state.ScanEntries.Insert(0, item);
            feedback.Text = item.Status;
            code.Clear(); code.Focus();
            changed(); status("扫码 UI 已录入编号；未建立正式 DUT 绑定或启动测试。");
        }
        code.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter) { Capture(); e.Handled = true; }
        };
        left.Children.Add(UiKit.Actions(UiKit.Button("记录编号", Capture, true)));
        left.Children.Add(feedback);
        left.Children.Add(new Border { Height = 1, Background = UiKit.Outline, Margin = new Thickness(0, 20, 0, 20) });
        left.Children.Add(UiKit.Text("本次检测", 17, true));
        foreach (var (label, value) in new (string, string)[]
        { ("工程匹配", "未接正式工程服务"), ("工位", "未连接"), ("运行任务", "尚未创建"),
          ("测试结果", "UNKNOWN / 无证据") })
        {
            var line = new DockPanel { Margin = new Thickness(0, 10, 0, 10) };
            var val = UiKit.Text(value, 12, false, UiKit.Muted);
            DockPanel.SetDock(val, Dock.Right);
            line.Children.Add(val);
            line.Children.Add(UiKit.Text(label, 13, true));
            left.Children.Add(line);
        }
        var run = UiKit.Button("启动真实检测", () => { }, true);
        UiKit.Disabled(run, "未连接扫码身份规则、正式 TEST 执行和真实工位");
        left.Children.Add(run);

        var right = new StackPanel();
        Grid.SetColumn(right, 1); body.Children.Add(UiKit.Card(right));
        right.Children.Add(UiKit.Text("当前录入记录", 17, true));
        right.Children.Add(UiKit.Note("此列表仅用来验证扫码界面和本地重复反馈，不是正式生产记录。"));
        var table = UiKit.Table(state.ScanEntries,
            ("时间", "Time", 135), ("编号", "Barcode", 150), ("状态", "Status", 0));
        foreach (var column in table.Columns) column.IsReadOnly = true;
        table.MinHeight = 300;
        right.Children.Add(table);
    }
}
