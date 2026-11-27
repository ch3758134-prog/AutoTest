using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoTest.Testing.Authoring;

// Only the authored order and definition identity are persisted.
// The editor always uses the current saved definition; execution captures
// its immutable content at the moment a real run is admitted.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TestProjectEntry(Guid ItemId, Guid PackageAssetId, string DisplayName);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TestProjectDocument(int SchemaVersion, Guid Id, string Name,
    List<TestProjectEntry> Packages)
{
    public const int CurrentSchema = 3;
    public static TestProjectDocument Create(string name) =>
        new(CurrentSchema, Guid.NewGuid(), name, []);

    public IReadOnlyList<TestProjectEntry> OrderedExecutionEntries() => Packages.ToArray();
    public IReadOnlyList<TestProjectEntry> AutomaticRunEntries() => Packages.ToArray();

    public void Validate()
    {
        if (SchemaVersion != CurrentSchema || Id == Guid.Empty ||
            string.IsNullOrWhiteSpace(Name) || Name.Length > 120 ||
            Packages is null || Packages.Count > 1000)
            throw new InvalidDataException("TEST 项目格式、名称或用例数量无效。");
        var ids = new HashSet<Guid>();
        foreach (var entry in Packages)
        {
            if (entry is null || entry.ItemId == Guid.Empty ||
                entry.PackageAssetId == Guid.Empty || !ids.Add(entry.ItemId) ||
                string.IsNullOrWhiteSpace(entry.DisplayName) || entry.DisplayName.Length > 120)
                throw new InvalidDataException("TEST 项目包含无效或重复的用例调用。");
        }
    }
}

public static class TestProjectCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static byte[] Encode(TestProjectDocument project)
    {
        ArgumentNullException.ThrowIfNull(project);
        project.Validate();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(project, Options);
        if (bytes.Length > 2 * 1024 * 1024)
            throw new InvalidDataException("TEST 项目超过 2MB 大小限制。");
        return bytes;
    }

    public static TestProjectDocument Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is <= 0 or > 2 * 1024 * 1024)
            throw new InvalidDataException("TEST 项目数据大小无效。");
        try
        {
            var doc = JsonSerializer.Deserialize<TestProjectDocument>(bytes, Options)
                ?? throw new InvalidDataException("TEST 项目内容为空。");
            doc.Validate();
            return doc;
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("TEST 项目不是当前版本的有效数据。", error);
        }
    }
}
