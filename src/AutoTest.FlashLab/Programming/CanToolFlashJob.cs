using System.Collections.Immutable;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using AutoTest.Contracts;
using AutoTest.Devices.Usb;
using AutoTest.FlashLab.Configuration;
using AutoTest.FlashLab.Firmware;
using AutoTest.Specialists.Flash;

namespace AutoTest.FlashLab.Programming;

public sealed record FlashAlignedBlock(uint Address,ImmutableArray<byte> Bytes)
{
    public ulong EndExclusive=>(ulong)Address+(ulong)Bytes.Length;
    public ushort Checksum=>CanToolProgrammingProtocol.ComplementOfSum(Bytes.AsSpan());
}
public sealed record CanToolFlashJob(
    UdsEndpointProfile App,UdsEndpointProfile Boot,
    uint FlashStart, uint FlashEndInclusive,string ConfiguredBitrate,
    int AlignmentBlockLength,string AlignmentSource,
    ImmutableArray<FlashAlignedBlock> Blocks,
    string FirmwarePath,string FirmwareSha256,string SecurityLibraryPath,
    string SecuritySha256,string TargetLabel,bool LegacyTailSignature,string Fingerprint,
    long SourceBytes,long PlannedBytes)
{
    public string Consent => "AUTHORIZE ECU FLASH "+Fingerprint;
    public ulong EraseLength => (ulong)FlashEndInclusive-FlashStart+1UL;

    public static CanToolFlashJob Freeze(CanToolXmlProfileReader config,
        string firmwarePath,string? binBase,string keyDll,string targetLabel,
        bool legacyTailSignature=false)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.ValidateConfiguredRange();
        if(string.IsNullOrWhiteSpace(targetLabel))
            throw new FormatException("必须填写现场 ECU 型号/版本标识，不能自动采用示例 ECU。");
        var (appTx,appRx)=config.GetEndpoint(false);
        var (bootTx,bootRx)=config.GetEndpoint(true);
        if(appTx==0 || appRx==0 || bootTx==0 || bootRx==0)
            throw new FormatException("APP/Boot 物理 CAN ID 不能为0。");
        var app=Endpoint(appTx,appRx);
        var boot=Endpoint(bootTx,bootRx);
        uint first=CanToolXmlProfileReader.ParseHexUInt(config.Get("FlashStartAddr"),"FLASH起始地址");
        uint last=CanToolXmlProfileReader.ParseHexUInt(config.Get("FlashEndAddr"),"FLASH结束地址");
        if(first>=last)throw new FormatException("Flash 空间必须包含至少两个地址。");
        string baud=config.Get("BaudRate").Trim().ToUpperInvariant();
        if(baud is not ("250K" or "500K" or "250000" or "500000"))
            throw new FormatException("原始 CanTool 波特率仅支持250K或500K；主控实际波特率需另行核对。");
        if(baud is "250K" or "250000")
            throw new NotSupportedException(
                "当前上传的主控固件 source/main.c 将 CAN1 初始化为500K；没有已确认可用的 USB 动态切换命令。禁止按250K配置擦写。");

        string rawBlock=config.Get("BlockLength");
        uint blockLength=string.IsNullOrWhiteSpace(rawBlock) ? 0x200u :
            CanToolXmlProfileReader.ParseHexUInt(rawBlock,"BlockLength");
        if(blockLength is < 64 or > 4096 || (blockLength & (blockLength-1))!=0)
            throw new FormatException("BlockLength 必须是64..4096范围内的2次幂（十六进制）。");
        string blockSource=string.IsNullOrWhiteSpace(rawBlock)?"CanTool 参考默认 0x200":"XML或操作员";

        // Never trust a stale UI validation. Re-read and hash the live files before
        // freezing. Everything material to the on-wire job belongs in Fingerprint.
        var inspected=FirmwareSelectionService.Load(firmwarePath,binBase,config);
        byte[] source=File.ReadAllBytes(firmwarePath);
        if(!Convert.ToHexString(SHA256.HashData(source)).Equals(inspected.Sha256,
            StringComparison.OrdinalIgnoreCase))
            throw new IOException("固件在预检时发生改变。");
        FirmwareImage image=Path.GetExtension(firmwarePath).ToLowerInvariant() switch
        {
            ".hex" or ".h86" => FirmwareImageParser.ParseHex(Encoding.UTF8.GetString(source).TrimStart('\uFEFF')),
            ".s19" or ".s28" or ".s37" or ".sx" or ".mot" =>
                FirmwareImageParser.ParseS19(Encoding.UTF8.GetString(source).TrimStart('\uFEFF')),
            ".bin" => FirmwareImageParser.ParseBin(source,
                CanToolXmlProfileReader.ParseHexUInt(binBase??"","BIN基址")),
            _ => throw new NotSupportedException("不支持的固件类型")
        };

        // Only align occupied blocks. Fill WITHIN each occupied block with FF,
        // as traced to CanTool; never insert its hardcoded 12 34 56 78 signature.
        var prepared=new SortedDictionary<uint,byte[]>();
        foreach(var segment in image.Segments)
        {
            for(int i=0;i<segment.Data.Length;i++)
            {
                ulong addr=(ulong)segment.Address+(ulong)i;
                ulong aligned=(addr/blockLength)*blockLength;
                if(aligned<first || aligned+blockLength>(ulong)last+1UL)
                    throw new FormatException($"块对齐超出 Flash 边界: 0x{aligned:X8}");
                uint key=checked((uint)aligned);
                if(!prepared.TryGetValue(key,out var dst))
                {
                    dst=new byte[blockLength];
                    Array.Fill(dst,(byte)0xFF);
                    prepared.Add(key,dst);
                }
                dst[checked((int)(addr-aligned))]=segment.Data[i];
            }
        }
        if(legacyTailSignature)
        {
            // Original CanTool stamped 12 34 56 78 at EndInclusive-3.
            // This OEM-specific behavior is EXPLICITLY OPT-IN, never automatic.
            if((ulong)last-3UL<first)
                throw new FormatException("参考签名地址超出 Flash 边界。");
            ulong mark=(ulong)last-3UL;
            ulong aligned=(mark/blockLength)*blockLength;
            if(aligned<first || aligned+blockLength>(ulong)last+1UL)
                throw new FormatException("参考签名块对齐超出 Flash 范围。");
            uint key=checked((uint)aligned);
            if(!prepared.TryGetValue(key,out var bytes))
            {
                bytes=new byte[blockLength];
                Array.Fill(bytes,(byte)0xFF);
                prepared.Add(key,bytes);
            }
            byte[] signature=[0x12,0x34,0x56,0x78];
            for(int i=0;i<signature.Length;i++)
                bytes[checked((int)(mark+i-aligned))]=signature[i];
        }
        if(prepared.Count==0)throw new FormatException("镜像没有可烧录的数据块。");
        var blocks=prepared.Select(p=>new FlashAlignedBlock(p.Key,
            ImmutableArray.CreateRange(p.Value))).ToImmutableArray();
        string dll=Path.GetFullPath(keyDll);
        var details=NativeZlgKeyProvider.Inspect(dll);
        if(!details.NativeX64)
            throw new FormatException("需要带有 ZLGKey 导出的原生 x64 DLL；托管 SafetyFile.dll 不能直接使用。");
        string effective=string.Join("\n",config.Parameters.Select(p=>p.Name+"="+p.Value.Trim()));
        string bound=$"CANTOOL-FLASH-F2-v1\nCAN1\n{targetLabel.Trim()}\n"+
            $"APP {appTx:X8} {appRx:X8}\nBOOT {bootTx:X8} {bootRx:X8}\n"+
            $"FLASH {first:X8}-{last:X8} BLOCK {blockLength:X8} BAUD {baud}\n"+
            $"IMAGE {inspected.Sha256}\nDLL {details.Sha256}\nLEGACY_SIGNATURE={legacyTailSignature}\n{effective}\n"+
            string.Join("\n",blocks.Select(x=>$"{x.Address:X8}:{Convert.ToHexString(SHA256.HashData(x.Bytes.AsSpan()))}"));
        string hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(bound)));
        return new(app,boot,first,last,baud,checked((int)blockLength),blockSource,
            blocks,Path.GetFullPath(firmwarePath),inspected.Sha256,dll,details.Sha256,
            targetLabel.Trim(),legacyTailSignature,hash,image.ByteCount,blocks.Sum(x=>(long)x.Bytes.Length));
    }

    private static UdsEndpointProfile Endpoint(uint tx,uint rx)
    {
        var value=new UdsEndpointProfile(BusRole.DutCan,tx,rx,
            TimeSpan.FromSeconds(2),TimeSpan.FromSeconds(5),"Normal");
        AutoTest.Specialists.Uds.UdsEndpointProfileValidator.ValidateAddressing(value);
        return value;
    }
}
