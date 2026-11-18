using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using EPB.Core.Communication;
using EPB.Core.Testing;

namespace EPB.Core.Configuration;

public sealed class EcuConfiguration
{
    public const string Schema = "ecu-configuration-2";
    public string SchemaVersion { get; set; } = Schema;
    public string Name { get; set; } = "新 ECU 配置";
    public ConfigurationTreeItem Root { get; set; } = new() { Name = "ECU 配置", Kind = ConfigurationItemKind.文件夹 };
    public TestPlan TestPlan { get; set; } = new();
    /// <summary>可选的通信数据库；项目内嵌保存，避免换电脑后只剩路径。</summary>
    public CanDatabase? CommunicationDatabase { get; set; }
    /// <summary>可选的报文解析定义，可由 CSV 导入并被测试步骤引用。</summary>

    public static EcuConfiguration CreateNew()
    {
        var configuration = new EcuConfiguration();
        configuration.Root.AddFolder("广播报文");
        configuration.Root.AddFolder("报文解析");
        configuration.Root.AddFolder("动作库");
        configuration.Root.AddFolder("测试工况");
        configuration.Root.AddFolder("测试用例");
        configuration.TestPlan.Name = "自动测试流程";
        return configuration;
    }

    public ConfigurationSequence? FindSequence(string sequenceId)
        => FindSequence(Root, sequenceId);

    private static ConfigurationSequence? FindSequence(ConfigurationTreeItem item, string sequenceId)
    {
        if (item.Sequence?.Id == sequenceId) return item.Sequence;
        foreach (var child in item.Children)
        {
            var found = FindSequence(child, sequenceId);
            if (found is not null) return found;
        }
        return null;
    }
}

public enum ConfigurationItemKind { 文件夹, 序列, 原子操作 }

