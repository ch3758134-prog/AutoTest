using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EPB.Core.Communication;

public sealed class CanDatabase
{
    public string Name { get; set; } = "通信数据库";
    public string SourcePath { get; set; } = "";
    public List<CanMessageDefinition> Messages { get; set; } = [];

    /// <summary>通信数据库的唯一查找入口。广播和解析都必须通过同一条定义。</summary>
    public CanMessageDefinition? FindMessage(uint id, bool extended) =>
        Messages.FirstOrDefault(x => x.Id == (id & 0x1FFFFFFFu) && x.Extended == extended);

    public CanMessageDefinition GetOrAddMessage(uint id, bool extended, string? name = null)
    {
        var normalized = id & 0x1FFFFFFFu;
        var message = FindMessage(normalized, extended);
        if (message is not null) return message;
        message = new CanMessageDefinition
        {
            Id = normalized,
            Extended = extended,
            Name = string.IsNullOrWhiteSpace(name) ? $"报文 0x{normalized:X}" : name.Trim()
        };
        Messages.Add(message);
        return message;
    }

    /// <summary>用于保存前和导入后的确定性整理，避免重复报文定义导致引用漂移。</summary>
    public IReadOnlyList<string> Normalize()
    {
        var diagnostics = new List<string>();
        Messages ??= [];
        Messages = Messages.Where(message => message is not null).ToList();
        foreach (var message in Messages)
        {
            message.Id &= 0x1FFFFFFFu;
            message.DefinitionId = string.IsNullOrWhiteSpace(message.DefinitionId)
                ? StableMessageId(message.Id, message.Extended)
                : message.DefinitionId.Trim();
            message.Dlc = Math.Clamp(message.Dlc, 0, 64);
            message.Name = string.IsNullOrWhiteSpace(message.Name) ? $"报文 0x{message.Id:X}" : message.Name.Trim();
            message.DisplayName ??= "";
            message.Description ??= "";
            message.Comment ??= "";
            message.Signals ??= [];
            foreach (var signal in message.Signals)
            {
                signal.Name = signal.Name?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(signal.Name))
                    diagnostics.Add($"报文 0x{message.Id:X} 存在名称为空的信号；别名不能替代名称。");
                if (string.IsNullOrWhiteSpace(signal.DefinitionId) && !string.IsNullOrWhiteSpace(signal.Name))
                    signal.DefinitionId = StableSignalId(message.DefinitionId, signal.Name);
                else
                    signal.DefinitionId = signal.DefinitionId?.Trim() ?? "";
                signal.DisplayName ??= "";
                signal.BitLength = Math.Clamp(signal.BitLength, 1, 64);
                signal.StartBit = Math.Max(0, signal.StartBit);
                signal.Factor = double.IsFinite(signal.Factor) ? signal.Factor : 1;
                signal.Offset = double.IsFinite(signal.Offset) ? signal.Offset : 0;
            }
        }
        var duplicateMessages = Messages.GroupBy(x => (x.Id, x.Extended)).Where(x => x.Count() > 1);
        foreach (var group in duplicateMessages) diagnostics.Add($"报文 0x{group.Key.Id:X} 存在重复定义（{(group.Key.Extended ? "扩展帧" : "标准帧")}）。");
        return diagnostics;
    }

    public IEnumerable<(CanMessageDefinition Message, CanSignalDefinition Signal)> EnumerateSignals() =>
        Messages.SelectMany(message => message.Signals.Select(signal => (message, signal)));

    public IReadOnlyList<string> ValidateSignalNames() =>
        Messages.SelectMany(message => message.Signals.Select(signal => (message, signal)))
            .Where(x => string.IsNullOrWhiteSpace(x.signal.Name))
            .Select(x => $"报文 0x{x.message.Id:X} 的信号名称不能为空；别名不能替代名称。")
            .ToList();

    public static string StableMessageId(uint id, bool extended) => $"message:{(extended ? "x" : "s")}:{id:X8}";
    public static string StableSignalId(string messageDefinitionId, string name) => $"signal:{messageDefinitionId}:{name.Trim()}";
}

