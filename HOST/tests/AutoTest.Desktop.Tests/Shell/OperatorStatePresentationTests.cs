using System.Globalization;
using System.Windows.Data;
using AutoTest.Desktop.Shell;
using Xunit;

namespace AutoTest.Desktop.Tests.Shell;

public sealed class OperatorStatePresentationTests
{
    private static readonly OperatorStateTextConverter Converter = new();

    [Theory]
    [InlineData("未执行", "未开始")]
    [InlineData("执行中", "进行中")]
    [InlineData("实测通过", "通过")]
    [InlineData("实测失败", "失败")]
    [InlineData("引用需修复", "资源需检查")]
    [InlineData("已停止", "已停止")]
    [InlineData("执行异常", "执行异常")]
    public void InternalExecutionStatesArePresentedInUserLanguage(
        string state, string expected)
    {
        var output = Converter.Convert(
            state, typeof(string), null!, CultureInfo.InvariantCulture);

        Assert.Equal(expected, output);
    }

    [Fact]
    public void UnknownStateIsNotSilentlyConvertedIntoPass()
    {
        Assert.Equal("结果待核对",
            Converter.Convert("结果待核对", typeof(string), null!,
                CultureInfo.InvariantCulture));
        Assert.Same(Binding.DoNothing,
            Converter.ConvertBack("通过", typeof(string), null!,
                CultureInfo.InvariantCulture));
    }
}
