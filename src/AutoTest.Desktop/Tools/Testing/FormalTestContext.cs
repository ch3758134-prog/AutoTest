namespace AutoTest.Desktop.Tools.Testing;

/// <summary>
/// TEST-private read-only display context. INTEGRATION may provide real values from
/// its authoritative Project/Station/Run context; this model grants no device capabilities.
/// </summary>
public sealed record FormalTestContext(
    string ProjectId,
    string DutProfile,
    string StationId,
    string ProjectRevision,
    string ResourceRevision,
    string SessionGeneration,
    string HostBuildGitSha)
{
    public static FormalTestContext Unbound { get; } = new(
        "UNBOUND", "UNBOUND", "NOT_CONNECTED", "UNBOUND", "UNBOUND",
        "NOT_CONNECTED", "NOT_SUPPLIED_BY_HOST");
}
