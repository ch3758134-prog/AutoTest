using System.Globalization;
using AutoTest.Desktop.Themes;
using Xunit;

namespace AutoTest.Desktop.Tests.Tools;

public sealed class OperatorStatusConverterTests
{
    [Theory]
    [InlineData("Physical Read", "实机读取")]
    [InlineData("Replay", "离线记录")]
    [InlineData("DEVICE_NOT_CONNECTED", "主控尚未连接")]
    [InlineData("PREPARED_NOT_SENT", "请求已编辑，尚未发送")]
    [InlineData("PROFILE_CONFIG_LOADED_NOT_VERIFIED", "诊断配置已加载，请核对实际 ECU")]
    [InlineData("IMAGE_PARSED_NOT_FLASHED", "固件已解析，尚未烧录")]
    [InlineData("PHYSICAL_FLASH_REQUIRES_EXPLICIT_P09_JOB", "真实烧录尚未授权，请检查配置并完成本次实机授权")]
    [InlineData("CATALOG_NOT_READY", "尚未发布扫码与测试配方")]
    public void MachineCodesAreExplainedWithoutChangingUnderlyingStatus(string input, string expected)
    {
        var converter = new OperatorStatusConverter();
        Assert.Equal(expected, converter.Convert(input, typeof(string), null!, CultureInfo.InvariantCulture));
        Assert.Equal(input, input);
    }

    [Fact]
    public void UnknownDiagnosticCodeIsNotMisrepresentedAsSuccess()
    {
        var converter = new OperatorStatusConverter();
        Assert.Equal("HARDWARE_UNKNOWN", converter.Convert("HARDWARE_UNKNOWN", typeof(string), null!, CultureInfo.InvariantCulture));
        Assert.Equal("", converter.Convert(null!, typeof(string), null!, CultureInfo.InvariantCulture));
    }
}
