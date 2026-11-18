using EPB.Core.Communication;

namespace EPB.Core.Configuration;

public enum CommunicationDefinitionResolutionStatus
{
    Resolved,
    DefinitionMissing,
    MessageMissing,
    SignalMissing,
    SignalBelongsToAnotherMessage,
    InvalidMessageDefinition,
    InvalidSignalDefinition
}

public sealed record CommunicationDefinitionDiagnostic(
    CommunicationDefinitionResolutionStatus Status,
    string Message);

public sealed record CommunicationDefinitionResolution(
    CommunicationDefinitionResolutionStatus Status,
    CanMessageDefinition? Message,
    CanSignalDefinition? Signal,
    IReadOnlyList<CommunicationDefinitionDiagnostic> Diagnostics)
{
    public bool Succeeded => Status == CommunicationDefinitionResolutionStatus.Resolved;

    public static CommunicationDefinitionResolution Missing(
        CommunicationDefinitionResolutionStatus status,
        string message)
        => new(status, null, null, [new(status, message)]);
}

/// <summary>
/// The only Core seam for resolving a step's communication-database definition.
/// Names and cached Id/bit fields are deliberately never used as fallback keys.
/// </summary>
public sealed class CommunicationDefinitionResolver
{
    public CommunicationDefinitionResolution Resolve(ConfigurationStep step, CanDatabase database)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(database);