public sealed class CanMessageDefinition
{
    /// <summary>项目内报文定义的稳定标识；重新导入时由定义身份生成，不使用显示名称。</summary>
    public string DefinitionId { get; set; } = "";
    public string Name { get; set; } = "未命名报文";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public string Comment { get; set; } = "";
    public uint Id { get; set; }
    public bool Extended { get; set; }
    public int Dlc { get; set; } = 8;
    public int? CycleTimeMs { get; set; }
    public string SendType { get; set; } = "";
    public List<CanSignalDefinition> Signals { get; set; } = [];
}

public sealed class CanSignalDefinition
{
    /// <summary>报文内信号定义的稳定标识；与所属报文 DefinitionId 一起构成引用身份。</summary>
    public string DefinitionId { get; set; } = "";
    public string Name { get; set; } = "";
    // 解析数据库中的展示与诊断元数据。它们与位定义同属一个信号，
    // 不再另存一份运行时解析数据库。
    public string DisplayName { get; set; } = "";
    public string DisplayMode { get; set; } = "数值";
    public string AlarmType { get; set; } = "";
    public string Description { get; set; } = "";
    public int StartBit { get; set; }
    public int BitLength { get; set; } = 1;
    public CanByteOrder ByteOrder { get; set; } = CanByteOrder.Intel;
    public bool Signed { get; set; }
    public double Factor { get; set; } = 1;
    public double Offset { get; set; }
    public double? Minimum { get; set; }
    public double? Maximum { get; set; }
    public string Unit { get; set; } = "";
    public string Comment { get; set; } = "";
    public Dictionary<long, string> ValueTable { get; set; } = [];

    // 通信数据库和报文解析统一使用“起始字节 + 字节内起始位”展示。
    // 旧配置仍以 StartBit 保存绝对位号，这两个属性只是编辑/显示投影。
    [JsonIgnore]
    public int StartByte
    {
        get => Math.Max(0, StartBit) / 8;
        set => StartBit = Math.Max(0, value) * 8 + StartBitInByte;
    }

    [JsonIgnore]
    public int StartBitInByte
    {
        get => Math.Max(0, StartBit) % 8;
        set => StartBit = StartByte * 8 + Math.Clamp(value, 0, 7);
    }

    [JsonIgnore]
    public string ByteOrderDisplay
    {
        get => ByteOrder.ToChinese();
        set => ByteOrder = CanByteOrderDisplay.FromChinese(value);
    }
}

public enum CanByteOrder { Intel, Motorola }

public static class CanByteOrderDisplay
{
    public static string ToChinese(this CanByteOrder order) => order == CanByteOrder.Motorola ? "大端序" : "小端序";
    public static CanByteOrder FromChinese(string? text) =>
        text?.Contains("大", StringComparison.Ordinal) == true || text?.Contains("Motorola", StringComparison.OrdinalIgnoreCase) == true
            ? CanByteOrder.Motorola : CanByteOrder.Intel;
}

public sealed class ParseDefinitionSet
{
    public string Name { get; set; } = "报文解析定义";
    public string SourcePath { get; set; } = "";
    public List<ParseSignalDefinition> Signals { get; set; } = [];
}

public sealed class ParseSignalDefinition
{
    [JsonIgnore]
    public string MessageName { get; set; } = "";
    [JsonIgnore]
    public string MessageDefinitionId { get; set; } = "";
    [JsonIgnore]
    public string SignalDefinitionId { get; set; } = "";
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public uint Id { get; set; }
    public bool Extended { get; set; }
    public int StartByte { get; set; }
    public int StartBit { get; set; }
    public int BitLength { get; set; } = 8;
    public string ByteOrder { get; set; } = "Intel";
    [JsonIgnore]
    public string ByteOrderDisplay
    {
        get => CanByteOrderDisplay.FromChinese(ByteOrder).ToChinese();
        set => ByteOrder = CanByteOrderDisplay.FromChinese(value) == CanByteOrder.Motorola ? "Motorola" : "Intel";
    }
    public double Factor { get; set; } = 1;
    public double Offset { get; set; }
    public string Unit { get; set; } = "";
    public string DisplayMode { get; set; } = "数值";
    public string AlarmType { get; set; } = "";
    public string Description { get; set; } = "";
}

