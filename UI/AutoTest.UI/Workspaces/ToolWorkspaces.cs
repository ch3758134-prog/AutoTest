using System.Windows;
using AutoTest.UI.Model;
using AutoTest.UI.Design;

namespace AutoTest.UI.Workspaces;

/// <summary>Professional tools own independent windows, not an overwritten navigation tree.</summary>
public static class ToolWorkspaces
{
    public static UIElement Build(string name, UiSession state, Action changed, Action<string> status) =>
        name switch
        {
            "DBC 编辑" => new DbcWorkspace(state, changed, status),
            "CAN 工作台" => new CanWorkspace(state, changed, status),
            "综合手动台架" => new BenchWorkspace(state, changed, status),
            "输入配置" => new InputWorkspace(state, changed, status),
            "UDS 诊断" => new UdsWorkspace(state, changed, status),
            "ECU 烧录" => new FlashWorkspace(state, changed, status),
            "波形分析" => new WaveWorkspace(state, changed, status),
            "设备诊断" => new DeviceWorkspace(state, changed, status),
            _ => UiKit.Empty("未知工作区", name)
        };
}
