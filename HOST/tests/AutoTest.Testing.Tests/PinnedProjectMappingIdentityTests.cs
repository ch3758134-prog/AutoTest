using AutoTest.Contracts;
using AutoTest.Testing.Authoring;
using Xunit;

namespace AutoTest.Testing.Tests;

/// <summary>Pure contract regression, not a real CAN/DBC hardware test.</summary>
public sealed class PinnedProjectMappingIdentityTests
{
    [Fact]
    public void SameDisplayNameInDifferentCasesIsNotANetworkResourceIdentity()
    {
        var first = new TestTargetMapping(
            "车身状态", TestTargetKind.RawCan, default, 0, "",
            BusRole.DutCan, 0x180);
        var second = new TestTargetMapping(
            "车身状态", TestTargetKind.RawCan, default, 0, "",
            BusRole.DutCan, 0x181);
        first.Validate();
        second.Validate();

        // A project-level station must preserve both pinned mapping values,
        // never discard one merely because engineers reused a local alias.
        var frozen = new HashSet<TestTargetMapping> { first, second };
        Assert.Equal(2, frozen.Count);
        Assert.Contains(frozen, x => x.CanId == 0x180);
        Assert.Contains(frozen, x => x.CanId == 0x181);
    }

    [Fact]
    public void IdenticalPinnedMappingInSeveralCasesIsOneFrozenTarget()
    {
        var original = new TestTargetMapping(
            "车辆状态报文", TestTargetKind.RawCan, default, 0, "",
            BusRole.DutCan, 0x180, Extended: false);
        var same = original with { };
        var union = new HashSet<TestTargetMapping> { original, same };
        Assert.Single(union);
    }

    [Fact]
    public void SameCanIdMayHaveDifferentDisplayAliasesWithoutExpandingFenceKey()
    {
        var first = new TestTargetMapping(
            "整车周期", TestTargetKind.RawCan, default, 0, "",
            BusRole.DutCan, 0x321);
        var alias = first with { Name = "供电状态模拟" };
        var physical = new HashSet<(BusRole Role, bool Extended, uint Id)>
        {
            (first.Role, first.Extended, first.CanId),
            (alias.Role, alias.Extended, alias.CanId)
        };
        // A single CAN frame identity means one PhysicalCommandFence allowlist
        // key and one replaceable periodic sender, regardless of display text.
        Assert.Single(physical);
    }
}
