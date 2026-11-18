using EPB.Core.Communication;
using EPB.Core.Configuration;

namespace EPB.Core.Testing;

/// <summary>后端结果通道。流程控制只消费通道，不参与信号采集或解码。</summary>
public enum ResultChannel
{
    Value,
    Verdict
}

public enum ResultVerdict
{
    Passed,
    Failed
}

public enum SignalFaultCode
{
    NoNewSample,
    ReadTimeout,
    DecodeError,
    InvalidDefinition,
    TransportError,
    EvaluationError
}

/// <summary>
/// 后端读取定义。它只描述如何定位和解码信号，不包含 WPF 或流程控制字段。
/// </summary>
public sealed record SignalReadDefinition(
    string MessageDefinitionId,
    string SignalDefinitionId,
    string SignalName,
    bool Extended,
    uint Id,
    int StartByte,
    int StartBit,
    int BitLength,
    string ByteOrder,
    double Factor,
    double Offset,
    string Unit)
{
    public static SignalReadDefinition From(ParseParameters parse)
    {
        ArgumentNullException.ThrowIfNull(parse);
        return new(
            parse.MessageDefinitionId,
            parse.SignalDefinitionId,
            parse.SignalName,
            parse.Extended,
            parse.Id,
            parse.StartByte,
            parse.StartBit,
            parse.BitLength,
            parse.ByteOrder,
            parse.Factor,
            parse.Offset,
            parse.Unit);
    }

    public static SignalReadDefinition From(CanMessageDefinition message, CanSignalDefinition signal)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(signal);
        return new(
            message.DefinitionId,
            signal.DefinitionId,
            CanDefinitionDisplay.SignalPrimaryName(signal),
            message.Extended,
            message.Id,
            signal.StartByte,
            signal.StartBitInByte,
            signal.BitLength,
            signal.ByteOrder == CanByteOrder.Motorola ? "Motorola" : "Intel",
            signal.Factor,
            signal.Offset,
            signal.Unit);
    }

    internal ParseParameters ToConfigurationParameters() => new()
    {
        Source = ConfigurationSource.通信数据库,
        MessageDefinitionId = MessageDefinitionId,
        SignalDefinitionId = SignalDefinitionId,
        SignalName = SignalName,
        Extended = Extended,
        Id = Id,
        StartByte = StartByte,
        StartBit = StartBit,
        BitLength = BitLength,
        ByteOrder = ByteOrder,
        Factor = Factor,
        Offset = Offset,
        Unit = Unit
    };
}

public sealed record SignalObservation(
    double Value,
    string RawFrame,
    DateTimeOffset Timestamp,
    string? Unit = null);

public sealed record SignalFault(SignalFaultCode Code, string Message);

/// <summary>一次硬件读取结果。没有新样本时不代表流程失败，调用方可以继续轮询。</summary>
public sealed record SignalReadResult(SignalObservation? Observation, SignalFault? Fault)
{
    public bool HasValue => Observation is not null;

    public static SignalReadResult Received(SignalObservation observation)
        => new(observation, null);

    public static SignalReadResult Unavailable()
        => new(null, null);

    public static SignalReadResult Faulted(SignalFaultCode code, string message)
        => new(null, new(code, message));
}

/// <summary>
/// 由后端提供给流程控制消费的中间结果。
/// Value 和 Verdict 可以同时存在；控制节点不负责重新读取、解码或比较。
/// </summary>
public sealed record ProducedSignalResult(
    SignalObservation? Observation,
    ResultVerdict? Verdict,
    SignalFault? Fault)
{
    public bool HasValue => Observation is not null;
    public bool HasVerdict => Verdict is not null;

    public static ProducedSignalResult FromObservation(SignalObservation observation)
        => new(observation, null, null);

    public ProducedSignalResult WithVerdict(ResultVerdict verdict)
        => this with { Verdict = verdict };

    public ProducedSignalResult WithFault(SignalFaultCode code, string message)
        => this with { Fault = new(code, message) };
}

/// <summary>流程控制对显式步骤结果的引用。它不是另一个操作定义。</summary>
public sealed record ResultReference(string ProducerNodeId, ResultChannel Channel);

/// <summary>后端流程控制类型；不对应 UI 控件名称。</summary>
public enum TestFlowControlKind
{
    Condition,
    WaitForVerdict,
    ValueBranch,
    Repeat
}

public enum TestFlowPathKind
{
    None,
    Then,
    Else,
    Hit,
    Timeout,
    Case,
    Default,
    Body
}

public sealed record SignalDefinitionReference(string MessageDefinitionId, string SignalDefinitionId);

/// <summary>
/// 流程控制只保存对结果或通信定义的引用，不保存另一份操作定义。
/// </summary>
public sealed class TestFlowControlDefinition
{
    public TestFlowControlKind Kind { get; set; }
    public ResultReference? ResultReference { get; set; }
    public ResultVerdict RequiredVerdict { get; set; } = ResultVerdict.Passed;
    public int PeriodMs { get; set; } = 20;
    public int TimeoutMs { get; set; } = 1000;
    /// <summary>QUICK bounded repeat: 1..100 iterations, full child-body replay.</summary>
    public int RepeatCount { get; set; } = 2;
    /// <summary>Delay between iterations, never an automatic output reset.</summary>
    public int RepeatDelayMs { get; set; }
    public SignalDefinitionReference? SignalDefinition { get; set; }
}
