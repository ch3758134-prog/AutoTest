using AutoTest.Contracts;
namespace AutoTest.Devices.Station;
public static class KeypadCommandCodec
{
    public const uint KeypadId=0x730;
    public static CanFrame EncodeMask(uint mask)
    {
        if(mask>0xFFFFF) throw new ArgumentOutOfRangeException(nameof(mask),"Only 20 keys are supported.");
        return CanFrame.Create(BusRole.KeypadCan,false,KeypadId,
            new byte[]{0x01,(byte)mask,(byte)(mask>>8),(byte)((mask>>16)&0x0F),0,0,0,0});
    }
    public static CanFrame EncodeReleaseAll() => CanFrame.Create(BusRole.KeypadCan,false,KeypadId,
        new byte[]{0x80,0,0,0,0,0,0,0});
}
