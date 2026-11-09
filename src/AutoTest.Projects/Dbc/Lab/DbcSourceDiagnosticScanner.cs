using System.Text.RegularExpressions;
using AutoTest.Projects.Project;

namespace AutoTest.Projects.Dbc.Lab;

/// <summary>
/// Read-only source diagnostics for the grammar implemented by DbcParserAdapter.
/// This does not rewrite the source or claim support for vendor DBC extensions.
/// A line of zero means the parser identified a semantic error without a reliable source line.
/// </summary>
public sealed record DbcSourceIssue(int Line, int Column, string Severity, string Code,
    string Message, string Snippet);

public static class DbcSourceDiagnosticScanner
{
    private static readonly Regex MessageLine = new(
        @"^BO_\s+\d+\s+\w+\s*:\s*[0-8]\s+\w+$", RegexOptions.Compiled);
    private static readonly Regex SignalLine = new(
        @"^SG_\s+\w+\s*(?:M|m\d+)?\s*:\s*\d+\|\d+@[01][+-]\s*\([^,]+,[^)]+\)\s*\[[^|]+\|[^]]+\]\s*""[^""]*""\s*\w+(?:,\w+)*$",
        RegexOptions.Compiled);
    private static readonly Regex ErrorLine = new(
        @"\bline\s+(\d+)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static IReadOnlyList<DbcSourceIssue> Inspect(string? source, string name = "Imported")
    {
        var issues = new List<DbcSourceIssue>();
        if (string.IsNullOrWhiteSpace(source))
        {
            issues.Add(new(0, 0, "ERROR", "DBC_PARSE", "DBC source is empty.", ""));
            return issues;
        }

        var lines = source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var inNs = false;
        var haveMessage = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("NS_ :", StringComparison.Ordinal))
            {
                inNs = true;
                continue;
            }
            if (line.StartsWith("BS_:", StringComparison.Ordinal))
            {
                inNs = false;
                continue;
            }
            if (inNs) continue;

            if (line.StartsWith("BO_", StringComparison.Ordinal))
            {
                haveMessage = true;
                if (!MessageLine.IsMatch(line))
                    Add("ERROR", "DBC_BO_SYNTAX", i, line, "Unsupported or malformed BO_ header.");
                continue;
            }
            if (line.StartsWith("SG_", StringComparison.Ordinal))
            {
                if (!haveMessage)
                    Add("ERROR", "DBC_SG_ORPHAN", i, line, "SG_ appears before any BO_ message.");
                if (!SignalLine.IsMatch(line))
                    Add("ERROR", "DBC_SG_SYNTAX", i, line, "Malformed SG_ or unsupported receivers/extension.");
                continue;
            }
            if (line.StartsWith("VERSION ", StringComparison.Ordinal))
            {
                if (line != "VERSION \"\"")
                    Add("WARNING", "DBC_READONLY_DIRECTIVE", i, line, "Nonempty VERSION is preserved read-only.");
                continue;
            }
            if (line.StartsWith("BU_:", StringComparison.Ordinal) || line.StartsWith("VAL_ ", StringComparison.Ordinal))
                continue;

            // Retain unknown directives; the authoring document refuses destructive edits.
            Add("WARNING", "DBC_READONLY_DIRECTIVE", i, line,
                "Directive is not guaranteed to round-trip through the editor; original source remains intact.");
        }

        if (!DbcParserAdapter.TryParse(source, out _, out var error))
        {
            var match = ErrorLine.Match(error ?? "");
            var lineNumber = match.Success && int.TryParse(match.Groups[1].Value, out var n) ? n : 0;
            // Avoid reporting the same syntax error twice; still report semantic validation failures.
            if (lineNumber == 0 || !issues.Any(x => x.Line == lineNumber && x.Severity == "ERROR"))
                issues.Add(new(lineNumber, lineNumber == 0 ? 0 : 1, "ERROR", "DBC_PARSE",
                    error ?? "DBC parse failed.", lineNumber > 0 && lineNumber <= lines.Length
                        ? Clip(lines[lineNumber - 1].Trim()) : ""));
        }
        return issues.OrderBy(x => x.Line == 0 ? int.MaxValue : x.Line)
            .ThenBy(x => x.Severity == "ERROR" ? 0 : 1).ToArray();

        void Add(string severity, string code, int index, string text, string message) =>
            issues.Add(new(index + 1, 1, severity, code, message, Clip(text)));
    }

    private static string Clip(string text) => text.Length <= 180 ? text : text[..180] + "…";
}
