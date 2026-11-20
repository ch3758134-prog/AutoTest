using System.Windows;
using System.Windows.Controls;
using AutoTest.UI.Model;
using AutoTest.UI.Design;

namespace AutoTest.UI.Workspaces;

public sealed class StartWorkspace : Border
{
    public StartWorkspace(UiSession state, Action changed, Action<string> status,
        Action<string> navigate, Action<string> openTool)
    {
        var stack = new StackPanel { MaxWidth = 920, HorizontalAlignment = HorizontalAlignment.Left };
        stack.Children.Add(UiKit.Text("继续你的工程工作", 25, true));
        var lead = UiKit.Text("这是一套可操作的独立 UI 体验。先检验操作和布局，再由后续阶段接入真实业务。", 13, false, UiKit.Muted);
        lead.Margin = new Thickness(0, 9, 0, 26);
        stack.Children.Add(lead);
        stack.Children.Add(UiKit.Note("当前仅有 UI 会话状态，不代表真实工程已加载，也没有连接实体工位。"));
        stack.Children.Add(UiKit.Text("主要任务", 16, true));
        var tasks = UiKit.Actions(
            UiKit.Button("编辑测试程序", () => navigate("自动测试"), true),
            UiKit.Button("整理工程资源", () => navigate("工程资源")),
            UiKit.Button("进入生产操作", () => navigate("生产扫码")),
            UiKit.Button("查看历史", () => navigate("报告与历史")));
        tasks.Margin = new Thickness(0, 12, 0, 17);
        stack.Children.Add(tasks);
        stack.Children.Add(UiKit.Text("专业工具", 16, true));
        var quick = new WrapPanel { Margin = new Thickness(0, 13, 0, 20) };
        foreach (var name in new[] { "DBC 编辑", "CAN 工作台", "综合手动台架", "输入配置",
            "UDS 诊断", "ECU 烧录", "波形分析", "设备诊断" })
        {
            var item = name;
            quick.Children.Add(UiKit.Button(item, () => openTool(item)));
        }
        stack.Children.Add(quick);
        stack.Children.Add(UiKit.Text("工作区名称", 12, true));
        var input = UiKit.Input(state.ProjectName);
        input.Margin = new Thickness(0, 8, 0, 12);
        input.MaxWidth = 420;
        stack.Children.Add(input);
        stack.Children.Add(UiKit.Button("更新 UI 工作区名称", () =>
        {
            if (string.IsNullOrWhiteSpace(input.Text)) { status("工作区名称不能为空。"); return; }
            state.ProjectName = input.Text.Trim();
            changed();
            status("已更新本地 UI 工作区名称。保存会话以保留更改。");
        }));
        Child = UiKit.Scroll(stack);
    }
}
