using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace AutoTest.Frontend;

// UI-only editable examples. These are not production TEST/PROJECT/DBC models.
public abstract class DraftEntity : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        return true;
    }
}

public sealed class CaseDraft : DraftEntity
{
    private string _name = "新测试用例";
    private bool _selectedForRun = true;
    public string Name { get => _name; set => Set(ref _name, value); }
    public bool SelectedForRun { get => _selectedForRun; set => Set(ref _selectedForRun, value); }
    public ObservableCollection<StepDraft> Steps { get; set; } = [];
    [JsonIgnore] public string Summary => $"{Steps.Count} 个步骤";
}

public sealed class StepDraft : DraftEntity
{
    private string _name = "新步骤", _phase = "测试", _kind = "CAN 发送",
        _target = "", _value = "", _note = "";
    private bool _enabled = true, _breakpoint;
    public string Name { get => _name; set => Set(ref _name, value); }
    public string Phase { get => _phase; set => Set(ref _phase, value); }
    public string Kind { get => _kind; set => Set(ref _kind, value); }
    public string Target { get => _target; set => Set(ref _target, value); }
    public string Value { get => _value; set => Set(ref _value, value); }
    public string Note { get => _note; set => Set(ref _note, value); }
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    public bool Breakpoint { get => _breakpoint; set => Set(ref _breakpoint, value); }
}

public sealed class CanDraft : DraftEntity
{
    private string _name = "新报文", _idHex = "", _dataHex = "";
    private bool _included = true, _periodic;
    private int _periodMs = 100;
    public string Name { get => _name; set => Set(ref _name, value); }
    public string IdHex { get => _idHex; set => Set(ref _idHex, value); }
    public string DataHex { get => _dataHex; set => Set(ref _dataHex, value); }
    public bool Included { get => _included; set => Set(ref _included, value); }
    public bool Periodic { get => _periodic; set => Set(ref _periodic, value); }
    public int PeriodMs { get => _periodMs; set => Set(ref _periodMs, value); }
}

public sealed class KeyCell : DraftEntity
{
    private bool _checked;
    public int Number { get; set; }
    public bool Checked { get => _checked; set => Set(ref _checked, value); }
    [JsonIgnore] public string Label => $"K{Number:00}";
}

public sealed class KeyStepDraft : DraftEntity
{
    private int _holdMs = 200;
    public int HoldMs { get => _holdMs; set => Set(ref _holdMs, value); }
    public ObservableCollection<KeyCell> Keys { get; set; } =
        new(Enumerable.Range(1, 20).Select(i => new KeyCell { Number = i }));
}

public sealed class KeyActionDraft : DraftEntity
{
    private string _name = "新输入动作";
    public string Name { get => _name; set => Set(ref _name, value); }
    public ObservableCollection<KeyStepDraft> Steps { get; set; } = [];
}

public sealed class UdsRequestDraft : DraftEntity
{
    private string _name = "新请求", _service = "22", _payload = "", _description = "";
    private int _timeoutMs = 1000;
    private bool _enabled = true;
    public string Name { get => _name; set => Set(ref _name, value); }
    public string Service { get => _service; set => Set(ref _service, value); }
    public string Payload { get => _payload; set => Set(ref _payload, value); }
    public string Description { get => _description; set => Set(ref _description, value); }
    public int TimeoutMs { get => _timeoutMs; set => Set(ref _timeoutMs, value); }
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
}

public sealed class DbcSignalDraft : DraftEntity
{
    private string _name = "Signal", _unit = "", _comment = "";
    private int _startBit, _bitLength = 8;
    private double _factor = 1, _offset;
    public string Name { get => _name; set => Set(ref _name, value); }
    public int StartBit { get => _startBit; set => Set(ref _startBit, value); }
    public int BitLength { get => _bitLength; set => Set(ref _bitLength, value); }
    public double Factor { get => _factor; set => Set(ref _factor, value); }
    public double Offset { get => _offset; set => Set(ref _offset, value); }
    public string Unit { get => _unit; set => Set(ref _unit, value); }
    public string Comment { get => _comment; set => Set(ref _comment, value); }
}

public sealed class DbcMessageDraft : DraftEntity
{
    private string _name = "NewMessage", _idHex = "100";
    private int _length = 8;
    public string Name { get => _name; set => Set(ref _name, value); }
    public string IdHex { get => _idHex; set => Set(ref _idHex, value); }
    public int Length { get => _length; set => Set(ref _length, value); }
    public ObservableCollection<DbcSignalDraft> Signals { get; set; } = [];
}

public sealed class WaveSignalDraft : DraftEntity
{
    private string _name = "新信号", _source = "", _unit = "";
    private bool _enabled = true;
    public string Name { get => _name; set => Set(ref _name, value); }
    public string Source { get => _source; set => Set(ref _source, value); }
    public string Unit { get => _unit; set => Set(ref _unit, value); }
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
}

public sealed class InputStateDraft : DraftEntity
{
    private string _name = "输入状态", _description = "", _voltageNote = "";
    public string Name { get => _name; set => Set(ref _name, value); }
    public string Description { get => _description; set => Set(ref _description, value); }
    public string VoltageNote { get => _voltageNote; set => Set(ref _voltageNote, value); }
    public ObservableCollection<KeyCell> Keys { get; set; } =
        new(Enumerable.Range(1, 20).Select(i => new KeyCell { Number = i }));
}

public sealed class UiDraftFile
{
    public string ProjectLabel { get; set; } = "界面示例工程";
    public ObservableCollection<CaseDraft> Cases { get; set; } = [];
    public ObservableCollection<CanDraft> CanRows { get; set; } = [];
    public ObservableCollection<KeyActionDraft> KeyActions { get; set; } = [];
    public ObservableCollection<UdsRequestDraft> UdsRequests { get; set; } = [];
    public ObservableCollection<DbcMessageDraft> DbcMessages { get; set; } = [];
    public ObservableCollection<WaveSignalDraft> WaveSignals { get; set; } = [];
    public ObservableCollection<InputStateDraft> InputStates { get; set; } = [];
    public string FlashFilePath { get; set; } = "";
    public string FlashProfile { get; set; } = "";
    public string WaveReferenceName { get; set; } = "";
}
