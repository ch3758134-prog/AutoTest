using AutoTest.Specialists.Uds;
using Xunit;

namespace AutoTest.Specialists.Tests.Uds;

public sealed class UdsDiagnosticResponseFormatterTests
{
    [Fact]
    public void ReadDid_Explains_Echoed_Identifier_And_Ascii()
    {
        var result = UdsDiagnosticResponseFormatter.Explain(
            [0x22, 0xF1, 0x90], [0x62, 0xF1, 0x90, 0x41, 0x42]);
        Assert.Contains("DID=0xF190", result);
        Assert.Contains("AB", result);
    }

    [Fact]
    public void NegativeResponse_Explains_Nrc_Without_Calling_It_Success()
    {
        var result = UdsDiagnosticResponseFormatter.Explain([0x22, 0xF1, 0x90],
            [0x7F, 0x22, 0x31]);
        Assert.Contains("RequestOutOfRange", result);
    }

    [Fact]
    public void ReadDid_Rejects_Did_Mismatch()
    {
        var result = UdsDiagnosticResponseFormatter.Explain(
            [0x22, 0xF1, 0x90], [0x62, 0xF1, 0x91, 0x01]);
        Assert.Contains("DID 不匹配", result);
    }

    [Fact]
    public void DtcStatusMask_Response_Parses_Basic_Records()
    {
        var result = UdsDiagnosticResponseFormatter.Explain([0x19,0x02,0xFF],
            [0x59,0x02,0xFF,0xC1,0x23,0x45,0x08]);
        Assert.Contains("C12345", result);
        Assert.Contains("0x08", result);
    }
}