public sealed class ConfigurationTreeItem : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _name = "新建项目";
    private ConfigurationItemKind _kind;
    private ConfigurationSequence? _sequence;
    private ObservableCollection<ConfigurationTreeItem> _children = [];
    public string Id { get => _id; set { var next = string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value; if (_id == next) return; _id = next; OnPropertyChanged(); OnPropertyChanged(nameof(NodeId)); } }
    [JsonIgnore]
    public string NodeId => Id;
    public string Name { get => _name; set { value ??= string.Empty; if (_name == value) return; _name = value; OnPropertyChanged(); } }
    public ConfigurationItemKind Kind { get => _kind; set { if (_kind == value) return; _kind = value; OnPropertyChanged(); } }
    public ConfigurationSequence? Sequence { get => _sequence; set { if (ReferenceEquals(_sequence, value)) return; if (_sequence is not null) _sequence.PropertyChanged -= SequenceChanged; _sequence = value; if (_sequence is not null) _sequence.PropertyChanged += SequenceChanged; OnPropertyChanged(); } }
    public ObservableCollection<ConfigurationTreeItem> Children { get => _children; set { if (ReferenceEquals(_children, value)) return; _children = value ?? []; OnPropertyChanged(); } }

    public ConfigurationTreeItem AddFolder(string name)
    {
        var child = new ConfigurationTreeItem { Name = name, Kind = ConfigurationItemKind.文件夹 };
        Children.Add(child); return child;
    }

    public ConfigurationTreeItem AddSequence(string name)
    {
        var child = new ConfigurationTreeItem { Name = name, Kind = ConfigurationItemKind.序列, Sequence = new ConfigurationSequence { Name = name } };
        Children.Add(child); return child;
    }

    public ConfigurationTreeItem AddAtomicOperation(ConfigurationStepKind kind)
    {
        var name = kind switch
        {
            ConfigurationStepKind.广播报文 => "发送报文",
            ConfigurationStepKind.动作控制 => "逻辑控制",
            ConfigurationStepKind.电源控制 => "电源控制",
            ConfigurationStepKind.报文解析 => "报文解析",
            ConfigurationStepKind.波形判定 => "波形判定",
            ConfigurationStepKind.等待 => "等待",
            _ => kind.ToString()
        };
        var sequence = new ConfigurationSequence { Name = name };
        sequence.AddStep(kind).Name = name;
        var child = new ConfigurationTreeItem { Name = name, Kind = ConfigurationItemKind.原子操作, Sequence = sequence };
        Children.Add(child); return child;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void SequenceChanged(object? sender, PropertyChangedEventArgs e) => OnPropertyChanged(nameof(Sequence));
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class ConfigurationSequence : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _name = "新建序列";
    private ObservableCollection<ConfigurationStep> _steps = [];
    public string Id { get => _id; set { var next = string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value; if (_id == next) return; _id = next; OnPropertyChanged(); OnPropertyChanged(nameof(NodeId)); } }
    [JsonIgnore]
    public string NodeId => Id;
    public string Name { get => _name; set { value ??= string.Empty; if (_name == value) return; _name = value; OnPropertyChanged(); } }
    public ObservableCollection<ConfigurationStep> Steps { get => _steps; set { if (ReferenceEquals(_steps, value)) return; _steps = value ?? []; OnPropertyChanged(); } }

    public ConfigurationStep AddStep(ConfigurationStepKind kind)
    {
        var step = new ConfigurationStep { Kind = kind, Name = kind.ToString() };
        Steps.Add(step); return step;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public enum ConfigurationStepKind { 广播报文, 报文解析, 电源控制, 动作控制, 等待, 引用序列, 组合, 波形判定 }

/// <summary>Defines which kind of definition a step references.</summary>
public enum ConfigurationSource
{
    独立配置,
    通信数据库
}

public enum StepExecutionState { 未运行, 运行中, 成功, 失败, 错误 }

public sealed class ConfigurationStep : INotifyPropertyChanged
{
    private bool _selectedForGroup;
    private StepExecutionState _executionState;
    private string? _resolvedSummary;
    private string _name = "新步骤";
    private ConfigurationStepKind _kind;
    private BroadcastParameters _broadcast = new();
    private ParseParameters _parse = new();
    private WaveformAssertionParameters _waveformAssertion = new();
    private PowerParameters _power = new();
    private ActionParameters _action = new();
    private WaitParameters _wait = new();
    private SequenceReferenceParameters _reference = new();
    private ObservableCollection<ConfigurationStep> _children = [];
    private string _id = Guid.NewGuid().ToString("N");
    public ConfigurationStep()
    {
        Subscribe(_broadcast); Subscribe(_parse); Subscribe(_waveformAssertion); Subscribe(_power); Subscribe(_action); Subscribe(_wait); Subscribe(_reference);
        _children.CollectionChanged += ChildrenChanged;
    }
    public string Id { get => _id; set { var next = string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value; if (_id == next) return; _id = next; OnPropertyChanged(); OnPropertyChanged(nameof(NodeId)); } }
    [JsonIgnore]
    public string NodeId => Id;
    public string Name
    {
        get => _name;
        set
        {
            if (_name == value) return;
            _name = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Summary));
        }
    }
    public ConfigurationStepKind Kind { get => _kind; set { if (_kind == value) return; _kind = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayKind)); OnPropertyChanged(nameof(Summary)); } }
    public BroadcastParameters Broadcast { get => _broadcast; set { if (ReferenceEquals(_broadcast, value)) return; Unsubscribe(_broadcast); _broadcast = value ?? new(); Subscribe(_broadcast); OnPropertyChanged(); NotifySummaryChanged(); } }
    public ParseParameters Parse { get => _parse; set { if (ReferenceEquals(_parse, value)) return; Unsubscribe(_parse); _parse = value ?? new(); Subscribe(_parse); OnPropertyChanged(); NotifySummaryChanged(); } }
    public WaveformAssertionParameters WaveformAssertion { get => _waveformAssertion; set { if (ReferenceEquals(_waveformAssertion, value)) return; Unsubscribe(_waveformAssertion); _waveformAssertion = value ?? new(); Subscribe(_waveformAssertion); OnPropertyChanged(); NotifySummaryChanged(); } }
    public PowerParameters Power { get => _power; set { if (ReferenceEquals(_power, value)) return; Unsubscribe(_power); _power = value ?? new(); Subscribe(_power); OnPropertyChanged(); NotifySummaryChanged(); } }
    public ActionParameters Action { get => _action; set { if (ReferenceEquals(_action, value)) return; Unsubscribe(_action); _action = value ?? new(); Subscribe(_action); OnPropertyChanged(); NotifySummaryChanged(); } }
    public WaitParameters Wait { get => _wait; set { if (ReferenceEquals(_wait, value)) return; Unsubscribe(_wait); _wait = value ?? new(); Subscribe(_wait); OnPropertyChanged(); NotifySummaryChanged(); } }
    public SequenceReferenceParameters Reference { get => _reference; set { if (ReferenceEquals(_reference, value)) return; Unsubscribe(_reference); _reference = value ?? new(); Subscribe(_reference); OnPropertyChanged(); NotifySummaryChanged(); } }
    public ObservableCollection<ConfigurationStep> Children { get => _children; set { if (ReferenceEquals(_children, value)) return; if (_children is not null) _children.CollectionChanged -= ChildrenChanged; _children = value ?? []; _children.CollectionChanged += ChildrenChanged; OnPropertyChanged(); NotifySummaryChanged(); } }

    [JsonIgnore]
    public bool SelectedForGroup { get => _selectedForGroup; set { if (_selectedForGroup == value) return; _selectedForGroup = value; OnPropertyChanged(); } }

    [JsonIgnore]
    public StepExecutionState ExecutionState { get => _executionState; set { if (_executionState == value) return; _executionState = value; OnPropertyChanged(); } }

    [JsonIgnore]
    public string DisplayKind => Kind switch
    {
        ConfigurationStepKind.广播报文 => "广播报文",
        ConfigurationStepKind.报文解析 => "报文解析",
        ConfigurationStepKind.电源控制 => "电源控制",
        ConfigurationStepKind.动作控制 => "动作控制",
        ConfigurationStepKind.等待 => "等待",
        ConfigurationStepKind.引用序列 => "引用",
        ConfigurationStepKind.组合 => "组合",
        ConfigurationStepKind.波形判定 => "波形判定",
        _ => Kind.ToString()
    };

    [JsonIgnore]
    public string Summary => _resolvedSummary ?? (Kind switch
    {
        ConfigurationStepKind.广播报文 => string.IsNullOrWhiteSpace(Broadcast.MessageName)
            ? $"0x{Broadcast.Id:X}，{Broadcast.Mode}，{Broadcast.PeriodMs} ms"
            : $"{Broadcast.MessageName} · 0x{Broadcast.Id:X}，{Broadcast.Mode}，{Broadcast.PeriodMs} ms",
        ConfigurationStepKind.报文解析 => $"{(string.IsNullOrWhiteSpace(Parse.SignalName) ? Name : Parse.SignalName)} · 0x{Parse.Id:X}，字节 {Parse.StartByte} 位 {Parse.StartBit}，{Parse.BitLength} bit",
        ConfigurationStepKind.波形判定 => WaveformAssertionSummary(),
        ConfigurationStepKind.电源控制 => $"{Power.Target} = {(Power.On ? "打开" : "关闭")}",
        ConfigurationStepKind.动作控制 => ActionSummary(),
        ConfigurationStepKind.等待 => $"{Wait.DurationMs} ms",
        ConfigurationStepKind.引用序列 => string.IsNullOrWhiteSpace(Reference.SequenceId) ? "未选择引用" : "引用",
        ConfigurationStepKind.组合 => $"包含 {Children.Count} 个步骤",
        _ => ""
    });

    public void SetResolvedSummary(string? summary)
    {
        _resolvedSummary = string.IsNullOrWhiteSpace(summary) ? null : summary;
        OnPropertyChanged(nameof(Summary));
    }

    private string ActionSummary()
    {
        var selected = Enumerable.Range(0, 20)
            .Where(i => (Action.OutputMask & (1u << i)) != 0)
            .Select(i => $"K{i + 1}")
            .ToArray();
        return selected.Length == 0 ? "未选择逻辑操作" : string.Join("、", selected);
    }

    private string WaveformAssertionSummary()
    {
        var signal = !string.IsNullOrWhiteSpace(WaveformAssertion.DisplayName)
            ? WaveformAssertion.DisplayName
            : !string.IsNullOrWhiteSpace(WaveformAssertion.SignalName)
                ? WaveformAssertion.SignalName
                : Name;
        var standard = string.IsNullOrWhiteSpace(WaveformAssertion.StandardWaveform.Name)
            ? "未选择标准波形"
            : WaveformAssertion.StandardWaveform.Name;
        return $"{signal} · {standard}，{WaveformAssertion.Rule.WindowDurationMs} ms";
    }

    public object ParametersFor(ConfigurationStepKind kind) => kind switch
    {
        ConfigurationStepKind.广播报文 => Broadcast,
        ConfigurationStepKind.报文解析 => Parse,
        ConfigurationStepKind.波形判定 => WaveformAssertion,
        ConfigurationStepKind.电源控制 => Power,
        ConfigurationStepKind.动作控制 => Action,
        ConfigurationStepKind.等待 => Wait,
        ConfigurationStepKind.引用序列 => Reference,
        ConfigurationStepKind.组合 => Children,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    public void NotifySummaryChanged() => OnPropertyChanged(nameof(Summary));
    public void RegenerateIdsRecursively() { Id = Guid.NewGuid().ToString("N"); foreach (var child in Children) child.RegenerateIdsRecursively(); }
    private void ChildrenChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => NotifySummaryChanged();
    private void Subscribe(INotifyPropertyChanged value) => value.PropertyChanged += ParameterChanged;
    private void Unsubscribe(INotifyPropertyChanged value) => value.PropertyChanged -= ParameterChanged;
    private void ParameterChanged(object? sender, PropertyChangedEventArgs e) => NotifySummaryChanged();
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public abstract class ConfigurationParametersBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new(name));
        return true;
    }
}

