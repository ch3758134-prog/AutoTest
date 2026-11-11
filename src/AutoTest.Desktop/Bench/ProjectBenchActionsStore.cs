using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace AutoTest.Desktop.Bench;

// Companion configuration beside the SAVED .autotest-project.json file.
// A profile revision does not delete the user's named physical action sequences.
public sealed class ProjectBenchActionsStore(
    Func<string?> projectPath, Func<string?> projectId)
{
    private sealed record FileDocument(int Schema, string ProjectId,
        IReadOnlyList<BenchKeyAction> Actions);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private (string Path, string Id) Resolve()
    {
        var original = projectPath();
        var id = projectId();
        if (string.IsNullOrWhiteSpace(original) || string.IsNullOrWhiteSpace(id) ||
            !Guid.TryParse(id, out var projectGuid) || projectGuid == Guid.Empty)
            throw new InvalidOperationException("请先创建并保存正式工程，再配置工程按键动作。");
        var full = Path.GetFullPath(original);
        if (!full.EndsWith(".autotest-project.json", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(full))
            throw new InvalidOperationException("工程未保存到有效的 .autotest-project.json 文件。");
        return (full + ".bench-actions.json", projectGuid.ToString("D"));
    }

    public async Task<IReadOnlyList<BenchKeyAction>> LoadAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (path, project) = Resolve();
        if (!File.Exists(path)) return Array.Empty<BenchKeyAction>();
        var json = await File.ReadAllTextAsync(path, ct);
        var document = JsonSerializer.Deserialize<FileDocument>(json, JsonOptions);
        if (document is null || document.Schema != 1 ||
            !string.Equals(document.ProjectId, project, StringComparison.OrdinalIgnoreCase) ||
            document.Actions is null)
            throw new InvalidDataException("按键动作文件与当前工程 ID 或配置版本不匹配，已拒绝加载。");
        BenchActionValidation.ValidateAll(document.Actions);
        return document.Actions;
    }

    public async Task SaveAsync(IReadOnlyList<BenchKeyAction> actions, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        BenchActionValidation.ValidateAll(actions);
        var (path, project) = Resolve();
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp,
                JsonSerializer.Serialize(new FileDocument(1, project, actions), JsonOptions), ct);
            ct.ThrowIfCancellationRequested();
            // A second project may have been opened while serialization was in flight.
            if (Resolve() != (path, project))
                throw new InvalidOperationException("工程已切换，取消保存旧工程动作。");
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
