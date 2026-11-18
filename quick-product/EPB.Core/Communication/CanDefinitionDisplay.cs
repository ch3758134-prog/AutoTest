using System.Globalization;

namespace EPB.Core.Communication;

/// <summary>通信定义的统一显示与搜索规则。Name 是协议名称，DisplayName 是用户别名。</summary>
public static class CanDefinitionDisplay
{
    public static string SignalPrimaryName(CanSignalDefinition signal)
    {
        if (!string.IsNullOrWhiteSpace(signal.DisplayName)) return signal.DisplayName.Trim();
        if (!string.IsNullOrWhiteSpace(signal.Name)) return signal.Name.Trim();
        return "未命名信号";
    }

    public static string MessagePrimaryName(CanMessageDefinition message) =>
        string.IsNullOrWhiteSpace(message.DisplayName) ? message.Name : message.DisplayName;

    public static string FormatMessage(CanMessageDefinition message)
    {
        var name = MessagePrimaryName(message)?.Trim();
        var id = $"0x{message.Id:X}";
        var identity = string.IsNullOrWhiteSpace(name) ? id : $"{name} · {id}";
        var frame = message.Extended ? "扩展帧" : "标准帧";
        var description = FirstNonEmpty(message.Description, message.Comment);
        return string.IsNullOrWhiteSpace(description)
            ? $"{identity} · {frame}"
            : $"{identity} · {frame} · {Shorten(description, 52)}";
    }

    public static string FormatMessageSearchResult(CanMessageDefinition message, string? query)
    {
        var summary = FormatMessage(message);
        if (string.IsNullOrWhiteSpace(query)) return summary;

        var q = query.Trim();
        var hits = new List<string>();
        if (ContainsAny(q, message.Name, message.DisplayName, message.Description, message.Comment))
            hits.Add("报文");
        var signal = (message.Signals ?? [])
            .FirstOrDefault(x => ContainsAny(q, x.Name, x.DisplayName, x.Description, x.Comment));
        if (signal is not null)
            hits.Add($"信号：{SignalPrimaryName(signal)}");
        return hits.Count == 0 ? summary : $"{summary}\n命中：{string.Join("、", hits)}";
    }

    public static string FormatSignalSearchResult(CanMessageDefinition message, CanSignalDefinition signal) =>
        $"{FormatMessage(message)}\n命中信号：{SignalPrimaryName(signal)} · {SignalPosition(signal)}" +
        (string.IsNullOrWhiteSpace(FirstNonEmpty(signal.Description, signal.Comment))
            ? ""
            : $"\n{Shorten(FirstNonEmpty(signal.Description, signal.Comment), 58)}");

    public static bool MatchesMessageIdentity(CanMessageDefinition message, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        var q = query.Trim();
        return ContainsAny(q, message.Id.ToString(CultureInfo.InvariantCulture), $"0x{message.Id:X}",
            message.Name, message.DisplayName, message.Description, message.Comment);
    }

    public static IReadOnlyList<CanSignalDefinition> MatchingSignals(CanMessageDefinition message, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return message.Signals ?? [];
        var q = query.Trim();
        return (message.Signals ?? [])
            .Where(signal => ContainsAny(q, signal.Name, signal.DisplayName, signal.Description, signal.Comment))
            .ToList();
    }

    public static string FormatSignal(CanMessageDefinition message, CanSignalDefinition signal) =>
        $"{SignalPrimaryName(signal)} · 字节{signal.StartByte + 1}·位{signal.StartBitInByte + 1}/{signal.BitLength}b";

    public static string SignalPosition(CanSignalDefinition signal) =>
        $"字节{signal.StartByte + 1}·位{signal.StartBitInByte + 1}/{signal.BitLength}b";

    public static string SignalNamesAndDescription(CanSignalDefinition signal)
    {
        var names = new[] { signal.Name, signal.DisplayName }
            .Where(x => !string.IsNullOrWhiteSpace(x));
        var description = FirstNonEmpty(signal.Description, signal.Comment);
        return $"名称/别名：{string.Join(" / ", names)}" +
               (string.IsNullOrWhiteSpace(description) ? "" : $" · {description}");
    }

    public static string FormatSignalCandidate(CanSignalDefinition signal)
    {
        var description = FirstNonEmpty(signal.Description, signal.Comment);
        return string.IsNullOrWhiteSpace(description)
            ? $"{SignalPrimaryName(signal)}\n{SignalPosition(signal)}"
            : $"{SignalPrimaryName(signal)}\n{Shorten(description, 58)} · {SignalPosition(signal)}";
    }

    public static string FullDescription(CanMessageDefinition message)
    {
        var description = FirstNonEmpty(message.Description, message.Comment);
        return string.IsNullOrWhiteSpace(description) ? FormatMessage(message) : $"{FormatMessage(message)}\n{description}";
    }

    public static string SignalTooltip(CanSignalDefinition signal)
    {
        var names = new[] { signal.DisplayName, signal.Name }.Where(x => !string.IsNullOrWhiteSpace(x));
        var details = new[] { $"名称：{signal.Name}\n别名：{signal.DisplayName}", signal.Description, signal.Comment }
            .Where(x => !string.IsNullOrWhiteSpace(x));
        return string.Join("\n", details);
    }

    public static string SearchText(CanMessageDefinition message)
    {
        var values = new List<string>
        {
            message.Id.ToString(CultureInfo.InvariantCulture), $"0x{message.Id:X}", message.Name,
            message.DisplayName, message.Description, message.Comment
        };
        foreach (var signal in message.Signals ?? [])
        {
            values.AddRange([signal.DisplayName, signal.Name, signal.Description, signal.Comment]);
        }
        return string.Join("\n", values.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    public static bool Matches(CanMessageDefinition message, string? query) =>
        string.IsNullOrWhiteSpace(query) || SearchText(message).Contains(query.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string FirstNonEmpty(params string?[] values) => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? "";

    private static bool ContainsAny(string query, params string?[] values) =>
        values.Any(value => !string.IsNullOrWhiteSpace(value) && value.Contains(query, StringComparison.OrdinalIgnoreCase));

    private static string Shorten(string value, int maxLength)
    {
        var oneLine = string.Join(" ", value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        return oneLine.Length <= maxLength ? oneLine : oneLine[..(maxLength - 1)] + "…";
    }
}