        return step.Kind switch
        {
            ConfigurationStepKind.广播报文 => ResolveBroadcast(step.Broadcast, database),
            ConfigurationStepKind.报文解析 => ResolveParse(step.Parse, database),
            ConfigurationStepKind.波形判定 => ResolveWaveformAssertion(step.WaveformAssertion, database),
            _ => new(CommunicationDefinitionResolutionStatus.Resolved, null, null, [])
        };
    }

    public CommunicationDefinitionResolution ResolveMessage(CanDatabase database, string definitionId)
    {
        ArgumentNullException.ThrowIfNull(database);
        if (string.IsNullOrWhiteSpace(definitionId))
            return CommunicationDefinitionResolution.Missing(CommunicationDefinitionResolutionStatus.DefinitionMissing, "未提供报文 DefinitionId。");

        var message = database.Messages.FirstOrDefault(x => x.DefinitionId == definitionId);
        if (message is null)
            return CommunicationDefinitionResolution.Missing(CommunicationDefinitionResolutionStatus.MessageMissing, $"数据库报文定义不存在：{definitionId}。");

        var messageError = ValidateMessage(database, message);
        return messageError is not null
            ? CommunicationDefinitionResolution.Missing(CommunicationDefinitionResolutionStatus.InvalidMessageDefinition, messageError)
            : new(CommunicationDefinitionResolutionStatus.Resolved, message, null, []);
    }

    public CommunicationDefinitionResolution ResolveSignal(CanDatabase database, string messageDefinitionId, string signalDefinitionId)
    {
        var messageResult = ResolveMessage(database, messageDefinitionId);
        if (!messageResult.Succeeded)
            return messageResult;
        if (string.IsNullOrWhiteSpace(signalDefinitionId))
            return CommunicationDefinitionResolution.Missing(CommunicationDefinitionResolutionStatus.SignalMissing, $"报文 {messageDefinitionId} 未提供信号 DefinitionId。");

        var message = messageResult.Message!;
        var signal = message.Signals.FirstOrDefault(x => x.DefinitionId == signalDefinitionId);
        if (signal is null)
        {
            var elsewhere = database.Messages.Any(x => x.Signals.Any(s => s.DefinitionId == signalDefinitionId));
            return elsewhere
                ? CommunicationDefinitionResolution.Missing(CommunicationDefinitionResolutionStatus.SignalBelongsToAnotherMessage, $"信号 {signalDefinitionId} 不属于报文 {messageDefinitionId}。")
                : CommunicationDefinitionResolution.Missing(CommunicationDefinitionResolutionStatus.SignalMissing, $"数据库信号定义不存在：{signalDefinitionId}。");
        }

        var signalError = ValidateSignal(message, signal);
        return signalError is not null
            ? new(CommunicationDefinitionResolutionStatus.InvalidSignalDefinition, message, signal, [new(CommunicationDefinitionResolutionStatus.InvalidSignalDefinition, signalError)])
            : new(CommunicationDefinitionResolutionStatus.Resolved, message, signal, []);
    }

    private CommunicationDefinitionResolution ResolveBroadcast(BroadcastParameters broadcast, CanDatabase database)
    {
        if (broadcast.Source != ConfigurationSource.通信数据库)
            return broadcast.Dlc is > 64 or < 0
                ? CommunicationDefinitionResolution.Missing(CommunicationDefinitionResolutionStatus.InvalidMessageDefinition, $"独立广播步骤的 DLC 不合法：{broadcast.Dlc}。")
                : new(CommunicationDefinitionResolutionStatus.Resolved, null, null, []);
        return ResolveMessage(database, broadcast.MessageDefinitionId);
    }

    private CommunicationDefinitionResolution ResolveParse(ParseParameters parse, CanDatabase database)
    {
        if (parse.Source != ConfigurationSource.通信数据库)
            return ValidateDirectParse(parse);
        if (string.IsNullOrWhiteSpace(parse.MessageDefinitionId) || string.IsNullOrWhiteSpace(parse.SignalDefinitionId))
            return CommunicationDefinitionResolution.Missing(CommunicationDefinitionResolutionStatus.DefinitionMissing, "数据库解析步骤必须同时绑定报文和信号 DefinitionId。");
        return ResolveSignal(database, parse.MessageDefinitionId, parse.SignalDefinitionId);
    }

    private CommunicationDefinitionResolution ResolveWaveformAssertion(WaveformAssertionParameters assertion, CanDatabase database)
    {
        if (assertion.Source != ConfigurationSource.通信数据库)
            return ValidateDirectWaveformAssertion(assertion);
        if (string.IsNullOrWhiteSpace(assertion.MessageDefinitionId) || string.IsNullOrWhiteSpace(assertion.SignalDefinitionId))
            return CommunicationDefinitionResolution.Missing(CommunicationDefinitionResolutionStatus.DefinitionMissing, "波形判定步骤必须同时绑定报文和信号 DefinitionId。");
        return ResolveSignal(database, assertion.MessageDefinitionId, assertion.SignalDefinitionId);
    }

    private static CommunicationDefinitionResolution ValidateDirectParse(ParseParameters parse)
    {
        if (parse.BitLength is < 1 or > 32 || parse.StartByte < 0 || parse.StartBit is < 0 or > 7 || !double.IsFinite(parse.Factor) || !double.IsFinite(parse.Offset))
            return CommunicationDefinitionResolution.Missing(CommunicationDefinitionResolutionStatus.InvalidSignalDefinition, "独立解析定义的位范围或换算参数无效。");
        return new(CommunicationDefinitionResolutionStatus.Resolved, null, null, []);
    }

    private static CommunicationDefinitionResolution ValidateDirectWaveformAssertion(WaveformAssertionParameters assertion)
    {
        if (assertion.BitLength is < 1 or > 32 || assertion.StartByte < 0 || assertion.StartBit is < 0 or > 7 || !double.IsFinite(assertion.Factor) || !double.IsFinite(assertion.Offset))
            return CommunicationDefinitionResolution.Missing(CommunicationDefinitionResolutionStatus.InvalidSignalDefinition, "独立波形判定定义的位范围或换算参数无效。");
        return new(CommunicationDefinitionResolutionStatus.Resolved, null, null, []);
    }

    private static string? ValidateMessage(CanDatabase database, CanMessageDefinition message)
    {
        if (message.Id > 0x1FFFFFFFu)
            return $"报文 {message.DefinitionId} 的 ID 超出 CAN 范围。";
        if (message.Dlc is < 0 or > 64)
            return $"报文 {message.DefinitionId} 的 DLC 不合法：{message.Dlc}。";
        if (database.Messages.Count(x => x.Id == message.Id && x.Extended == message.Extended) > 1)
            return $"报文 0x{message.Id:X} 存在重复的 {(message.Extended ? "扩展" : "标准")}帧定义。";
        return null;
    }

    private static string? ValidateSignal(CanMessageDefinition message, CanSignalDefinition signal)
    {
        if (string.IsNullOrWhiteSpace(signal.Name))
            return $"报文 0x{message.Id:X} 的信号名称不能为空。";
        if (signal.BitLength is < 1 or > 32)
            return $"信号 {signal.DefinitionId} 的位长度不合法：{signal.BitLength}。";
        if (signal.StartBit < 0 || signal.StartBit >= Math.Max(1, message.Dlc) * 8)
            return $"信号 {signal.DefinitionId} 的起始位超出报文 DLC。";
        if (!double.IsFinite(signal.Factor) || !double.IsFinite(signal.Offset))
            return $"信号 {signal.DefinitionId} 的系数或偏移无效。";

        var valid = signal.ByteOrder == CanByteOrder.Motorola
            ? MotorolaFits(signal.StartBit, signal.BitLength, message.Dlc)
            : signal.StartBit + signal.BitLength <= message.Dlc * 8;
        return valid ? null : $"信号 {signal.DefinitionId} 的位范围超出报文 DLC。";
    }

    private static bool MotorolaFits(int startBit, int bitLength, int dlc)
    {
        var position = startBit;
        for (var i = 0; i < bitLength; i++)
        {
            if (position < 0 || position >= dlc * 8) return false;
            position = position % 8 == 0 ? position + 15 : position - 1;
        }
        return true;
    }

    /// <summary>Copies only canonical definition fields into an execution snapshot.</summary>
    public static void ApplyCanonicalProjection(ConfigurationStep step, CommunicationDefinitionResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (!resolution.Succeeded || resolution.Message is null) return;

        if (step.Kind == ConfigurationStepKind.广播报文)
        {
            step.Broadcast.MessageName = resolution.Message.Name;
            step.Broadcast.Id = resolution.Message.Id;
            step.Broadcast.Extended = resolution.Message.Extended;
            step.Broadcast.Dlc = (byte)Math.Clamp(resolution.Message.Dlc, 0, 64);
            return;
        }

        if (step.Kind == ConfigurationStepKind.报文解析 && resolution.Signal is { } signal)
        {
            step.Parse.SignalName = signal.Name;
            step.Parse.DatabaseSignalName = signal.Name;
            step.Parse.DisplayName = signal.DisplayName;
            step.Parse.Id = resolution.Message.Id;
            step.Parse.Extended = resolution.Message.Extended;
            step.Parse.StartByte = signal.StartByte;
            step.Parse.StartBit = signal.StartBitInByte;
            step.Parse.BitLength = signal.BitLength;
            step.Parse.ByteOrder = signal.ByteOrder == CanByteOrder.Motorola ? "Motorola" : "Intel";
            step.Parse.Factor = signal.Factor;
            step.Parse.Offset = signal.Offset;
            step.Parse.Unit = signal.Unit;
            step.Parse.DisplayMode = signal.DisplayMode;
            step.Parse.AlarmType = signal.AlarmType;
            step.Parse.Description = signal.Description;
            return;
        }

        if (step.Kind == ConfigurationStepKind.波形判定 && resolution.Signal is { } waveformSignal)
        {
            step.WaveformAssertion.SignalName = waveformSignal.Name;
            step.WaveformAssertion.DatabaseSignalName = waveformSignal.Name;
            step.WaveformAssertion.DisplayName = waveformSignal.DisplayName;
            step.WaveformAssertion.Id = resolution.Message.Id;
            step.WaveformAssertion.Extended = resolution.Message.Extended;
            step.WaveformAssertion.StartByte = waveformSignal.StartByte;
            step.WaveformAssertion.StartBit = waveformSignal.StartBitInByte;
            step.WaveformAssertion.BitLength = waveformSignal.BitLength;
            step.WaveformAssertion.ByteOrder = waveformSignal.ByteOrder == CanByteOrder.Motorola ? "Motorola" : "Intel";
            step.WaveformAssertion.Factor = waveformSignal.Factor;
            step.WaveformAssertion.Offset = waveformSignal.Offset;
            step.WaveformAssertion.Unit = waveformSignal.Unit;
        }
    }
}
