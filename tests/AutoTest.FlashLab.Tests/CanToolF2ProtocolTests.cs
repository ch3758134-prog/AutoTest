using System.Buffers.Binary;
using System.IO;
using AutoTest.FlashLab.Programming;
using AutoTest.FlashLab.Hardware;
using AutoTest.Devices.Can;
using AutoTest.Contracts;
using Xunit;

namespace AutoTest.FlashLab.Tests;

// Pure CanTool protocol byte layout and rejection-path checks. No fake ECU/ACK.
public sealed class CanToolF2ProtocolTests
{
    [Theory]
    [InlineData(0x18DA50F1u,true)]
    [InlineData(0x747u,false)]
    public void F2PhysicalCAN1SingleFrameIsPaddedToEightAndLoggedAsEight(uint id,bool extended)
    {
        // Pure frame/USB encoding only; no mock, hardware or ECU responses.
        // This reproduces the observed 02 10 03 request for Boot and APP.
        var logical=CanFrame.Create(BusRole.DutCan,extended,id,
            new byte[]{0x02,0x10,0x03});
        Assert.Equal((byte)3,logical.Dlc);
        var onWire=CanToolCan1Transport.PadProgrammingFrame(logical);
        Assert.Equal((byte)8,onWire.Dlc);
        Assert.Equal("0210030000000000",Convert.ToHexString(onWire.Data.ToArray()));
        Assert.Equal(id,onWire.Id);
        Assert.Equal(extended,onWire.ExtendedId);
        var usb=CanFrameCodec.ToUsbPayload(onWire,new BusRoleMap(default,1,0));
        Assert.Equal(15,usb.Length);
        Assert.Equal((byte)1,usb[0]); // Physical CAN1 = ECU
        Assert.Equal(extended?(byte)1:(byte)0,usb[1]);
        Assert.Equal((byte)8,usb[6]); // Actual CAN DLC sent to firmware
        Assert.Equal("0210030000000000",Convert.ToHexString(usb.AsSpan(7,8)));
        Assert.True(CanFrameCodec.FromUsbPayload(usb,
            new BusRoleMap(default,1,0),out var decoded));
        Assert.NotNull(decoded);
        Assert.Equal((byte)8,decoded!.Dlc);
    }

    [Fact]
    public void FlashPaddingDoesNotChangeAlreadyFullCANFrameOrGenericCANCodec()
    {
        var existing=CanFrame.Create(BusRole.DutCan,false,0x747,
            Convert.FromHexString("1011121314151617"));
        Assert.Same(existing,CanToolCan1Transport.PadProgrammingFrame(existing));
        var originalShort=CanFrame.Create(BusRole.DutCan,false,0x747,
            Convert.FromHexString("021003"));
        // The shared generic CAN codec remains unmodified for other features.
        var untouched=CanFrameCodec.ToUsbPayload(originalShort,
            new BusRoleMap(default,1,0));
        Assert.Equal((byte)3,untouched[6]);
        Assert.Throws<ArgumentException>(()=>CanToolCan1Transport.PadProgrammingFrame(
            CanFrame.Create(BusRole.DutCan,false,0x747,Array.Empty<byte>())));
    }

