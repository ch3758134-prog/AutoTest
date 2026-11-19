using System.IO;
using System.Text.Json;

namespace AutoTest.Testing.Authoring;

/// <summary>
/// ecu.test Project analogue: an ordered list of PACKAGE references, not an alternative
/// executable test tree. A package may intentionally be referenced more than once.
/// </summary>
public sealed record TestProjectEntry(Guid ItemId, Guid PackageAssetId,
    int PackageRevision, string PackageSha256, string DisplayName, bool Enabled);

public sealed record TestProjectDocument(int SchemaVersion, Guid Id, string Name,
    List<TestProjectEntry> Packages)
{
    public static TestProjectDocument Create(string name) => new(1, Guid.NewGuid(), name, []);

    public void Validate()
    {
        if (SchemaVersion != 1 || Id == Guid.Empty || string.IsNullOrWhiteSpace(Name) ||
            Name.Length > 120 || Packages is null || Packages.Count > 1000)
            throw new InvalidDataException("测试项目身份或用例数量无效。");
        var itemIds = new HashSet<Guid>();
        foreach (var item in Packages)
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
