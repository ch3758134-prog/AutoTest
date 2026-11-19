using System.Windows;
using System.Windows.Controls;
using AutoTest.Frontend.Tools;

namespace AutoTest.Frontend;

public partial class ToolWindow : Window
{
    public ToolWindow(string kind, UiWorkbenchState state)
    {
        InitializeComponent();
        DataContext = state;
        (string title, string subtitle, UserControl content) presentation = kind switch
        {
            "bench" => ("手动台架", "CAN 发送 / 工位输入 / 实时 CAN 监控", new ManualBenchView()),
            "uds" => ("UDS 诊断", "可编辑请求序列 / 服务参数 / 请求与响应", new UdsView()),
            "flash" => ("ECU 烧录", "镜像 / 配置 / 烧录阶段与日志", new FlashView()),
            "dbc" => ("DBC 编辑器", "报文 / 信号 / 编码参数与校验", new DbcView()),
            "wave" => ("波形分析", "信号 / 曲线区域 / 参考与判据", new WaveView()),
            "inputs" => ("输入模拟配置", "工程输入状态 / 20 KEY 电阻组合 / 电压条件", new InputConfigView()),
            "device" => ("设备连接与诊断", "USB 设备身份 / 总线 / 错误与追踪", new DeviceView()),
            "records" => ("记录与报告", "历史执行 / 检索 / 报告与追溯", new RecordsView()),
            "production" => ("扫码生产", "生产工位 / 编号输入 / 结果与追溯", new ProductionView()),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知的 UI 工作区")
        };
        var (title, subtitle, content) = presentation;
        Title = $"AutoTest · {title}";
        ToolTitle.Text = title;
        ToolSubtitle.Text = subtitle;
        content.DataContext = state;
        ToolContent.Content = content;
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
