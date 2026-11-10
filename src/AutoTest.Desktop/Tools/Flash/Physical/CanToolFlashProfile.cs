using System.Globalization;
using System.Xml.Linq;

namespace AutoTest.Desktop.Tools.Flash.Physical;

/// <summary>CanTool Configuration.xml compatibility. No default target CAN IDs or addresses:
/// a physical ECU must never be programmed using guessed identifiers.</summary>
public sealed record CanToolFlashProfile(
    uint BootRequestId, uint BootResponseId, uint AppRequestId, uint AppResponseId,
    uint FlashStart, uint FlashEndInclusive, string BaudRate, uint BlockLength,
    int P2TimeoutMs, int P2StarTimeoutMs, int EraseTimeoutMs,
    int FlowControlTimeoutMs, int ForceMinIntervalMs, string? SeedKeyDll)
{
    public static CanToolFlashProfile Load(string path)
    {
        var root = XDocument.Load(path).Root ?? throw new FormatException("Missing XML root.");
        static string? Value(XElement r, params string[] names) =>
            names.Select(x => r.Element(x)?.Value.Trim()).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        static uint Number(XElement r, params string[] names)
        {
            string s = Value(r,names) ?? throw new FormatException("Missing XML setting: " + names[0]);
            return uint.Parse(s.StartsWith("0x",StringComparison.OrdinalIgnoreCase)?s[2..]:s,
                NumberStyles.HexNumber,CultureInfo.InvariantCulture);
        }
        static int Millis(XElement r,int fallback,params string[] names)
        {
            string? s=Value(r,names);
            return s is null ? fallback : int.Parse(s,CultureInfo.InvariantCulture);
        }
        var profile = new CanToolFlashProfile(
            Number(root,"BootloadPhyID"),Number(root,"BootRespond"),
            Number(root,"APPPhyID"),Number(root,"APPRespond"),
            Number(root,"FlashStartAddr","FLASH起始地址","FLASHStartAddr"),
            Number(root,"FlashEndAddr","FLASH结束地址","FLASHEndAddr"),
            Value(root,"BaudRate","波特率") ?? "500K",
            Value(root,"BlockLength") is null ? 0x200u : Number(root,"BlockLength"),
            Millis(root,1000,"P2TimeoutMs"),
            Millis(root,5000,"P2StarTimeoutMs"),
            Millis(root,30000,"EraseTimeoutMs"),
            Millis(root,2000,"FlowControlTimeoutMs"),
            Millis(root,0,"ForceMinIntervalMs","ForceMinInterval"),
            Value(root,"SafetyFilePath","SeedToKeyDll_Path"));
        profile.Validate();
        return profile;
    }
    public void Validate()
    {
        if (BootRequestId==0 || BootResponseId==0 || AppRequestId==0 || AppResponseId==0 ||
            BootRequestId>0x1FFFFFFF || BootResponseId>0x1FFFFFFF ||
            AppRequestId>0x1FFFFFFF || AppResponseId>0x1FFFFFFF ||
            BootRequestId==BootResponseId || AppRequestId==AppResponseId)
            throw new FormatException("CAN IDs missing, out of range or request equals response.");
        if (FlashStart>=FlashEndInclusive || BlockLength==0 || BlockLength>0x100000)
            throw new FormatException("Invalid flash address range or block length.");
        string baud=BaudRate.ToUpperInvariant().Replace("BPS","");
        if (baud is not ("250K" or "500K" or "250000" or "500000"))
            throw new FormatException("Unsupported CAN baud rate.");
        if (P2TimeoutMs<=0 || P2StarTimeoutMs<P2TimeoutMs ||
            EraseTimeoutMs<=0 || FlowControlTimeoutMs<=0 || ForceMinIntervalMs<0)
            throw new FormatException("Invalid diagnostic timeout.");
    }
}
