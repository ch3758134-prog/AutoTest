using AutoTest.Desktop.Projects;
using Xunit;

namespace AutoTest.Desktop.Tests.Projects;

/// <summary>Pure resource-review tests. No fake ECU or hardware PASS.</summary>
public sealed class ResourceDependencyReviewTests
{
    [Fact]
    public void OneUpdatedDbcRootProducesOneReviewGroupWithAllAffectedCases()
    {
        var asset = Guid.NewGuid();
        var firstCase = Guid.NewGuid();
        var secondCase = Guid.NewGuid();
        var rows = new[]
        {
            new ResourceDependencyIssue("RESOURCE:" + asset.ToString("N"),
                "工程资源", "DBC 映射需核对", "静态驻车 · Car_State",
                "引用 r1，当前 r4", asset, firstCase),
            new ResourceDependencyIssue("RESOURCE:" + asset.ToString("N"),
                "工程资源", "DBC 映射需核对", "静态释放 · KeyStat",
                "引用 r1，当前 r4", asset, secondCase),
            new ResourceDependencyIssue("CASE:" + Guid.NewGuid().ToString("N"),
                "TEST 引用", "定义修订失效", "静态驻车", "旧版 TEST 不在当前工程")
        };
        var groups = ResourceDependencyGroup.Group(rows);
        Assert.Equal(2, groups.Count);
        var dbcGroup = Assert.Single(groups, x => x.ResourceId == asset);
        Assert.Equal(2, dbcGroup.AffectedCount);
        Assert.Contains("静态驻车", dbcGroup.Explanation);
        Assert.Contains("静态释放", dbcGroup.Explanation);
        Assert.Equal(firstCase, dbcGroup.InvocationId);
    }

    [Fact]
    public void ReviewRetainsTheExactAffectedInvocationAndNeverPicksAnotherAsset()
    {
        var source = Guid.NewGuid();
        var invocation = Guid.NewGuid();
        var groups = ResourceDependencyGroup.Group(
            [new("CASE:" + source.ToString("N"), "TEST 引用",
                "TEST 引用变化", "驻车实例", "必须确认",
                source, invocation)]);
        var group = Assert.Single(groups);
        Assert.Equal(source, group.ResourceId);
        Assert.Equal(invocation, group.InvocationId);
        Assert.Contains("必须确认", group.Explanation);
    }

    [Fact]
    public void EmptyReviewDoesNotClaimAnySimulatedPass()
    {
        Assert.Empty(ResourceDependencyGroup.Group([]));
    }
}