public sealed record DbcImportResult(CanDatabase Database, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);

public static class DbcParser
{
    public static DbcImportResult Parse(string path)
    {
        var db = new CanDatabase { Name = Path.GetFileNameWithoutExtension(path), SourcePath = Path.GetFullPath(path) };
        var errors = new List<string>(); var warnings = new List<string>();
        var byId = new Dictionary<uint, CanMessageDefinition>();
        CanSignalDefinition? currentSignal = null; CanMessageDefinition? currentMessage = null;
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim(); if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal)) continue;
            try
            {
                if (line.StartsWith("BO_ ", StringComparison.Ordinal))
                {
                    var colon = line.IndexOf(':'); var head = colon >= 0 ? line[4..colon].Trim() : line[4..].Trim();
                    var parts = head.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2 || !uint.TryParse(parts[0], out var id)) { errors.Add($"第{i + 1}行：报文定义无法解析。"); continue; }
                    var name = parts[1]; var dlc = 8;
                    if (colon >= 0) { var tail = line[(colon + 1)..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries); if (tail.Length > 0) int.TryParse(tail[0], out dlc); }
                    currentMessage = new CanMessageDefinition { Id = id & 0x1FFFFFFFu, Extended = id > 0x7FF, Name = name, Dlc = dlc <= 0 ? 8 : dlc };
                    db.Messages.Add(currentMessage); byId[currentMessage.Id] = currentMessage; continue;
                }
                if (line.StartsWith("SG_ ", StringComparison.Ordinal) && currentMessage is not null)
                {
                    var colon = line.IndexOf(':'); if (colon < 0) continue; var left = line[4..colon].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries); var right = line[(colon + 1)..].Trim();
                    var bits = right.Split(' ', StringSplitOptions.RemoveEmptyEntries); if (left.Length == 0 || bits.Length == 0) continue;
                    var pos = bits[0]; var pipe = pos.IndexOf('|'); var at = pos.IndexOf('@'); if (pipe < 0 || at < 0) continue;
                    var start = int.Parse(pos[..pipe], CultureInfo.InvariantCulture); var len = int.Parse(pos[(pipe + 1)..at], CultureInfo.InvariantCulture);
                    var order = pos[at + 1] == '0' ? CanByteOrder.Motorola : CanByteOrder.Intel; var signed = pos.Length > at + 2 && pos[at + 2] == '-';
                    var sig = new CanSignalDefinition { Name = left[0], StartBit = start, BitLength = len, ByteOrder = order, Signed = signed };
                    var tupleStart = right.IndexOf('('); var tupleEnd = right.IndexOf(')', tupleStart + 1); if (tupleStart >= 0 && tupleEnd > tupleStart) { var tuple = right[(tupleStart + 1)..tupleEnd].Split(','); if (tuple.Length > 0 && double.TryParse(tuple[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var factor)) sig.Factor = factor; if (tuple.Length > 1 && double.TryParse(tuple[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var offset)) sig.Offset = offset; }
                    var rangeStart = right.IndexOf('[', tupleEnd + 1); var rangeEnd = right.IndexOf(']', rangeStart + 1); if (rangeStart >= 0 && rangeEnd > rangeStart) { var range = right[(rangeStart + 1)..rangeEnd].Split('|'); if (range.Length == 2) { if (double.TryParse(range[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var min)) sig.Minimum = min; if (double.TryParse(range[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var max)) sig.Maximum = max; } }
                    var quotes = right.IndexOf('"', rangeEnd + 1); if (quotes >= 0) { var end = right.IndexOf('"', quotes + 1); if (end > quotes) sig.Unit = right[(quotes + 1)..end]; }
                    currentMessage.Signals.Add(sig); currentSignal = sig; continue;
                }
                if (line.StartsWith("VAL_ ", StringComparison.Ordinal))
                {
                    var rest = line[5..].Trim().TrimEnd(';'); var parts = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries); if (parts.Length < 4 || !uint.TryParse(parts[0], out var id)) continue; var message = byId.GetValueOrDefault(id & 0x1FFFFFFF); var signal = message?.Signals.FirstOrDefault(s => s.Name == parts[1]); if (signal is null) continue; for (var p = 2; p + 1 < parts.Length; p += 2) if (long.TryParse(parts[p], out var value)) signal.ValueTable[value] = parts[p + 1].Trim('"');
                    continue;
                }
                if (line.StartsWith("BA_ \"GenMsgCycleTime\" BO_ ", StringComparison.Ordinal)) { var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries); if (parts.Length >= 5 && uint.TryParse(parts[3], out var id) && int.TryParse(parts[4].TrimEnd(';'), out var ms) && byId.TryGetValue(id & 0x1FFFFFFF, out var m)) m.CycleTimeMs = ms; continue; }
            }
            catch (Exception ex) { errors.Add($"第{i + 1}行：{ex.Message}"); }
        }
        if (db.Messages.Count == 0) errors.Add("没有找到 BO_ 报文定义。");
        return new DbcImportResult(db, errors, warnings);
    }
}