    [Fact]
    public void EraseDownloadVerifyAndResetMatchOriginalCanToolLayout()
    {
        Assert.Equal("3101FF001000800000068000",
            Convert.ToHexString(CanToolProgrammingProtocol.Erase(0x10008000,0x68000)));
        Assert.Equal("3400441000800000000200",
            Convert.ToHexString(CanToolProgrammingProtocol.Download(0x10008000,0x200)));
        Assert.Equal("3101FF011000800000000200",
            Convert.ToHexString(CanToolProgrammingProtocol.Verify(0x10008000,0x200)));
        Assert.Equal("37",Convert.ToHexString(CanToolProgrammingProtocol.RequestDownloadExit()));
        Assert.Equal("1101",Convert.ToHexString(CanToolProgrammingProtocol.UdsReset()));
        // Complete successful CAN1 capture verifies the last FF-padded tail.
        byte[] tail=Enumerable.Repeat((byte)0xFF,0x200).ToArray();
        Convert.FromHexString("12345678").CopyTo(tail,0x200-4);
        Assert.Equal((ushort)0x04E7,CanToolProgrammingProtocol.ComplementOfSum(tail));
        CanToolProgrammingProtocol.CheckVerify(
            Convert.FromHexString("7101FF0104E7"),0x04E7);
    }
    [Fact]
    public void DownloadMaximumUsesHighNibbleNotOriginalCanToolBug()
    {
        Assert.Equal(0x102u,CanToolProgrammingProtocol.ParseDownloadMaximum(
            Convert.FromHexString("74200102")));
        // Successful Boot CAN1 capture: 06 74 20 00 00 02 02 AA.
        // Only the exact OEM 6-byte PDU is accepted as 0x202.
        Assert.Equal(0x202u,CanToolProgrammingProtocol.ParseDownloadMaximum(
            Convert.FromHexString("742000000202")));
        Assert.Throws<InvalidDataException>(()=>
            CanToolProgrammingProtocol.ParseDownloadMaximum(
                Convert.FromHexString("742000000201")));
        Assert.Throws<InvalidDataException>(()=>
            CanToolProgrammingProtocol.ParseDownloadMaximum(Convert.FromHexString("74010102")));
        Assert.Throws<InvalidDataException>(()=>
            CanToolProgrammingProtocol.ParseDownloadMaximum(Convert.FromHexString("741000")));
        Assert.Throws<InvalidDataException>(()=>
            CanToolProgrammingProtocol.ParseDownloadMaximum(Convert.FromHexString("74200002")));
    }
    [Fact]
    public void NoSuccessWhenFf01ChecksumIsMissingOrMismatched()
    {
        var bytes=new byte[]{0x01,0x02,0x03};
        ushort checksum=CanToolProgrammingProtocol.ComplementOfSum(bytes);
        Assert.Equal((ushort)0xFFF9,checksum);
        Assert.Throws<InvalidDataException>(()=>
            CanToolProgrammingProtocol.CheckVerify(Convert.FromHexString("7101FF01"),checksum));
        Assert.Throws<InvalidDataException>(()=>
            CanToolProgrammingProtocol.CheckVerify(Convert.FromHexString("7101FF010000"),checksum));
        CanToolProgrammingProtocol.CheckVerify(Convert.FromHexString("7101FF01FFF9"),checksum);
    }
    [Fact]
    public void SecurityDllWithoutRealCanToolAbiMustNotBeAssumedCompatible()
    {
        // The test assembly is a CLR DLL, but does NOT expose SafetyVerificate.
        // No mock ECU, no seed calculation and no physical operations.
        Assert.Throws<NotSupportedException>(()=>
            CanToolSecurityProvider.Inspect(typeof(CanToolF2ProtocolTests).Assembly.Location));
    }

    [Fact]
    public void MissingSecurityLibraryFailsBeforeAnyProgrammingAction()
    {
        string missing=Path.Combine(Path.GetTempPath(),
            Guid.NewGuid().ToString("N")+".dll");
        Assert.Throws<FileNotFoundException>(()=>CanToolSecurityProvider.Inspect(missing));
    }

    [Fact]
    public void ReferenceCanToolInternalAlgorithmMatchesProvidedDemoStaticLogic()
    {
        // Artificial test input, NOT an ECU seed/key trace. The expected value
        // is derived from the provided sa_demo.dll ZLGKey machine instructions.
        Assert.Equal("E26D0EF8",Convert.ToHexString(
            CanToolReferenceLfsr.Calculate([0x11,0x22,0x33,0x44])));
        Assert.Throws<FormatException>(()=>CanToolReferenceLfsr.Calculate([1,2,3]));
        Assert.Throws<FormatException>(()=>CanToolReferenceLfsr.Calculate([1,2,3,4,5]));
        Assert.Throws<FormatException>(()=>CanToolReferenceLfsr.Calculate(null!));
    }
    [Fact]
    public void ReferenceSafetyFileProvenanceIsPinnedToUploadedBinaryHash()
    {
        // Do not accept arbitrary SafetyFile or KEY.dll binaries as matching
        // the original reference without direct provenance.
        Assert.Equal(
            "EE2C4A2791B88DCE19DB29F308752C8F79BC0654463D43E6880F65C6FDEA3833",
            CanToolSecurityProvider.KnownReferenceSafetyFileSha256);
    }

    [Fact]
    public void TransferDataCarriesExactSequenceAndImageBytes()
    {
        Assert.Equal("3601010203",
            Convert.ToHexString(CanToolProgrammingProtocol.Transfer(1,[1,2,3])));
        Assert.Equal("3600AA",
            Convert.ToHexString(CanToolProgrammingProtocol.Transfer(0,[0xAA])));
        Assert.Throws<ArgumentException>(()=>
            CanToolProgrammingProtocol.Transfer(1,Array.Empty<byte>()));
    }
}
