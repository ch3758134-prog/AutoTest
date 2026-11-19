using System.Collections.Immutable;
using System.Text;
using AutoTest.Projects.KeyActions;
using AutoTest.Projects.Project;
using Xunit;

namespace AutoTest.Projects.Tests;

public sealed class KeyActionCatalogTests
{
    [Fact]
    public void TwoFieldStep_RoundTripsWithFullMaskAndZeroMask()
    {
        var first = KeyActionStep.Create(0x0000A, 300);
        var waitLike = KeyActionStep.Create(0x0000A, 500);
        var release = KeyActionStep.Create(0, 100);
        var action = new KeyActionDefinition(Guid.NewGuid(), "组合操作", 0,
            ImmutableArray.Create(first, waitLike, release));
        var catalog = KeyActionCatalog.Create() with
        {
            Controller = new KeyActionControllerPin(Guid.NewGuid(), 2, new string('a', 64)),
            Actions = ImmutableArray.Create(action)
        };
        var encoded = KeyActionCatalogCodec.Encode(catalog);
        var decoded = KeyActionCatalogCodec.Decode(encoded);
        Assert.Equal(catalog.CatalogId, decoded.CatalogId);
        Assert.Equal(catalog.Actions[0].ActionId, decoded.Actions[0].ActionId);
        Assert.Equal(catalog.Controller!.Sha256, decoded.Controller!.Sha256);
        Assert.Equal(new uint[] { 0xA, 0xA, 0 }, decoded.Actions[0].Steps.Select(s => s.KeyMask));
        Assert.Equal(new[] { 300, 500, 100 }, decoded.Actions[0].Steps.Select(s => s.HoldMs));
        var json = Encoding.UTF8.GetString(encoded);
        Assert.DoesNotContain("Wait", json);
        Assert.DoesNotContain("SetKeys", json);
        Assert.DoesNotContain("Release", json);
    }

    [Theory]
    [InlineData(0x100000u, 100)]
    [InlineData(0x0u, -1)]
    [InlineData(0x0u, 600001)]
    public void InvalidKeyMaskOrHoldIsRejected(uint mask, int ms)
    {
        Assert.Throws<ProjectDataException>(() => KeyActionStep.Create(mask, ms));
    }

    [Fact]
    public void DuplicateStepsAndActionNamesRejected()
    {
        var a = KeyActionStep.Create(0x80, 10);
        var b = new KeyActionDefinition(Guid.NewGuid(), "A", 0, ImmutableArray.Create(a, a));
        Assert.Throws<ProjectDataException>(() => b.Validate());
        var valid = KeyActionDefinition.Create("A", 0);
        var duplicate = KeyActionDefinition.Create("a", 1);
        var catalog = KeyActionCatalog.Create() with
        {
            Actions = ImmutableArray.Create(valid, duplicate)
        };
        Assert.Throws<ProjectDataException>(() => catalog.Validate());
    }

    [Fact]
    public void GarbledSchemaAndControllerFingerprintAreFailClosed()
    {
        var catalog = KeyActionCatalog.Create() with { SchemaVersion = 9 };
        Assert.Throws<ProjectDataException>(() => KeyActionCatalogCodec.Encode(catalog));
        Assert.Throws<ProjectDataException>(() => KeyActionCatalogCodec.Decode("{broken"u8));
        Assert.Throws<ProjectDataException>(() => new KeyActionControllerPin(
            Guid.NewGuid(), 1, "not-a-sha").Validate());
    }
}
