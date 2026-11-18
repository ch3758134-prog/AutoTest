using System.Globalization;
using System.Text.RegularExpressions;

namespace EPB.Core.Testing;

/// <summary>将报文解析的图形化判定控件转换为兼容旧配置的表达式。</summary>
public static class ExpectationExpression
{
    public readonly record struct EditorValue(string Operator, string First, string Second);
    public const string None = "不判定";
    public const string Range = "范围";

    private static readonly IReadOnlyDictionary<string, string> DisplayToOperator = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["等于"] = "=", ["不等于"] = "!=", ["大于"] = ">", ["大于等于"] = ">=",
        ["小于"] = "<", ["小于等于"] = "<=", [Range] = Range
    };
    private static readonly IReadOnlyDictionary<string, string> OperatorToDisplay = DisplayToOperator.ToDictionary(x => x.Value, x => x.Key, StringComparer.Ordinal);

    /// <summary>编辑器显示中文语义；配置文件仍保存稳定的符号表达式。</summary>
    public static IReadOnlyList<string> OperatorOptions { get; } = [None, .. DisplayToOperator.Keys];

    public static string Build(string? operatorText, string? first, string? second = null)
    {
        var display = operatorText?.Trim() ?? None;
        var op = DisplayToOperator.GetValueOrDefault(display, display);
        var left = first?.Trim() ?? string.Empty;
        if (op == None || left.Length == 0) return string.Empty;
        if (op == Range)
        {
            var right = second?.Trim() ?? string.Empty;
            return right.Length == 0 ? string.Empty : $"{left}..{right}";
        }
        return DisplayToOperator.Values.Contains(op, StringComparer.Ordinal) ? $"{op}{left}" : string.Empty;
    }

    public static void Parse(string? expression, out string operatorText, out string first, out string second)
    {
        operatorText = None;
        first = string.Empty;
        second = string.Empty;
        var text = expression?.Trim() ?? string.Empty;
        if (text.Length == 0) return;
        var range = Regex.Match(text, @"^\s*([-+]?\d+(?:\.\d+)?)\s*\.\.\s*([-+]?\d+(?:\.\d+)?)\s*$");
        if (range.Success)
        {
            operatorText = Range;
            first = range.Groups[1].Value;
            second = range.Groups[2].Value;
            return;
        }
        var match = Regex.Match(text, @"^(==|=|!=|>=|<=|>|<)\s*([-+]?\d+(?:\.\d+)?)$");
        if (!match.Success) return;
        var symbol = match.Groups[1].Value == "==" ? "=" : match.Groups[1].Value;
        operatorText = symbol;
        first = match.Groups[2].Value;
    }

    public static EditorValue ParseForEditor(string? expression)
    {
        Parse(expression, out var op, out var first, out var second);
        return new EditorValue(OperatorToDisplay.GetValueOrDefault(op, op), first, second);
    }
}
