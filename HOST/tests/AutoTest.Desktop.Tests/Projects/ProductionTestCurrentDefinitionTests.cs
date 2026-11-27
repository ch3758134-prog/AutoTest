using System.IO;
using System.Text;
using AutoTest.Testing.Authoring;
using Xunit;

namespace AutoTest.Desktop.Tests.Projects;

public sealed class ProductionTestCurrentDefinitionTests
{
    [Fact]
    public void FormalOrderContainsAllInvocationsWithoutEnableSwitches()
    {
        var asset = Guid.NewGuid();
        var a = new TestProjectEntry(Guid.NewGuid(), asset, "驻车");
        var b = new TestProjectEntry(Guid.NewGuid(), asset, "驻车复检");
        var project = TestProjectDocument.Create("正式 TEST") with { Packages = [a, b] };
        Assert.Equal(new[] { a.ItemId, b.ItemId },
            project.AutomaticRunEntries().Select(x => x.ItemId).ToArray());
    }

    [Fact]
    public void UnsupportedLegacyInvocationPropertiesAreNotAcceptable()
    {
        var input = TestProjectDocument.Create("TEST") with
        {
            Packages = [new TestProjectEntry(Guid.NewGuid(), Guid.NewGuid(), "驻车")]
        };
        var json = Encoding.UTF8.GetString(TestProjectCodec.Encode(input));
        var legacy = json.Replace("\"DisplayName\": \"驻车\"",
            "\"DisplayName\": \"驻车\", \"PackageSha256\": \"old\"");
        Assert.NotEqual(json, legacy);
        Assert.Throws<InvalidDataException>(() =>
            TestProjectCodec.Decode(Encoding.UTF8.GetBytes(legacy)));
    }
}
