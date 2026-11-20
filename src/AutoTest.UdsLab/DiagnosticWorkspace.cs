using System.IO;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using AutoTest.Specialists.Uds.Workbench;

namespace AutoTest.UdsLab;

/// <summary>One user-editable, reusable diagnostic operation. Not a TEST StepNode.</summary>
public sealed class DiagnosticAction : INotifyPropertyChanged
{
    private string _name = "新诊断操作";
    private string _group = "未分类";

    public event PropertyChangedEventHandler? PropertyChanged;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name
    {
        get => _name;
        set
        {
            if (_name == value) return;
            _name = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        }
    }
    public string Group
    {
        get => _group;
        set
        {
            if (_group == value) return;
            _group = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Group)));
        }
    }
    public string RequestHex { get; set; } = "";
    public string ExpectedPrefixHex { get; set; } = "";
    // Human-facing service editor state. Optional for backward compatibility with L1 JSON.
    public string ServiceSidHex { get; set; } = "";
    public string PrimaryHex { get; set; } = "";
    public string AuxiliaryHex { get; set; } = "";
    public bool RawMode { get; set; } = false;
    public string Decoder { get; set; } = "Hex";
    public int DataOffset { get; set; } = 3;
    public double Scale { get; set; } = 1;
    public string Unit { get; set; } = "";

    public DiagnosticAction Duplicate() => new()
    {
        Id = Guid.NewGuid(),
        Name = Name + " · 副本", Group = Group, RequestHex = RequestHex,
        ExpectedPrefixHex = ExpectedPrefixHex, ServiceSidHex = ServiceSidHex,
        PrimaryHex = PrimaryHex, AuxiliaryHex = AuxiliaryHex, RawMode = RawMode, Decoder = Decoder,
        DataOffset = DataOffset, Scale = Scale, Unit = Unit
    };
}

public sealed class DiagnosticWorkspaceDocument
{
    public int Schema { get; set; } = 2;
    public string TxIdHex { get; set; } = "";
    public string RxIdHex { get; set; } = "";
    public int P2Ms { get; set; } = 1000;
    public int P2StarMs { get; set; } = 5000;
    public List<DiagnosticAction> Actions { get; set; } = [];
    // Multi-service diagnosis cases. Old v1 Actions remain importable, so
    // historical user-authored requests are not silently discarded.
    public List<DiagnosticCase> Cases { get; set; } = [];
    // Ordered, user-curated manual-interaction queue (not a TEST program).
    public List<Guid> ManualSequence { get; set; } = [];
    public int PollIntervalMs { get; set; } = 2000;
}

public sealed record AuditRecord(DateTimeOffset Utc, string Kind, string Detail);

