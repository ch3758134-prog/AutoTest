using AutoTest.Specialists.Uds;
using Xunit;

namespace AutoTest.Specialists.Tests.Uds;

public sealed class TypedDiagnosticRequestBuilderTests
{
    [Fact]
    public void ReadDidAndDtcBuildValidServicePdUs()
    {
        var did=UdsTypedRequestBuilder.ReadDataByIdentifier(0xF190,0xF187);
        Assert.Equal("22F190F187",did.PduHex);
        Assert.Equal(UdsRiskLevel.ReadOnly,did.Risk);
        Assert.Equal("1902FF",UdsTypedRequestBuilder.ReadDtcByStatus(0xFF).PduHex);
    }
    [Fact]
    public void DangerousServicesStayIdentifiedAsNonReadOnly()
    {
        var session=UdsTypedRequestBuilder.SessionControl(0x02);
        Assert.False(session.IsReadOnly);
        Assert.Equal(UdsRiskLevel.DiagnosticStateChange,session.Risk);
        Assert.False(UdsTypedRequestBuilder.RequestDownload(0x10008000,0x100).IsReadOnly);
        Assert.False(UdsTypedRequestBuilder.SecuritySeedRequest(0x01).IsReadOnly);
        Assert.False(UdsTypedRequestBuilder.WriteDataByIdentifier(0xF190,[0x01]).IsReadOnly);
    }
    [Fact]
    public void DownloadAlfidEncodesBigEndianAddressAndSize()
    {
        Assert.Equal("3400441000800000010203",
            UdsTypedRequestBuilder.RequestDownload(0x10008000,0x10203).PduHex);
        Assert.Throws<ArgumentException>(()=>
            UdsTypedRequestBuilder.RequestDownload(0x10008000,0x100,addressBytes:2));
    }
    [Fact]
    public void RoutineTransferAndExitKeepCorrectPduBoundaries()
    {
        Assert.Equal("3101FF001234",UdsTypedRequestBuilder.RoutineControl(1,0xFF00,[0x12,0x34]).PduHex);
        Assert.Equal("3601ABCD",UdsTypedRequestBuilder.TransferData(1,[0xAB,0xCD]).PduHex);
        Assert.Equal("37",UdsTypedRequestBuilder.TransferExit().PduHex);
    }
    [Fact]
    public void MalformedParametersFailClosed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(()=>UdsTypedRequestBuilder.ReadDataByIdentifier());
        Assert.Throws<ArgumentOutOfRangeException>(()=>UdsTypedRequestBuilder.SessionControl(0x80));
        Assert.Throws<ArgumentOutOfRangeException>(()=>UdsTypedRequestBuilder.SecuritySeedRequest(2));
        Assert.Throws<ArgumentException>(()=>UdsTypedRequestBuilder.RequestDownload(0xFFFFFF00,0x200));
        Assert.Throws<ArgumentException>(()=>UdsTypedRequestBuilder.WriteDataByIdentifier(0xF190,[]));
    }
}
