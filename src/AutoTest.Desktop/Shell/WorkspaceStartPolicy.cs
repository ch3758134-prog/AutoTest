namespace AutoTest.Desktop.Shell;

/// <summary>Presentation navigation only. A TEST asset does not imply it is linked to a project.</summary>
public static class WorkspaceStartPolicy
{
    public static string Choose(bool hasProject, int projectCount, bool hasLinkedTestCase) =>
        !hasProject ? "none" :
        projectCount <= 0 ? "project" :
        !hasLinkedTestCase ? "case" : "ready";
}
