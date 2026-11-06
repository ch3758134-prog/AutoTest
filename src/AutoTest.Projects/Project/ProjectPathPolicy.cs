namespace AutoTest.Projects.Project;

public static class ProjectPathPolicy
{
    public static string Normalize(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
            throw new ProjectDataException("PATH_OUTSIDE_ROOT", "A relative path within the project is required.");
        if (relative.Split('/', '\\').Any(s => s is ".." or "." or ""))
            throw new ProjectDataException("PATH_OUTSIDE_ROOT", "Path traversal or empty path segment is forbidden.");
        var fullRoot = Path.GetFullPath(root);
        var full = Path.GetFullPath(Path.Combine(fullRoot, relative));
        ValidateInsideRoot(fullRoot, full);
        // Reject pre-existing reparse points (directory junctions/symlinks), including the final target.
        var current = fullRoot;
        foreach (var segment in Path.GetRelativePath(fullRoot, full).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new ProjectDataException("PATH_LINK", "Resource path may not traverse symbolic links or junctions.");
        }
        return full;
    }
    public static void ValidateInsideRoot(string root, string fullPath)
    {
        var canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var canonical = Path.GetFullPath(fullPath);
        if (!canonical.StartsWith(canonicalRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ProjectDataException("PATH_OUTSIDE_ROOT", "Resource path escapes the project directory.");
    }
}
