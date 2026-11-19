using System.IO;
using System.Globalization;
using System.Text;
using System.Text.Json;
using AutoTest.Specialists.Uds.Workbench;

namespace AutoTest.UdsLab;

/// <summary>One user-editable, reusable diagnostic operation. Not a TEST StepNode.</summary>
public sealed class DiagnosticAction
{
    public string Name { get; set; } = "新诊断操作";
    public string Group { get; set; } = "未分类";
    public string RequestHex { get; set; } = "";
    public string ExpectedPrefixHex { get; set; } = "";
    public string Decoder { get; set; } = "Hex";
    public int DataOffset { get; set; } = 3;
    public double Scale { get; set; } = 1;
    public string Unit { get; set; } = "";

    public DiagnosticAction Duplicate() => new()
    {
        Name = Name + " · 副本", Group = Group, RequestHex = RequestHex,
        ExpectedPrefixHex = ExpectedPrefixHex, Decoder = Decoder,
        DataOffset = DataOffset, Scale = Scale, Unit = Unit
    };
}

public sealed class DiagnosticWorkspaceDocument
{
    public int Schema { get; set; } = 1;
    public string TxIdHex { get; set; } = "";
    public string RxIdHex { get; set; } = "";
    public int P2Ms { get; set; } = 1000;
    public int P2StarMs { get; set; } = 5000;
    public List<DiagnosticAction> Actions { get; set; } = [];
}

public sealed record AuditRecord(DateTimeOffset Utc, string Kind, string Detail);

public static class DiagnosticWorkspace
{
    private const int MaxBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static async Task SaveAsync(string path, DiagnosticWorkspaceDocument document)
    {
        Validate(document);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, Json);
        if (bytes.Length > MaxBytes) throw new InvalidDataException("诊断工作台文件超过 1 MiB。");
        string full = Path.GetFullPath(path), tmp = full + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllBytesAsync(tmp, bytes);
            File.Move(tmp, full, true);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }

    public static async Task<DiagnosticWorkspaceDocument> LoadAsync(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length > MaxBytes) throw new InvalidDataException("配置不存在或超过大小限制。");
        var document = JsonSerializer.Deserialize<DiagnosticWorkspaceDocument>(await File.ReadAllBytesAsync(path), Json)
            ?? throw new InvalidDataException("诊断文件为空。");
        Validate(document);
        return document;
    }

    public static void Validate(DiagnosticWorkspaceDocument doc)
    {
        if (doc.Schema != 1 || doc.Actions is null || doc.Actions.Count > 500)
            throw new InvalidDataException("工作台版本不支持或操作数量超过 500。");
        foreach (var action in doc.Actions)
        {
            if (action is null || string.IsNullOrWhiteSpace(action.Name) ||
                action.Name.Length > 200 || action.Group.Length > 200 ||
                action.DataOffset is < 0 or > 4095 ||
                !double.IsFinite(action.Scale) || Math.Abs(action.Scale) > 1e12 ||
                action.Decoder is not ("Hex" or "Ascii" or "UInt16BE" or "UInt16LE" or "UInt8"))
                throw new InvalidDataException("存在无效的诊断操作参数。");
            if (!string.IsNullOrWhiteSpace(action.RequestHex)) _ = UdsHex.Parse(action.RequestHex);
            if (!string.IsNullOrWhiteSpace(action.ExpectedPrefixHex)) _ = UdsHex.Parse(action.ExpectedPrefixHex);
        }
    }

    public static string Decode(DiagnosticAction action, byte[] response)
    {
        if (action.DataOffset > response.Length) return "数据偏移超出响应长度，无法解析。";
        var data = response.AsSpan(action.DataOffset);
        return action.Decoder switch
        {
            "Ascii" => Encoding.ASCII.GetString(data).TrimEnd('\0', ' ') ,
            "UInt16BE" when data.Length >= 2 =>
                (System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(data) * action.Scale)
                    .ToString("G", CultureInfo.InvariantCulture) + " " + action.Unit,
            "UInt16LE" when data.Length >= 2 =>
                (System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(data) * action.Scale)
                    .ToString("G", CultureInfo.InvariantCulture) + " " + action.Unit,
            "UInt8" when data.Length >= 1 =>
                (data[0] * action.Scale).ToString("G", CultureInfo.InvariantCulture) + " " + action.Unit,
            "Hex" => UdsHex.Format(data.ToArray()),
            _ => "响应数据不足，或解码类型不支持。"
        };
    }
}
