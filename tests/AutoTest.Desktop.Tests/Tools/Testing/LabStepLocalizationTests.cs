using AutoTest.Desktop.Tools.Testing;
using Xunit;

namespace AutoTest.Desktop.Tests.Tools.Testing;

public sealed class LabStepLocalizationTests
{
    [Theory]
    [InlineData("Sequence", "顺序执行")]
    [InlineData("TestCase", "测试用例")]
    [InlineData("If", "条件判断 IF")]
    [InlineData("Switch", "分支选择 SWITCH")]
    [InlineData("When", "条件等待 WHEN")]
    [InlineData("Loop", "循环")]
    [InlineData("TryCatch", "异常处理")]
    [InlineData("Subroutine", "子程序定义")]
    [InlineData("CallSubroutine", "调用子程序")]
    [InlineData("SetVariable", "设置变量")]
    [InlineData("Observe", "读取观测")]
    [InlineData("Assert", "断言判定")]
    [InlineData("Wait", "定时等待")]
    [InlineData("ControllerState", "按键状态")]
    [InlineData("Log", "写入记录")]
    [InlineData("Prepare", "准备")]
    [InlineData("Test", "测试")]
    [InlineData("Cleanup", "清理")]
    [InlineData("then", "条件成立")]
    [InlineData("else", "条件不成立")]
    [InlineData("finally", "始终清理")]
    public void OperatorSeesLocalizedTestStepTypeWithoutChangingIdentifier(
        string rawType, string expected)
    {
        Assert.Equal(expected, LabStepLocalization.Display(rawType));
    }

    [Fact]
    public void UnsupportedStepIsNotSilentlyMappedToAnotherOperation()
    {
        Assert.Equal("UnknownECUAction", LabStepLocalization.Display("UnknownECUAction"));
        Assert.Equal("", LabStepLocalization.Display(null));
        Assert.Contains("禁止真实运行", LabStepLocalization.Display("MockObserve"));
    }
}
