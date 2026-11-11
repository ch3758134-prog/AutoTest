namespace AutoTest.Desktop.Shell;

/// <summary>
/// An ECU diagnostic/engineering tool owns its own full-size workspace.
/// The central document area is reserved for the primary TEST runner and
/// TEST authoring, not a collection of cramped tool UIs.
/// This policy is intentionally free of WPF and can be regression-tested.
/// </summary>
public static class R1ToolWindowPolicy
{
    public static bool IsProfessionalTool(string key) => key is
        "station" or "can" or "dbc" or "controller" or "bench" or
        "wave" or "uds" or "flash" or "scan" or "records" or "operations";

    public static bool IsMainDocument(string key) => key is "run" or "program";

    // Both actions operate on one DeviceLabEmbeddedView and one WinUSB session.
    public static string Normalize(string key) => key == "can" ? "station" : key;
}
