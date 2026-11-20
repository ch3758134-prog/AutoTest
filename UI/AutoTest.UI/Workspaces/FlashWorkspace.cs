using System.IO;
using System.Windows;
using System.Windows.Controls;
using AutoTest.UI.Design;
using AutoTest.UI.Model;
using Microsoft.Win32;

namespace AutoTest.UI.Workspaces;

public sealed class FlashWorkspace : Grid
{
    public FlashWorkspace(UiSession state, Action changed, Action<string> status)
    {
        Margin = new Thickness(16);
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition());
        Children.Add(UiKit.Header("ECU 烧录", "固件 → 配置 → 预检 → 执行阶段 → 证据；不可逆实体操作必须由真实后端授权"));
        var area = UiKit.Columns(365, 0);
        Grid.SetRow(area, 1); Children.Add(area);
        var left = new StackPanel { Margin = new Thickness(0, 0, 11, 0) };
        area.Children.Add(UiKit.Card(UiKit.Scroll(left)));
        left.Children.Add(UiKit.Text("烧录配置", 17, true));
        left.Children.Add(UiKit.Note("这里可以组织固件文件与配置的 UI 工作流；不会解析 HEX、生成 Seed/Key 或执行实体烧录。"));
        var firmware = UiKit.Input(state.FlashFile);
        left.Children.Add(UiKit.Field("固件文件", firmware));
        left.Children.Add(UiKit.Button("选择文件", () =>
        {
            var dlg = new OpenFileDialog { Title = "选择 ECU 固件",
                Filter = "固件 (*.hex;*.s19;*.s28;*.s37)|*.hex;*.s19;*.s28;*.s37|全部文件 (*.*)|*.*" };
            if (dlg.ShowDialog() != true) return;
            firmware.Text = dlg.FileName;
            state.FlashFile = dlg.FileName; changed();
            status("已选择文件，但未执行真正的文件校验和烧录。");
        }));
        var profile = UiKit.Input(state.FlashProfile);
        left.Children.Add(UiKit.Field("烧录配置名称 / Profile", profile));
        var bootTx = UiKit.Input("18DA50F1");
        var bootRx = UiKit.Input("18DAF150");
        left.Children.Add(UiKit.Field("Boot 物理请求 ID（仅 UI 编辑）", bootTx));
        left.Children.Add(UiKit.Field("Boot 响应 ID（仅 UI 编辑）", bootRx));
        left.Children.Add(UiKit.Button("应用配置草稿", () =>
        {
            state.FlashProfile = profile.Text;
            state.FlashFile = firmware.Text; changed();
            status("已更新本地 Flash UI 配置；CAN ID 编辑未写入真实烧录 Profile。");
        }, true));
        left.Children.Add(UiKit.Text("执行操作", 15, true));
        var preflight = UiKit.Button("实际预检", () => { });
        var flash = UiKit.Button("开始烧录", () => { }, true);
        var stop = UiKit.Button("请求停止", () => { });
        foreach (var b in new[] { preflight, flash, stop })
            UiKit.Disabled(b, "未连接真实 FlashLab 和设备授权；不会进行物理编程");
        left.Children.Add(UiKit.Actions(preflight, flash, stop));

        var right = new StackPanel();
        Grid.SetColumn(right, 1); area.Children.Add(UiKit.Card(UiKit.Scroll(right)));
        right.Children.Add(UiKit.Text("烧录过程", 19, true));
        right.Children.Add(UiKit.Note("进度、块确认、复位和结果必须来自真实日志。当前不生成假百分比和假成功。", true));
        foreach (var stage in new[] { "会话建立与扩展诊断", "安全访问与 Seed/Key",
            "进入 Boot", "下载请求 / RequestDownload", "TransferData 数据块传输",
            "TransferExit 与一致性检查", "ECU 复位", "结果与证据归档" })
        {
            var row = new DockPanel { Margin = new Thickness(0, 5, 0, 5) };
            var badge = UiKit.Text("未执行", 11, false, UiKit.Muted);
            DockPanel.SetDock(badge, Dock.Right);
            row.Children.Add(badge);
            row.Children.Add(UiKit.Text(stage, 13));
            right.Children.Add(row);
            right.Children.Add(new Border { Height = 1, Background = UiKit.Outline });
        }
        right.Children.Add(UiKit.Text("阶段日志", 15, true));
        right.Children.Add(UiKit.Empty("尚无真实烧录日志", "后续接线将展示请求、响应、分块确认、失败位置及审计导出。"));
    }
}