public sealed class BroadcastParameters : ConfigurationParametersBase
{
    private bool _extended; private uint _id; private byte _dlc = 8; private string _dataHex = "00 00 00 00 00 00 00 00"; private int _periodMs = 100; private BroadcastMode _mode = BroadcastMode.发送一次; private string _messageName = ""; private ConfigurationSource _source = ConfigurationSource.独立配置; private string _messageDefinitionId = "";
    /// <summary>Missing legacy JSON defaults to 独立配置.</summary>
    public ConfigurationSource Source { get => _source; set => Set(ref _source, value); }
    public string MessageDefinitionId { get => _messageDefinitionId; set => Set(ref _messageDefinitionId, value ?? string.Empty); }
    public bool Extended { get => _extended; set => Set(ref _extended, value); }
    public uint Id { get => _id; set => Set(ref _id, value); }
    public byte Dlc { get => _dlc; set => Set(ref _dlc, value); }
    public string DataHex { get => _dataHex; set => Set(ref _dataHex, value ?? string.Empty); }
    public int PeriodMs { get => _periodMs; set => Set(ref _periodMs, value); }
    public BroadcastMode Mode { get => _mode; set => Set(ref _mode, value); }
    public string MessageName { get => _messageName; set => Set(ref _messageName, value ?? string.Empty); }
    public Dictionary<string, string> SignalValues { get; set; } = [];
}
public enum BroadcastMode { 发送一次, 开始周期发送, 停止周期发送 }

