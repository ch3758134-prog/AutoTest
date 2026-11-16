using System.IO;

namespace AutoTest.FlashLab.Configuration;

/// <summary>
/// Offline CanTool three-file workspace discovery. No USB activity, no
/// algorithm invocation, no firmware writing or guessing among multiple images.
/// </summary>
public sealed record CanToolThreeFileBundle(
    string ConfigurationXml,string FirmwareImage,string ManagedSafetyFile);

public static class CanToolBundleDiscovery
{
    private static readonly HashSet<string> FirmwareExtensions=
        new(StringComparer.OrdinalIgnoreCase)
        {".hex",".h86",".s19",".s28",".s37",".sx",".mot",".bin"};

    public static CanToolThreeFileBundle Discover(string folder)
    {
        if(string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            throw new DirectoryNotFoundException("请选择包含 CanTool 配置、固件和安全文件的目录。");
        var options=new EnumerationOptions
        {
            RecurseSubdirectories=true,
            MaxRecursionDepth=3,
            IgnoreInaccessible=true,
            AttributesToSkip=FileAttributes.ReparsePoint|FileAttributes.System
        };
        // Avoid scanning an accidentally selected entire disk or enormous tree.
        var files=Directory.EnumerateFiles(folder,"*",options).Take(1501).ToArray();
        if(files.Length>1500)
            throw new IOException("所选目录包含过多文件。请选取具体的 ECU 烧录资料文件夹。");
        string xml=One(files.Where(f=>string.Equals(Path.GetFileName(f),
                "Configuration.xml",StringComparison.OrdinalIgnoreCase)),
            "Configuration.xml");
        string firmware=One(files.Where(f=>
                FirmwareExtensions.Contains(Path.GetExtension(f))),
            "固件文件（HEX/S19/BIN）");
        string safety=One(files.Where(f=>string.Equals(Path.GetFileName(f),
                "SafetyFile.dll",StringComparison.OrdinalIgnoreCase)),
            "SafetyFile.dll 安全文件");
        return new(Path.GetFullPath(xml),Path.GetFullPath(firmware),Path.GetFullPath(safety));
    }

    private static string One(IEnumerable<string> items,string name)
    {
        var list=items.OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray();
        if(list.Length==0)
            throw new FileNotFoundException("目录中未找到 "+name+
                "。可以继续使用左侧的单独文件选择入口。");
        if(list.Length>1)
            throw new FormatException("找到多个 "+name+"，无法判断哪份属于目标 ECU："+
                string.Join("；",list.Take(8).Select(Path.GetFileName))+
                "。请选更具体的文件夹，或使用单独文件选择入口。");
        return list[0];
    }
}
