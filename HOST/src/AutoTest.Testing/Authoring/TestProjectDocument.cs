using System.IO;
using System.Text.Json;

namespace AutoTest.Testing.Authoring;

/// <summary>
/// Ordered project-specific instances of reusable TEST definitions, not an
/// alternative TEST language. Each entry has its own stable ItemId and can
/// reference a shared TEST definition more than once.
///
//// Legacy Enabled=true means the entry participates in the complete automatic
//// test; false means it was explicitly excluded by persisted project data.
/// The *temporary* engineer RunSelected checkbox is not this property.
/// New entries participate immediately (Enabled=true), requiring no second
/// enable action. Never auto-reactivate old excluded entries during migration.
/// </summary>
public sealed record TestProjectEntry(Guid ItemId, Guid PackageAssetId,
    int PackageRevision, string PackageSha256, string DisplayName, bool Enabled);

public sealed record TestProjectDocument(int SchemaVersion, Guid Id, string Name,
    List<TestProjectEntry> Packages)
{
    /// <summary>Optional project-wide vehicle-environment stages. Each stage
    /// is a pinned invocation of the SAME TestDocument DSL/RealRunCoordinator.
    /// No scanner, fixture, FLASH or new test-program language is introduced.
    /// Separate stages are not ordinary product-judgement cases.</summary>
    public TestProjectEntry? ProjectSetup { get; init; }
    public TestProjectEntry? ProjectTeardown { get; init; }

    public static TestProjectDocument Create(string name) => new(2, Guid.NewGuid(), name, []);

    public IReadOnlyList<TestProjectEntry> OrderedExecutionEntries()
    {
        var stages = new List<TestProjectEntry>(Packages.Count + 2);
        if (ProjectSetup is not null) stages.Add(ProjectSetup);
        stages.AddRange(Packages);
        if (ProjectTeardown is not null) stages.Add(ProjectTeardown);
        return stages;
    }

    /// <summary>
    /// The ONE authoritative production scope is the COMPLETE ordered entry
    /// list, never a persisted per-case enable/disable sub-selection.
    /// Historical Enabled=false data remains readable so the production
    /// resource contract can fail CLOSED and demand explicit repair; it may
    /// never silently omit those entries from the proposed execution scope.
    /// Engineer RunSelected is separate and only affects manual debugging.
    /// </summary>
    public IReadOnlyList<TestProjectEntry> AutomaticRunEntries() =>
        Packages.ToArray();

    public void Validate()
    {
        if (SchemaVersion is not (1 or 2) || Id == Guid.Empty ||
            (SchemaVersion == 1 && (ProjectSetup is not null || ProjectTeardown is not null)) ||
            string.IsNullOrWhiteSpace(Name) ||
            Name.Length > 120 || Packages is null || Packages.Count > 1000)
            throw new InvalidDataException("测试项目身份或用例数量无效。");
        var itemIds = new HashSet<Guid>();
        foreach (var item in OrderedExecutionEntries())
        {
            if (item is null || item.ItemId == Guid.Empty || item.PackageAssetId == Guid.Empty ||
                !itemIds.Add(item.ItemId) || item.PackageRevision <= 0 ||
                item.PackageSha256 is not { Length: 64 } ||
                !item.PackageSha256.All(Uri.IsHexDigit) ||
                string.IsNullOrWhiteSpace(item.DisplayName) || item.DisplayName.Length > 120)
                throw new InvalidDataException("测试项目包含缺失身份、重复序号或未固定修订的用例引用。");
        }
    }
}

public static class TestProjectCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, PropertyNameCaseInsensitive = false
    };
    public static byte[] Encode(TestProjectDocument project)
    {
        ArgumentNullException.ThrowIfNull(project);
        project.Validate();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(project, Options);
        if (bytes.Length > 2 * 1024 * 1024)
            throw new InvalidDataException("测试项目超过 2MB 大小限制。");
        return bytes;
    }
    public static TestProjectDocument Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is <= 0 or > 2 * 1024 * 1024)
            throw new InvalidDataException("测试项目数据大小无效。");
        try
        {
            var document = JsonSerializer.Deserialize<TestProjectDocument>(bytes, Options)
                ?? throw new InvalidDataException("测试项目内容为空。");
            document.Validate();
            return document;
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("测试项目 JSON 无法读取。", error);
        }
    }
}