public static class DbcExporter
{
    public static void Export(string path, CanDatabase database)
    {
        var invalid = database.Messages.SelectMany(message => message.Signals.Select(signal => (message, signal)))
            .Where(x => string.IsNullOrWhiteSpace(x.signal.Name)).ToList();
        if (invalid.Count > 0)
            throw new InvalidOperationException($"无法导出 DBC：有 {invalid.Count} 个信号名称为空。请先填写“名称”，别名不能作为 DBC 名称。");
        var lines = new List<string>
        {
            "VERSION \"\"", "", "NS_ :", "", "BS_: ", "", "BU_: ECU", ""
        };
        foreach (var message in database.Messages.OrderBy(x => x.Id))
        {
            // DBC uses bit31 to mark an extended-frame identifier.  Internally we
            // keep the normalized 29-bit ID plus Extended, so export must restore
            // the wire/file representation or another DBC tool will import the
            // frame with the wrong identity.
            var dbcId = message.Extended ? message.Id | 0x80000000u : message.Id;
            lines.Add($"BO_ {dbcId} {message.Name}: {Math.Clamp(message.Dlc, 0, 64)} ECU");
            foreach (var signal in message.Signals)
            {
                var order = signal.ByteOrder == CanByteOrder.Motorola ? "0" : "1";
                var sign = signal.Signed ? "-" : "+";
                var min = signal.Minimum?.ToString(CultureInfo.InvariantCulture) ?? "0";
                var max = signal.Maximum?.ToString(CultureInfo.InvariantCulture) ?? "0";
                var unit = signal.Unit.Replace("\"", "'");
                lines.Add($" SG_ {signal.Name} : {signal.StartBit}|{signal.BitLength}@{order}{sign} ({signal.Factor.ToString(CultureInfo.InvariantCulture)},{signal.Offset.ToString(CultureInfo.InvariantCulture)}) [{min}|{max}] \"{unit}\" ECU");
            }
            lines.Add("");
            if (message.CycleTimeMs is > 0) lines.Add($"BA_ \"GenMsgCycleTime\" BO_ {message.Id} {message.CycleTimeMs.Value};");
        }
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }
}

