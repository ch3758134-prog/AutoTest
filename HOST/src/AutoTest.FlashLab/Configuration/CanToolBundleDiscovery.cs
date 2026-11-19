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

    /// <summary>
    /// Resolve an already-provided CanTool safety asset from the loaded
    /// configuration/firmware workspace. Read-only, no recursion, no DLL load.
    /// In particular, Cfg/Configuration.xml -> Cfg/SafetyFile.dll works.
    /// </summary>
    public static string? FindSafetyFileNear(string? configFile,string? firmwareFile)
    {
        var bases=new List<string>();
        void AddLocation(string? file)
        {
            if(string.IsNullOrWhiteSpace(file) || !Path.IsPathRooted(file))return;
            string full=Path.GetFullPath(file);
            string? dir=Path.GetDirectoryName(full);
            if(dir is not null && Directory.Exists(dir) &&
                !bases.Contains(dir,StringComparer.OrdinalIgnoreCase))
                bases.Add(dir);
        }
        // XML has priority: original CanTool stores SafetyFile in Cfg.
        AddLocation(configFile);
        AddLocation(firmwareFile);
        foreach(var folder in bases)
        {
            // Do not traverse arbitrary directories or look at unrelated DLLs.
            foreach(var candidate in new[]
            {
                Path.Combine(folder,"SafetyFile.dll"),
                Path.Combine(folder,"Cfg","SafetyFile.dll"),
                Path.Combine(folder,"cfg","SafetyFile.dll")
            })
            {
                if(File.Exists(candidate))return Path.GetFullPath(candidate);
            }
        }
        return null;
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