public sealed class ParseParameters : ConfigurationParametersBase
{
    /// <summary>信号在解析库中的稳定名称；为空时沿用步骤名称。</summary>
    private string _signalName = ""; private bool _extended; private uint _id; private int _startByte; private int _startBit; private int _bitLength = 8; private string _byteOrder = "Intel"; private double _factor = 1; private double _offset; private string _unit = ""; private string _displayMode = "波形显示"; private string _alarmType = ""; private string _description = ""; private string _databaseSignalName = ""; private string _displayName = ""; private ConfigurationSource _source = ConfigurationSource.独立配置; private string _messageDefinitionId = ""; private string _signalDefinitionId = "";
    /// <summary>Missing legacy JSON defaults to 独立配置.</summary>
    public ConfigurationSource Source { get => _source; set => Set(ref _source, value); }
    public string MessageDefinitionId { get => _messageDefinitionId; set => Set(ref _messageDefinitionId, value ?? string.Empty); }
    public string SignalDefinitionId { get => _signalDefinitionId; set => Set(ref _signalDefinitionId, value ?? string.Empty); }
    public string SignalName { get => _signalName; set => Set(ref _signalName, value ?? string.Empty); }
    public bool Extended { get => _extended; set => Set(ref _extended, value); }
    public uint Id { get => _id; set => Set(ref _id, value); }
    public int StartByte { get => _startByte; set => Set(ref _startByte, value); }
    public int StartBit { get => _startBit; set => Set(ref _startBit, value); }
    public int BitLength { get => _bitLength; set => Set(ref _bitLength, value); }
    /// <summary>当前绿通协议表使用 Intel（LSB）位序；保留为字符串便于后续加入 Motorola。</summary>
    public string ByteOrder { get => _byteOrder; set => Set(ref _byteOrder, value ?? string.Empty); }
    public double Factor { get => _factor; set => Set(ref _factor, value); }
    public double Offset { get => _offset; set => Set(ref _offset, value); }
    public string Unit { get => _unit; set => Set(ref _unit, value ?? string.Empty); }
    public string DisplayMode { get => _displayMode; set => Set(ref _displayMode, value ?? string.Empty); }
    public string AlarmType { get => _alarmType; set => Set(ref _alarmType, value ?? string.Empty); }
    public string Description { get => _description; set => Set(ref _description, value ?? string.Empty); }
    public string DatabaseSignalName { get => _databaseSignalName; set => Set(ref _databaseSignalName, value ?? string.Empty); }
    public string DisplayName { get => _displayName; set => Set(ref _displayName, value ?? string.Empty); }
    // 判定条件不属于解析资源。保留旧字段仅用于读取旧配置/旧 API，新的配置序列化不会写入。
    [JsonIgnore]
    public string Expectation { get; set; } = "";
    [JsonIgnore]
    public int TimeoutMs { get; set; } = 1000;
}

