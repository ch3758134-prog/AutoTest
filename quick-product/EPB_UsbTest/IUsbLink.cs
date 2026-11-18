using System.Threading.Tasks;

namespace EPB_UsbTest;

/// <summary>
/// USB 链路抽象：FlowEngine / 连接上电策略依赖接口而非具体 UsbLink，
/// 便于在无硬件时用假实现测试执行编排。UsbLink 实现本接口，行为不变。
/// </summary>
public interface IUsbLink
{
    bool IsOpen { get; }

    /// <summary>0x41 CAN 上报帧（读线程触发，订阅者自行编派 UI）。</summary>
    event Action<CanFrame>? CanFrameReceived;

    /// <summary>发一帧请求并异步等待匹配响应。</summary>
    Task<(bool ok, string err, byte status, byte[] payload)> SendAndWaitAsync(
        byte cmd, byte[]? payload, int timeoutMs = 1000);
}
