using AutoTest.Projects.Project;

namespace AutoTest.Desktop.Projects;

/// <summary>
/// The sole owner of the open project for the desktop UI. No second project model.
/// </summary>
public sealed record OpenProjectContext(ProjectDefinition Definition, string ManifestPath)
{
    public string RootDirectory => Path.GetDirectoryName(ManifestPath)!;
}

public sealed class ProjectWorkspaceService
{
    public const string ManifestFileName = "project.autotest-project.json";
    public OpenProjectContext? Current { get; private set; }
    public event EventHandler? CurrentChanged;

    /// <summary>Validate strings without touching disk; safe for each TextBox keystroke.</summary>
    public static string ValidateInput(string name, string parentDirectory)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "请输入工程名称。";
        if (name != name.Trim() || name.Length > 100)
            return "工程名称不能以空格开头或结尾，长度不得超过 100 个字符。";
        if (name is "." or ".." || name.EndsWith('.') || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return "工程名称包含不允许的字符或结尾符号。";
        var stem = name.Split('.')[0].ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" ||
            (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) ||
                                  stem.StartsWith("LPT", StringComparison.Ordinal)) &&
             stem[3] is >= '1' and <= '9'))
            return "工程名称不能使用 Windows 保留设备名。";
        if (string.IsNullOrWhiteSpace(parentDirectory))
            return "请选择工程保存位置。";
        try
        {
            var full = Path.GetFullPath(parentDirectory);
            if (!Path.IsPathFullyQualified(full))
                return "请选择完整的保存目录路径。";
            _ = Path.Combine(full, name, ManifestFileName);
            return "";
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "工程保存路径无效：" + error.Message;
        }
    }

    public static string PreviewManifestPath(string name, string parentDirectory)
    {
        if (!string.IsNullOrEmpty(ValidateInput(name, parentDirectory)))
            return "填写工程名称与保存位置后显示创建路径";
        try { return Path.Combine(Path.GetFullPath(parentDirectory), name, ManifestFileName); }
        catch (Exception) { return "工程路径暂不可用"; }
    }

    public async Task<OpenProjectContext> CreateAsync(
        string name, string parentDirectory, CancellationToken cancellationToken = default)
    {
        var error = ValidateInput(name, parentDirectory);
        if (error.Length > 0) throw new ArgumentException(error);

        // ProjectRepository has an atomic FILE replace API. New PROJECT creation
        // must instead be non-overwriting. Build in a sibling staging directory,
        // and move it into its final name only when fully written and validated.
        var result = await Task.Run(async () =>
        {
            var parent = Path.GetFullPath(parentDirectory);
            if (!Directory.Exists(parent))
                throw new DirectoryNotFoundException("工程保存位置不存在，请重新选择目录。");
            var target = Path.Combine(parent, name);
            if (Directory.Exists(target) || File.Exists(target))
                throw new IOException("同名工程目录已经存在，不能覆盖。请更换工程名称或保存位置。");

            var staging = Path.Combine(parent, ".autotest-create-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var definition = ProjectDefinition.Create(name);
                var stagedManifest = Path.Combine(staging, ManifestFileName);
                await ProjectRepository.SaveDefinitionAsync(definition, stagedManifest, cancellationToken)
                    .ConfigureAwait(false);
                // Read the actual persisted file; no fabricated successful project state.
                var verified = await ProjectRepository.OpenDefinitionAsync(stagedManifest, cancellationToken)
                    .ConfigureAwait(false);
                if (verified.Id != definition.Id || verified.Name != name)
                    throw new InvalidDataException("工程文件写入后校验未通过。");
                cancellationToken.ThrowIfCancellationRequested();
                Directory.Move(staging, target); // refuses a pre-existing target
                return new OpenProjectContext(verified, Path.Combine(target, ManifestFileName));
            }
            finally
            {
                if (Directory.Exists(staging))
                {
                    try { Directory.Delete(staging, recursive: true); }
                    catch (IOException) { /* Keep cleanup failure from masking the original error. */ }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }, cancellationToken);

        // The desktop caller resumes on the WPF synchronization context.
        Current = result;
        CurrentChanged?.Invoke(this, EventArgs.Empty);
        return result;
    }
}