public static class ParseDefinitionCsv
{
    public static ParseDefinitionSet Import(string path)
    {
        var set = new ParseDefinitionSet { Name = Path.GetFileNameWithoutExtension(path), SourcePath = Path.GetFullPath(path) };
        var rows = File.ReadAllLines(path, Encoding.UTF8).Where(x => !string.IsNullOrWhiteSpace(x)).ToList(); if (rows.Count == 0) return set;
        var header = Split(rows[0]); var indexes = header.Select((v, i) => (v.Trim(), i)).ToDictionary(x => x.Item1, x => x.i, StringComparer.OrdinalIgnoreCase);
        string Get(List<string> row, params string[] names) { foreach (var n in names) if (indexes.TryGetValue(n, out var index) && index < row.Count) return row[index]; return ""; }
        var rowNumber = 1;
        foreach (var text in rows.Skip(1))
        {
            rowNumber++;
            var r = Split(text);
            var x = new ParseSignalDefinition
            {
                Name = Get(r, "变量名称", "信号名称", "Name").Trim(),
                DisplayName = Get(r, "显示名称", "中文名称").Trim()
            };
            if (x.Name.Length == 0)
                throw new InvalidDataException($"第 {rowNumber} 行信号名称不能为空；别名不能替代名称。");

            x.Id = ParseHexRequired(Get(r, "报文ID", "上报ID", "ID"), rowNumber, "报文ID");
            if (x.Id > 0x1FFFFFFFu)
                throw new InvalidDataException($"第 {rowNumber} 行报文ID超出29位 CAN 范围：0x{x.Id:X}。");
            x.StartByte = IntRequired(Get(r, "起始字节", "字节"), rowNumber, "起始字节");
            x.StartBit = IntRequired(Get(r, "起始位", "位"), rowNumber, "起始位");
            x.BitLength = IntRequired(Get(r, "位宽", "位长度"), rowNumber, "位宽");
            if (x.StartByte is < 0 or > 7) throw new InvalidDataException($"第 {rowNumber} 行起始字节必须为0~7。");
            if (x.StartBit is < 0 or > 7) throw new InvalidDataException($"第 {rowNumber} 行起始位必须为0~7。");
            if (x.BitLength is < 1 or > 64) throw new InvalidDataException($"第 {rowNumber} 行位宽必须为1~64。");
            x.ByteOrder = Get(r, "字节序").Contains("Motorola", StringComparison.OrdinalIgnoreCase) || Get(r, "字节序").Contains("大", StringComparison.Ordinal) ? "Motorola" : "Intel";
            x.Factor = DoubleOptional(Get(r, "系数", "换算系数"), 1, rowNumber, "系数");
            x.Offset = DoubleOptional(Get(r, "偏移"), 0, rowNumber, "偏移");
            x.Unit = Get(r, "单位");
            x.DisplayMode = Get(r, "显示方式");
            x.AlarmType = Get(r, "报警类型");
            x.Description = Get(r, "信号描述", "描述");
            set.Signals.Add(x);
        }
        return set;
    }
    public static void Export(string path, ParseDefinitionSet set)
    {
        var lines = new List<string> { "变量名称,显示名称,报文ID,起始字节,起始位,位宽,字节序,系数,偏移,单位,显示方式,报警类型,信号描述" };
        lines.AddRange(set.Signals.Select(x => string.Join(',', new[] { x.Name, x.DisplayName, $"0x{x.Id:X}", x.StartByte.ToString(), x.StartBit.ToString(), x.BitLength.ToString(), x.ByteOrder == "Motorola" ? "大端序" : "小端序", x.Factor.ToString(CultureInfo.InvariantCulture), x.Offset.ToString(CultureInfo.InvariantCulture), x.Unit, x.DisplayMode, x.AlarmType, x.Description }.Select(Escape)))); File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }
    private static string Escape(string x) => x.Contains(',') || x.Contains('"') || x.Contains('\n') ? $"\"{x.Replace("\"", "\"\"")}\"" : x;
    private static List<string> Split(string line) { var list = new List<string>(); var sb = new StringBuilder(); var quote = false; var delimiter = line.Contains('\t') ? '\t' : ','; foreach (var c in line) { if (c == '"') quote = !quote; else if (c == delimiter && !quote) { list.Add(sb.ToString().Trim()); sb.Clear(); } else sb.Append(c); } list.Add(sb.ToString().Trim()); return list; }
    private static uint ParseHexRequired(string text, int row, string field)
    {
        var value = text.Trim().Replace("0x", "", StringComparison.OrdinalIgnoreCase);
        if (value.Length == 0 || !uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var result))
            throw new InvalidDataException($"第 {row} 行{field}不是有效的十六进制数：{text}。");
        return result;
    }

    private static int IntRequired(string text, int row, string field)
    {
        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
            throw new InvalidDataException($"第 {row} 行{field}不是有效整数：{text}。");
        return result;
    }

    private static double DoubleOptional(string text, double fallback, int row, string field)
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;
        if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var result) || !double.IsFinite(result))
            throw new InvalidDataException($"第 {row} 行{field}不是有效数值：{text}。");
        return result;
    }
}

public static class CanPayloadCodec
{
    public readonly record struct DecodedSignal(long RawValue, double PhysicalValue);

