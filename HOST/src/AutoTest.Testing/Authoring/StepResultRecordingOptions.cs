using System.Collections.Immutable;

namespace AutoTest.Testing.Authoring;

/// <summary>
/// Additional results are optional; mandatory step execution and judgement facts are not.
/// Stored as namespaced StepNode properties to preserve the single TEST JSON model.
/// </summary>
public enum ExtraResultCapture { None, Always, NonPass }

public sealed record StepResultRecordingOptions(
    ExtraResultCapture Capture,
    ImmutableArray<string> ExtraVariables,
    string? RequirementId,
    string? ReportNote,
    bool ShowEvidence)
{
    public static StepResultRecordingOptions Default { get; } =
        new(ExtraResultCapture.None, ImmutableArray<string>.Empty, null, null, true);

    public static StepResultRecordingOptions Read(StepNode step)
    {
        ArgumentNullException.ThrowIfNull(step);
        string Get(string name) => step.Properties.GetValueOrDefault("result." + name, "");
        var captureText = Get("capture");
        var capture = string.IsNullOrEmpty(captureText) ? ExtraResultCapture.None :
            Enum.TryParse<ExtraResultCapture>(captureText, ignoreCase: false, out var value) &&
            Enum.IsDefined(value)
                ? value : throw new FormatException("result.capture must be None, Always or NonPass.");

        var variables = Get("extraVariables").Split(',', StringSplitOptions.TrimEntries |
            StringSplitOptions.RemoveEmptyEntries);
        if (variables.Length > 16 || variables.Any(x => x.Length > 128 ||
            x.Any(char.IsControl) || x.Contains(' ')) ||
            variables.Distinct(StringComparer.OrdinalIgnoreCase).Count() != variables.Length)
            throw new FormatException("result.extraVariables must contain up to 16 distinct valid variable names.");
        if (capture == ExtraResultCapture.None && variables.Length != 0)
            throw new FormatException("result.extraVariables requires result.capture=Always or NonPass.");

        var requirement = Get("requirementId");
        var note = Get("reportNote");
        if (requirement.Length > 128 || requirement.Any(char.IsControl) ||
            note.Length > 2048 || note.Any(c => c == '\0'))
            throw new FormatException("Result report labels exceed the supported size or contain controls.");

        var showText = Get("showEvidence");
        var show = string.IsNullOrEmpty(showText) || showText == "true" ? true :
            showText == "false" ? false :
            throw new FormatException("result.showEvidence must be true or false.");
        return new StepResultRecordingOptions(capture, variables.ToImmutableArray(),
            string.IsNullOrWhiteSpace(requirement) ? null : requirement,
            string.IsNullOrWhiteSpace(note) ? null : note, show);
    }

    public StepNode Apply(StepNode step)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (ExtraVariables.IsDefault)
            throw new ArgumentException("result.extraVariables must be initialized.");
        var result = step.WithProperty("result.capture", Capture.ToString())
            .WithProperty("result.extraVariables", string.Join(",", ExtraVariables))
            .WithProperty("result.requirementId", RequirementId ?? "")
            .WithProperty("result.reportNote", ReportNote ?? "")
            .WithProperty("result.showEvidence", ShowEvidence ? "true" : "false");
        _ = Read(result); // same validation as preparation; no separate editor-only rules
        return result;
    }
}