public static class DiagnosticWorkspace
{
    private const int MaxBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static async Task SaveAsync(string path, DiagnosticWorkspaceDocument document)
    {
        // New multi-service cases require schema 2. Earlier L1/L4 tools
        // reject this version instead of silently overwriting Cases.
        document.Schema = 2;
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
        if (doc.Schema is not (1 or 2) || doc.Actions is null || doc.Actions.Count > 500)
            throw new InvalidDataException("工作台版本不支持或操作数量超过 500。");
        if (doc.PollIntervalMs is < 1000 or > 60000)
            throw new InvalidDataException("连续读取间隔必须为 1000..60000 ms。");
        if (doc.ManualSequence is null || doc.ManualSequence.Count > 100)
            throw new InvalidDataException("交互清单最多 100 项。");
        var actionIds = new HashSet<Guid>();
        foreach (var action in doc.Actions)
        {
            if (action is null || action.Id == Guid.Empty || !actionIds.Add(action.Id) ||
                string.IsNullOrWhiteSpace(action.Name) ||
                action.Name.Length > 200 || action.Group.Length > 200 ||
                action.DataOffset is < 0 or > 4095 ||
                action.ServiceSidHex.Length > 2 || action.PrimaryHex.Length > 8190 ||
                action.AuxiliaryHex.Length > 8190 ||
                !double.IsFinite(action.Scale) || Math.Abs(action.Scale) > 1e12 ||
                action.Decoder is not ("Hex" or "Ascii" or "UInt16BE" or "UInt16LE" or "UInt8" or "Dtc"))
                throw new InvalidDataException("存在无效的诊断操作参数。");
            if (!string.IsNullOrWhiteSpace(action.RequestHex)) _ = UdsHex.Parse(action.RequestHex);
            if (!string.IsNullOrWhiteSpace(action.ExpectedPrefixHex)) _ = UdsHex.Parse(action.ExpectedPrefixHex);
        }
        if (doc.ManualSequence.Any(id => !actionIds.Contains(id)))
            throw new InvalidDataException("交互清单引用了不存在的诊断操作。");
        if (doc.Cases is null || doc.Cases.Count > 100)
            throw new InvalidDataException("诊断用例不能超过 100 个。");
        var caseIds = new HashSet<Guid>();
        var stepIds = new HashSet<Guid>();
        int totalSteps = 0;
        foreach (var diagnosticCase in doc.Cases)
        {
            if (diagnosticCase is null || diagnosticCase.Id == Guid.Empty ||
                !caseIds.Add(diagnosticCase.Id) ||
                string.IsNullOrWhiteSpace(diagnosticCase.Name) ||
                diagnosticCase.Name.Length > 200 ||
                diagnosticCase.Description.Length > 3000 ||
                diagnosticCase.Revision < 1 ||
                diagnosticCase.Steps is null || diagnosticCase.Steps.Count > 150)
                throw new InvalidDataException("诊断用例名称、版本或步骤数量无效。");
            totalSteps += diagnosticCase.Steps.Count;
            if (totalSteps > 1000)
                throw new InvalidDataException("整个诊断配置不能超过 1000 个步骤。");
            foreach (var step in diagnosticCase.Steps)
            {
                if (step is null || !stepIds.Add(step.Id))
                    throw new InvalidDataException("诊断步骤 ID 重复或为空。");
                DiagnosticCaseVariables.Validate(step);
                var action = step.Action;
                if (action.Id == Guid.Empty || action.Name.Length > 200 ||
                    action.Group.Length > 200 || action.DataOffset is < 0 or > 4095 ||
                    action.ServiceSidHex.Length > 2 || action.PrimaryHex.Length > 8190 ||
                    action.AuxiliaryHex.Length > 8190 ||
                    !double.IsFinite(action.Scale) || Math.Abs(action.Scale) > 1e12 ||
                    action.Decoder is not ("Hex" or "Ascii" or "UInt16BE" or "UInt16LE" or "UInt8" or "Dtc"))
                    throw new InvalidDataException("诊断步骤参数不合法。");
                if (!string.IsNullOrWhiteSpace(action.RequestHex))
                    _ = UdsHex.Parse(action.RequestHex);
                if (!string.IsNullOrWhiteSpace(action.ExpectedPrefixHex))
                    _ = UdsHex.Parse(action.ExpectedPrefixHex);
            }
        }
    }

    public static string Decode(DiagnosticAction action, byte[] response)
    {
        if (action.DataOffset > response.Length) return "数据偏移超出响应长度，无法解析。";
        var data = response.AsSpan(action.DataOffset);
        return action.Decoder switch
        {
            "Dtc" => DecodeDtc(response),
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

    private static string DecodeDtc(ReadOnlySpan<byte> pdu)
    {
        if (pdu.Length < 3 || pdu[0] != 0x59 || pdu[1] != 0x02)
            return "不是可解析的 ReadDTCInformation 0x19/02 正响应；请查看原始报文。";
        int dataLength = pdu.Length - 3;
        if (dataLength % 4 != 0)
            return "0x19/02 DTC 记录长度异常；不能按完整故障码解释。";
        var statusAvailable = pdu[2];
        var lines = new List<string>
        {
            "ECU DTC 状态可用掩码：0x" + statusAvailable.ToString("X2"),
            "报告 DTC 数：" + (dataLength / 4)
        };
        for (int off = 3; off < pdu.Length; off += 4)
        {
            string dtc = "0x" + Convert.ToHexString(pdu.Slice(off, 3));
            byte status = pdu[off + 3];
            string[] bits = ["testFailed", "testFailedThisOperationCycle", "pendingDTC",
                "confirmedDTC", "testNotCompletedSinceLastClear", "testFailedSinceLastClear",
                "testNotCompletedThisOperationCycle", "warningIndicatorRequested"];
            var enabled = Enumerable.Range(0, 8).Where(bit => (status & (1 << bit)) != 0)
                .Select(bit => bits[bit]).ToArray();
            lines.Add(dtc + "  状态=0x" + status.ToString("X2") + "  " +
                (enabled.Length == 0 ? "无置位状态" : string.Join(" | ", enabled)));
        }
        return string.Join(Environment.NewLine, lines);
    }
}
