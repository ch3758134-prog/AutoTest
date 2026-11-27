using AutoTest.Desktop.Testing;
using Xunit;

namespace AutoTest.Desktop.Tests.Testing;

public sealed class StepActionPaletteCatalogTests
{
    [Fact]
    public void EveryDisplayedTabHasRealSelectableActionsAndNoHiddenTypeIsSelected()
    {
        var actions = TestStepInspector.PaletteActions(caseOnly: false);
        var tabs = StepActionPaletteCatalog.Categories(actions);
        Assert.Contains("外部操作", tabs);
        Assert.Contains("读取与观察", tabs);
        Assert.Contains("分析与判断", tabs);
        Assert.Contains("流程控制", tabs);
        Assert.All(tabs, tab =>
        {
            var visible = StepActionPaletteCatalog.Filter(actions, tab, "");
            Assert.NotEmpty(visible);
            Assert.All(visible, option => Assert.Contains(option, actions));
        });
        var outside = StepActionPaletteCatalog.Filter(actions, "外部操作", "");
        Assert.Contains(outside, x => x.Key == "can");
        Assert.Contains(outside, x => x.Key == "key");
        Assert.Contains(outside, x => x.Key == "ign");
        Assert.DoesNotContain(actions, x => x.Key is "flash" or "can-send" or "can-wait");
        Assert.Equal(actions.Select(x => x.Key).Distinct().Count(), actions.Count);
    }

    [Fact]
    public void SearchFindsRealActionWithoutRequiringUnrelatedTabAndSwitchOnlyOffersCase()
    {
        var actions = TestStepInspector.PaletteActions(caseOnly: false);
        Assert.Contains(StepActionPaletteCatalog.Filter(actions, "常用步骤", "按键"),
            x => x.Key == "key");
        var caseActions = TestStepInspector.PaletteActions(caseOnly: true);
        Assert.Equal(["流程控制"], StepActionPaletteCatalog.Categories(caseActions));
        var candidates = StepActionPaletteCatalog.Filter(caseActions, "流程控制", "");
        Assert.Single(candidates);
        Assert.Equal("case", candidates[0].Key);
    }
}
