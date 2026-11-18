using EPB.Core.Communication;

namespace EPB.Core.Configuration;

/// <summary>
/// 报文解析编辑器的唯一选择状态投影。
/// MessageDefinitionId 是数据库来源的唯一上游；Id/Extended 只是当前报文定义的运行参数投影。
/// </summary>
public sealed record ParseDefinitionSelection(
    CanMessageDefinition? Message,
    CanSignalDefinition? Signal,
    bool DefinitionMissing)
{
    public bool IsDatabaseBinding => Message is not null || DefinitionMissing;
}

public static class ParseDefinitionSelectionState
{
    public static ParseDefinitionSelection Resolve(ParseParameters parse, CanDatabase database)
    {
        ArgumentNullException.ThrowIfNull(parse);
        ArgumentNullException.ThrowIfNull(database);
        if (parse.Source != ConfigurationSource.通信数据库)
            return new(null, null, false);

        var message = database.Messages.FirstOrDefault(x => x.DefinitionId == parse.MessageDefinitionId);
        if (message is null) return new(null, null, true);
        var signal = message.Signals.FirstOrDefault(x => x.DefinitionId == parse.SignalDefinitionId);
        return new(message, signal, signal is null);
    }

    public static void SynchronizeMessageProjection(ParseParameters parse, CanMessageDefinition message)
    {
        ArgumentNullException.ThrowIfNull(parse);
        ArgumentNullException.ThrowIfNull(message);
        parse.Source = ConfigurationSource.通信数据库;
        parse.MessageDefinitionId = message.DefinitionId;
        parse.Id = message.Id;
        parse.Extended = message.Extended;
    }

    public static void BindMessage(ParseParameters parse, CanMessageDefinition message)
    {
        SynchronizeMessageProjection(parse, message);
        parse.SignalDefinitionId = "";
    }

    public static void BindSignal(ParseParameters parse, CanMessageDefinition message, CanSignalDefinition signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        if (!message.Signals.Contains(signal)) throw new InvalidOperationException("信号不属于当前报文。");
        SynchronizeMessageProjection(parse, message);
        parse.SignalDefinitionId = signal.DefinitionId;
    }
}
