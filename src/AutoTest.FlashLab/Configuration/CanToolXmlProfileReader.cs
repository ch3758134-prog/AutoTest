using System.IO;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace AutoTest.FlashLab.Configuration;

/// <summary>Original CanTool XML field names, kept separate from AutoTest live hardware state.
/// Empty is UNKNOWN, never silently populated from a reference ECU sample.</summary>
public sealed class CanToolParameter(string name, string description) : INotifyPropertyChanged
{
    private string _value = "", _source = "未设置";
    public string Name { get; } = name;
    public string Description { get; } = description;
    public string Value
    {
        get => _value;
        set { if (_value==value) return; _value=value ?? ""; _source="操作员修改";
            PropertyChanged?.Invoke(this,new(nameof(Value)));
            PropertyChanged?.Invoke(this,new(nameof(Source))); }
    }
    public string Source { get => _source; private set
    { _source=value; PropertyChanged?.Invoke(this,new(nameof(Source))); } }
    public void Restore(string value,string source)
    { _value=value; _source=source; PropertyChanged?.Invoke(this,new(nameof(Value)));PropertyChanged?.Invoke(this,new(nameof(Source))); }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class CanToolXmlProfileReader
{
    private readonly XDocument? _document;
    private static readonly (string Name,string Description)[] Known =
    [
        ("BootloadPhyID","Boot 请求 CAN ID（16进制）"),
        ("BootRespond","Boot 应答 CAN ID（16进制）"),
        ("APPPhyID","APP 请求 CAN ID（16进制）"),
        ("APPRespond","APP 应答 CAN ID（16进制）"),
        ("VersionID","版本信息参考 CAN ID（未绑定）"),
        ("DiagnosticsID","诊断信息参考 CAN ID（未绑定）"),
        ("FlashStartAddr","参考 Flash 起始地址（含）"),
        ("FlashEndAddr","参考 Flash 结束地址（含）"),
        ("BaudRate","参考波特率；不能据此确认主控真实速率"),
        ("BlockLength","参考块长（不等于0x74与ISO-TP BS）"),
        ("SafetyFilePath","安全文件 SafetyFile.dll：独立的托管算法配置路径"),
        ("SeedToKeyDll_Path","Seed/Key 原生 ZLGKey 算法 DLL：独立配置路径"),
        ("ForceMinInterval","CanTool CF 参考间隔；是否应用须依据 ECU FC")
    ];

    // Physical CanTool Configuration.xml uses these Chinese names, not the English UI keys.
    // Do not invent controller baud-rate support: mapping only imports reference data.
    private static readonly IReadOnlyDictionary<string, string[]> XmlAliases =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["FlashStartAddr"] = ["FLASH起始地址"],
            ["FlashEndAddr"] = ["FLASH结束地址"],
            ["BaudRate"] = ["波特率"]
        };

    public List<CanToolParameter> Parameters { get; } =
        Known.Select(x=>new CanToolParameter(x.Name,x.Description)).ToList();
    public string? SourcePath { get; private set; }
    public IReadOnlyList<string> UnmappedNames { get; private set; } = [];
    public IReadOnlyList<string> UnmappedEntries { get; private set; } = [];
    public CanToolXmlProfileReader() { }
    private CanToolXmlProfileReader(XDocument document,string source)
    { _document=document; SourcePath=source; LoadKnown(); }

    private static bool Matches(XElement element,string name) =>
        string.Equals(element.Name.LocalName,name,StringComparison.OrdinalIgnoreCase) ||
        string.Equals((string?)element.Attribute("name"),name,StringComparison.OrdinalIgnoreCase) ||
        string.Equals((string?)element.Attribute("Name"),name,StringComparison.OrdinalIgnoreCase);

    private static XElement? Find(XContainer doc,string name)
    {
        // Explicit aliases only. A conflicting canonical + localized pair is ambiguous:
        // fail closed rather than silently using whichever element occurs first.
        var names = XmlAliases.TryGetValue(name,out var aliases)
            ? new[] { name }.Concat(aliases).ToArray()
            : new[] { name };
        var matches=doc.Descendants().Where(e=>names.Any(n=>Matches(e,n))).ToArray();
        if(matches.Length>1)
            throw new FormatException($"配置项 {name} 存在多个英文/中文重复定义，无法确定有效值。");
        return matches.FirstOrDefault();
    }

    private static string RelativePath(XElement element) =>
        string.Join("/",element.AncestorsAndSelf().Reverse()
            .Skip(1).Select(e=>e.Name.LocalName));

