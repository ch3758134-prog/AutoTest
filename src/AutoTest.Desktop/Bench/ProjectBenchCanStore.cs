using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AutoTest.Desktop.Bench;

public sealed class ProjectBenchCanStore(Func<string?> projectPath, Func<string?> projectId)
{
    private sealed record FileDocument(int Schema, string ProjectId,
        IReadOnlyList<BenchCanMessage> Messages);
    private static (string Path, string Id) Resolve(Func<string?> path, Func<string?> id)
    {
        var raw = path();
        var identity = id();
        if (string.IsNullOrWhiteSpace(raw) || !Guid.TryParse(identity, out var guid) || guid == Guid.Empty)
            throw new InvalidOperationException("请先保存工程，才能保存 CAN 发送配置。");
        var full = Path.GetFullPath(raw);
        if (!full.EndsWith(".autotest-project.json", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(full))
            throw new InvalidOperationException("未找到当前已保存的正式工程文件。");
        return (full + ".bench-can.json", guid.ToString("D"));
    }
    private (string Path, string Id) Current() => Resolve(projectPath, projectId);

    public async Task<IReadOnlyList<BenchCanMessage>> LoadAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (path, id) = Current();
        if (!File.Exists(path)) return Array.Empty<BenchCanMessage>();
        var data = JsonSerializer.Deserialize<FileDocument>(await File.ReadAllTextAsync(path, ct));
        if (data is not { Schema: 1 } || data.ProjectId != id || data.Messages is null)
            throw new InvalidDataException("CAN 报文配置不属于当前工程或文件版本不匹配。");
        BenchCanMessage.ValidateAll(data.Messages);
        return data.Messages;
    }
    public async Task SaveAsync(IReadOnlyList<BenchCanMessage> messages, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        BenchCanMessage.ValidateAll(messages);
        var (path, id) = Current();
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(
                new FileDocument(1, id, messages), new JsonSerializerOptions { WriteIndented = true }), ct);
            ct.ThrowIfCancellationRequested();
            if (Current() != (path, id))
                throw new InvalidOperationException("工程在保存 CAN 配置期间已发生变化。");
            File.Move(tmp, path, overwrite: true);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
}
