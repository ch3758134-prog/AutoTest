using System.IO;
using System.Security.Cryptography;
using AutoTest.Specialists.Flash;
using AutoTest.FlashLab.Configuration;

namespace AutoTest.FlashLab.Firmware;

public sealed record FirmwareSelection(string FileName,string Format,long Bytes,
    int Segments,ulong Start,ulong EndExclusive,string Sha256,string AddressRanges)
{
    public string Summary => $"{FileName} | {Format} | {Bytes:N0} 字节 / {Segments} 段 | "+
        $"0x{Start:X8}..0x{EndExclusive-1:X8} | SHA256={Sha256}";
}

public static class FirmwareSelectionService
{
    /// <summary>Offline-only, strict image parsing. No flash commands or implicit EEPROM writes.</summary>
    public static FirmwareSelection Load(string filename,string? binBaseAddress,
        IFlashParameterSource configuration)
    {
        var path=Path.GetFullPath(filename);
        var info=new FileInfo(path);
        if(!info.Exists) throw new FileNotFoundException("固件文件不存在。",path);
        if(info.Length<=0 || info.Length>32L*1024*1024)
            throw new FormatException("固件文件必须非空且不超过32MiB原始文件上限。");
        byte[] data=File.ReadAllBytes(path);
        string ext=Path.GetExtension(path).ToLowerInvariant();
        FirmwareImage image=ext switch
        {
            ".hex" or ".h86" => FirmwareImageParser.ParseHex(
                System.Text.Encoding.UTF8.GetString(data).TrimStart('\uFEFF')),
            ".s19" or ".s28" or ".s37" or ".sx" or ".mot" => FirmwareImageParser.ParseS19(
                System.Text.Encoding.UTF8.GetString(data).TrimStart('\uFEFF')),
            ".bin" => FirmwareImageParser.ParseBin(data,
                CanToolXmlProfileReader.ParseHexUInt(binBaseAddress ?? "","BIN 起始地址")),
            _ => throw new NotSupportedException("仅支持 HEX/H86、S19/S28/S37/SX/MOT、BIN。")
        };
        configuration.ValidateConfiguredRange();
        ulong minimum=image.Segments.Min(x=>(ulong)x.Address);
        ulong maximum=image.Segments.Max(x=>x.EndExclusive);
        string startText=configuration.Get("FlashStartAddr"),endText=configuration.Get("FlashEndAddr");
        if(startText.Length>0 && endText.Length>0)
        {
            ulong allowedStart=CanToolXmlProfileReader.ParseHexUInt(startText,"FlashStartAddr");
            ulong allowedEndExclusive=(ulong)CanToolXmlProfileReader.ParseHexUInt(endText,"FlashEndAddr")+1UL;
            if(image.Segments.Any(x=>x.Address<allowedStart || x.EndExclusive>allowedEndExclusive))
                throw new FormatException("镜像地址段超出当前配置的 Flash 起止边界，拒绝继续。");
        }
        string ranges=string.Join("  |  ",image.Segments.Select(x=>
            $"0x{x.Address:X8}..0x{x.EndExclusive-1:X8} ({x.Data.Length:N0}B)"));
        return new(Path.GetFileName(path),image.Format,image.ByteCount,image.Segments.Length,
            minimum,maximum,Convert.ToHexString(SHA256.HashData(data)),ranges);
    }
}