    private void LoadKnown()
    {
        if (_document is null) return;
        var mapped=new HashSet<XElement>();
        foreach(var item in Parameters)
        {
            var e=Find(_document,item.Name);
            if(e is not null)
            {
                string source=string.Equals(e.Name.LocalName,item.Name,StringComparison.OrdinalIgnoreCase)
                    ? "CanTool XML" : "XML："+e.Name.LocalName;
                item.Restore(e.Value.Trim(),source);
                mapped.Add(e);
            }
        }
        // Keep nested feature fields distinctly named: BootFeature/item and
        // APPFeature/item must not collapse into a misleading single "item".
        var unrecognized=_document.Descendants().Where(e=>!e.HasElements && !mapped.Contains(e))
            .Select(e=>new {Path=RelativePath(e),Value=e.Value.Trim()}).ToArray();
        UnmappedNames=unrecognized.Select(x=>x.Path).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray();
        UnmappedEntries=unrecognized.Select(x=>x.Path+" = "+x.Value).ToArray();
    }

    public static CanToolXmlProfileReader Load(string path)
    {
        using var reader=XmlReader.Create(path,new XmlReaderSettings { DtdProcessing=DtdProcessing.Prohibit,
            XmlResolver=null, MaxCharactersInDocument=2_000_000 });
        var document=XDocument.Load(reader,LoadOptions.PreserveWhitespace);
        if(document.Root is null) throw new FormatException("CanTool XML 没有根元素。");
        return new(document,Path.GetFullPath(path));
    }
    public string Get(string name) => Parameters.First(p=>p.Name==name).Value.Trim();
    public void Set(string name,string value) => Parameters.First(p=>p.Name==name).Value=value;

    public void LoadRelatedJson(string path)
    {
        using var json=JsonDocument.Parse(File.ReadAllBytes(path),new JsonDocumentOptions
        { CommentHandling=JsonCommentHandling.Skip, AllowTrailingCommas=true, MaxDepth=32 });
        if(json.RootElement.ValueKind!=JsonValueKind.Object)
            throw new FormatException("settings.json 必须是 JSON 对象。");
        foreach(var item in Parameters)
        {
            // Preserve XML fields; settings.json cannot silently override a verified XML input.
            if (!string.IsNullOrWhiteSpace(item.Value)) continue;
            if(json.RootElement.TryGetProperty(item.Name,out var v) && v.ValueKind==JsonValueKind.String)
                item.Restore(v.GetString() ?? "","CanTool JSON");
        }
    }

    public void SaveAs(string path)
    {
        var doc=_document is null ? new XDocument(new XElement("Configuration")) :
            new XDocument(_document);
        var root=doc.Root!;
        foreach(var field in Parameters)
        {
            var existing=Find(doc,field.Name);
            if(existing is null)
            {
                if(string.IsNullOrWhiteSpace(field.Value)) continue;
                root.Add(new XElement(field.Name,field.Value.Trim()));
            }
            else if(field.Source=="操作员修改") existing.Value=field.Value.Trim();
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var writer=XmlWriter.Create(path,new XmlWriterSettings
        { Indent=true, Encoding=new System.Text.UTF8Encoding(false) });
        doc.Save(writer);
    }

    public static uint ParseHexUInt(string text,string label)
    {
        if(string.IsNullOrWhiteSpace(text)) throw new FormatException(label+" 尚未配置。");
        string s=text.Trim();
        if(s.StartsWith("0x",StringComparison.OrdinalIgnoreCase)) s=s[2..];
        if(!uint.TryParse(s,NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,out var n))
            throw new FormatException(label+" 必须是 32 位十六进制数。");
        return n;
    }
    public static ushort ParseDid(string text)
    {
        uint v=ParseHexUInt(text,"DID");
        if(v>ushort.MaxValue) throw new FormatException("DID 超出 16 位。");
        return (ushort)v;
    }
    public void ValidateConfiguredRange()
    {
        string start=Get("FlashStartAddr"),end=Get("FlashEndAddr");
        if(start.Length==0 && end.Length==0) return;
        if(start.Length==0 || end.Length==0)
            throw new FormatException("FlashStartAddr 和 FlashEndAddr 必须成对提供。");
        if(ParseHexUInt(start,"FlashStartAddr")>ParseHexUInt(end,"FlashEndAddr"))
            throw new FormatException("Flash 起点不能晚于终点。");
    }
    public (uint Request,uint Response) GetEndpoint(bool boot)
    {
        var tx=ParseHexUInt(Get(boot?"BootloadPhyID":"APPPhyID"),boot?"BootloadPhyID":"APPPhyID");
        var rx=ParseHexUInt(Get(boot?"BootRespond":"APPRespond"),boot?"BootRespond":"APPRespond");
        if(tx==rx || tx>0x1FFFFFFF || rx>0x1FFFFFFF || (tx>0x7FF)!=(rx>0x7FF))
            throw new FormatException("请求/响应 CAN ID 无效或混用11位与29位。");
        return(tx,rx);
    }
}