    public static string Encode(CanMessageDefinition message, IReadOnlyDictionary<string, string> values)
    {
        var data = new byte[Math.Clamp(message.Dlc, 0, 64)];
        foreach (var signal in message.Signals)
        {
            if (!values.TryGetValue(signal.Name, out var text) || !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var physical)) continue;
            var raw = signal.Factor == 0 ? 0 : (long)Math.Round((physical - signal.Offset) / signal.Factor);
            if (signal.Signed && raw < 0) raw = (1L << Math.Min(signal.BitLength, 62)) + raw;
            for (var i = 0; i < signal.BitLength; i++)
            {
                var bit = signal.ByteOrder == CanByteOrder.Intel ? signal.StartBit + i : MotorolaBit(signal.StartBit, i);
                var b = bit / 8; if (b < 0 || b >= data.Length) continue; var mask = (byte)(1 << (bit % 8)); if (((raw >> i) & 1) != 0) data[b] |= mask; else data[b] &= (byte)~mask;
            }
        }
        return string.Join(' ', data.Select(x => x.ToString("X2", CultureInfo.InvariantCulture)));
    }

    public static IReadOnlyDictionary<string, string> Decode(CanMessageDefinition message, string? text)
    {
        var bytes = (text ?? string.Empty).Split(new[] { ' ', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => byte.TryParse(x, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value) ? value : (byte)0)
            .Take(Math.Clamp(message.Dlc, 0, 64)).ToArray();
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var signal in message.Signals)
        {
            if (TryDecodeSignal(message, signal, bytes, out var decoded, out _))
                result[signal.Name] = decoded.PhysicalValue.ToString("0.########", CultureInfo.InvariantCulture);
        }
        return result;
    }

    /// <summary>
    /// Canonical signal decode used by Database-bound Parse. The same bit
    /// extraction path is also used by the legacy dictionary Decode facade.
    /// </summary>
    public static bool TryDecodeSignal(
        CanMessageDefinition message,
        CanSignalDefinition signal,
        ReadOnlySpan<byte> data,
        out DecodedSignal decoded,
        out string? error)
    {
        decoded = default;
        error = null;
        if (message is null || signal is null)
        {
            error = "报文或信号定义为空。";
            return false;
        }
        if (signal.BitLength is < 1 or > 64 || signal.StartBit < 0)
        {
            error = $"信号位定义无效：StartBit={signal.StartBit}，BitLength={signal.BitLength}。";
            return false;
        }
        if (!double.IsFinite(signal.Factor) || !double.IsFinite(signal.Offset))
        {
            error = "信号换算系数或偏移不是有限数值。";
            return false;
        }

        ulong rawBits = 0;
        for (var i = 0; i < signal.BitLength; i++)
        {
            var bit = signal.ByteOrder == CanByteOrder.Intel
                ? signal.StartBit + i
                : MotorolaBit(signal.StartBit, i);
            var byteIndex = bit / 8;
            var bitIndex = bit % 8;
            if (bit < 0 || byteIndex >= data.Length)
            {
                error = $"信号位范围超出实际 CAN 数据：StartBit={signal.StartBit}，BitLength={signal.BitLength}，DLC={data.Length}。";
                return false;
            }
            if (((data[byteIndex] >> bitIndex) & 1) != 0)
                rawBits |= 1UL << i;
        }

        long raw;
        if (signal.Signed)
        {
            raw = signal.BitLength == 64
                ? unchecked((long)rawBits)
                : (rawBits & (1UL << (signal.BitLength - 1))) != 0
                    ? unchecked((long)(rawBits | (~0UL << signal.BitLength)))
                    : (long)rawBits;
        }
        else if (rawBits > long.MaxValue)
        {
            error = "无符号原始值超出当前结果类型的范围。";
            return false;
        }
        else
        {
            raw = (long)rawBits;
        }

        var physical = raw * signal.Factor + signal.Offset;
        if (!double.IsFinite(physical))
        {
            error = "工程值不是有限数值。";
            return false;
        }
        decoded = new(raw, physical);
        return true;
    }

    private static int MotorolaBit(int start, int offset) { var bit = start; for (var i = 0; i < offset; i++) bit = bit % 8 == 0 ? bit + 15 : bit - 1; return bit; }
}
