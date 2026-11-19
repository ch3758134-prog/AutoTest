namespace AutoTest.Testing.Authoring;

/// <summary>
/// Case-level report metadata. Presentation options never disable mandatory audit
/// collection or change a TEST verdict. Serializes inside the existing TEST JSON.
/// </summary>
public sealed record TestReportSettings(string? Title, string? CaseNumber, string? Description)
{
    public static TestReportSettings Default { get; } = new(null, null, null);

    public TestReportSettings Validate()
    {
        if ((Title?.Length ?? 0) > 256 || (CaseNumber?.Length ?? 0) > 128 ||
            (Description?.Length ?? 0) > 4096 ||
            new[] { Title, CaseNumber, Description }.Any(value =>
                value is not null && value.Any(c =>
                    (char.IsControl(c) && c is not '\n' and not '\t') || c == '\0')))
            throw new FormatException("Case report title, number or description is invalid.");
        return this;
    }
}