public sealed class WaveformAssertionParameters : ConfigurationParametersBase
{
    private string _signalName = ""; private bool _extended; private uint _id; private int _startByte; private int _startBit; private int _bitLength = 8; private string _byteOrder = "Intel"; private double _factor = 1; private double _offset; private string _unit = ""; private string _databaseSignalName = ""; private string _displayName = ""; private ConfigurationSource _source = ConfigurationSource.通信数据库; private string _messageDefinitionId = ""; private string _signalDefinitionId = ""; private bool _participatesInJudgement = true; private StandardWaveform _standardWaveform = new(); private WaveformAssertionRule _rule = new();
    public ConfigurationSource Source { get => _source; set => Set(ref _source, value); }
    public string MessageDefinitionId { get => _messageDefinitionId; set => Set(ref _messageDefinitionId, value ?? string.Empty); }
    public string SignalDefinitionId { get => _signalDefinitionId; set => Set(ref _signalDefinitionId, value ?? string.Empty); }
    public string SignalName { get => _signalName; set => Set(ref _signalName, value ?? string.Empty); }
    public bool Extended { get => _extended; set => Set(ref _extended, value); }
    public uint Id { get => _id; set => Set(ref _id, value); }
    public int StartByte { get => _startByte; set => Set(ref _startByte, value); }
    public int StartBit { get => _startBit; set => Set(ref _startBit, value); }
    public int BitLength { get => _bitLength; set => Set(ref _bitLength, value); }
    public string ByteOrder { get => _byteOrder; set => Set(ref _byteOrder, value ?? string.Empty); }
    public double Factor { get => _factor; set => Set(ref _factor, value); }
    public double Offset { get => _offset; set => Set(ref _offset, value); }
    public string Unit { get => _unit; set => Set(ref _unit, value ?? string.Empty); }
    public string DatabaseSignalName { get => _databaseSignalName; set => Set(ref _databaseSignalName, value ?? string.Empty); }
    public string DisplayName { get => _displayName; set => Set(ref _displayName, value ?? string.Empty); }
    public bool ParticipatesInJudgement { get => _participatesInJudgement; set => Set(ref _participatesInJudgement, value); }
    public StandardWaveform StandardWaveform { get => _standardWaveform; set => Set(ref _standardWaveform, value ?? new()); }
    public WaveformAssertionRule Rule { get => _rule; set => Set(ref _rule, value ?? new()); }
}

public sealed class StandardWaveform
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string SourceName { get; set; } = "";
    public string Unit { get; set; } = "";
    public ObservableCollection<StandardWaveformPoint> Points { get; set; } = [];
}

public sealed class StandardWaveformPoint
{
    public double TimeMs { get; set; }
    public double Value { get; set; }
}

public sealed class WaveformAssertionRule
{
    public int WindowStartMs { get; set; }
    public int WindowDurationMs { get; set; } = 1000;
    public double AbsoluteTolerance { get; set; }
    /// <summary>Configuration value in percent. For example, 5 means 5%.</summary>
    public double PercentTolerance { get; set; } = 5;
    public int TimeShiftToleranceMs { get; set; }
    public double AllowedOutlierPercent { get; set; }
    public int MinimumSampleCount { get; set; } = 2;
}

public sealed class PowerParameters : ConfigurationParametersBase
{
    private PowerTarget _target = PowerTarget.ECU主电源; private bool _on = true;
    public PowerTarget Target { get => _target; set => Set(ref _target, value); }
    public bool On { get => _on; set => Set(ref _on, value); }
}
public enum PowerTarget { ECU主电源, IGN, 按键板电源 }

public sealed class ActionParameters : ConfigurationParametersBase
{
    /// <summary>完整 20 路快照；未置位即断开。内部 CAN 与 GPIO 对配置人员隐藏。</summary>
    private uint _outputMask;
    public uint OutputMask { get => _outputMask; set => Set(ref _outputMask, value); }
}

public sealed class WaitParameters : ConfigurationParametersBase
{
    private int _durationMs = 1000;
    public int DurationMs { get => _durationMs; set => Set(ref _durationMs, value); }
}

public sealed class SequenceReferenceParameters : ConfigurationParametersBase
{
    private string _sequenceId = "";
    public string SequenceId { get => _sequenceId; set => Set(ref _sequenceId, value ?? string.Empty); }
}

public static class ConfigurationJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    static ConfigurationJson()
    {
        Options.Converters.Add(new ConfigurationSourceJsonConverter());
    }

    public static string Serialize(EcuConfiguration configuration) => JsonSerializer.Serialize(configuration, Options);
    public static EcuConfiguration Deserialize(string json)
    {
        var configuration = JsonSerializer.Deserialize<EcuConfiguration>(json, Options) ?? throw new JsonException("配置文件为空。");
        if (configuration.SchemaVersion != EcuConfiguration.Schema) throw new JsonException($"不支持的配置版本：{configuration.SchemaVersion}");
        return configuration;
    }

    /// <summary>Explicit one-time migration; it never infers source from names.</summary>
    public static void MigrateLegacyDefinitionSources(EcuConfiguration configuration, ConfigurationSource source)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        foreach (var step in EnumerateSteps(configuration.Root))
        {
            if (step.Kind == ConfigurationStepKind.广播报文) step.Broadcast.Source = source;
            if (step.Kind == ConfigurationStepKind.报文解析) step.Parse.Source = source;
        }
    }

    private static IEnumerable<ConfigurationStep> EnumerateSteps(ConfigurationTreeItem item)
    {
        if (item.Sequence is not null)
            foreach (var step in item.Sequence.Steps)
                foreach (var nested in EnumerateSteps(step)) yield return nested;
        foreach (var child in item.Children)
            foreach (var step in EnumerateSteps(child)) yield return step;
    }

    private static IEnumerable<ConfigurationStep> EnumerateSteps(ConfigurationStep step)
    {
        yield return step;
        foreach (var child in step.Children)
            foreach (var nested in EnumerateSteps(child)) yield return nested;
    }

    private sealed class ConfigurationSourceJsonConverter : JsonConverter<ConfigurationSource>
    {
        public override ConfigurationSource Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number))
                return Enum.IsDefined(typeof(ConfigurationSource), number)
                    ? (ConfigurationSource)number
                    : ConfigurationSource.独立配置;

            if (reader.TokenType == JsonTokenType.String)
            {
                var value = reader.GetString();
                if (string.Equals(value, "CommunicationDatabase", StringComparison.OrdinalIgnoreCase) || value == "通信数据库")
                    return ConfigurationSource.通信数据库;
                if (string.Equals(value, "Direct", StringComparison.OrdinalIgnoreCase) || value == "独立配置")
                    return ConfigurationSource.独立配置;
            }

            return ConfigurationSource.独立配置;
        }

        public override void Write(Utf8JsonWriter writer, ConfigurationSource value, JsonSerializerOptions options)
            => writer.WriteNumberValue((int)value);
    }
}
