using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using EPB.Core.Communication;

namespace EPB_UsbTest;

// QUICK manual bench editable rows use the G4 station workbench semantics,
// not G4 Prism/Project services. No simulation or second USB connection is created.
public abstract class QuickBenchNotify : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
    protected void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class QuickBenchCanRow : QuickBenchNotify
{
    private string _name = "新 CAN 报文";
    private string _idText = "0x701";
    private string _dataText = "00 00 00 00 00 00 00 00";
    private string _dlcText = "8";
    private string _periodText = "100";
    private string _status = "未发送";
    private bool _included = true, _periodic, _extended, _useDbc;
    public string Name { get => _name; set => Set(ref _name, value ?? ""); }
    public string IdText { get => _idText; set => Set(ref _idText, value ?? ""); }
    public string DataText { get => _dataText; set => Set(ref _dataText, value ?? ""); }
    public string DlcText { get => _dlcText; set => Set(ref _dlcText, value ?? ""); }
    public string PeriodText { get => _periodText; set => Set(ref _periodText, value ?? ""); }
    public string Status { get => _status; set => Set(ref _status, value ?? ""); }
    public bool Included { get => _included; set => Set(ref _included, value); }
    public bool Periodic { get => _periodic; set => Set(ref _periodic, value); }
    public bool Extended { get => _extended; set => Set(ref _extended, value); }
    public bool UseDbc { get => _useDbc; set => Set(ref _useDbc, value); }
    public string? DbcDefinitionId { get; set; }
    public Dictionary<string, string> SignalValues { get; set; } = new(StringComparer.Ordinal);

    public QuickBenchCanRow Clone() => new()
    {
        Name = Name + " 副本", IdText = IdText, DataText = DataText, DlcText = DlcText,
        PeriodText = PeriodText, Periodic = Periodic, Extended = Extended, UseDbc = UseDbc,
        DbcDefinitionId = DbcDefinitionId,
        SignalValues = new Dictionary<string, string>(SignalValues, StringComparer.Ordinal)
    };
}

public sealed record QuickBenchDbcChoice(CanMessageDefinition Message)
{
    public string Display => CanDefinitionDisplay.FormatMessage(Message);
}

public sealed record QuickBenchEnumOption(string Label, string Value);

public sealed class QuickBenchSignalValue : QuickBenchNotify
{
    private string _value;
    private QuickBenchEnumOption? _selectedEnum;
    public CanSignalDefinition Signal { get; }
    public string Display { get; }
    public ObservableCollection<QuickBenchEnumOption> EnumOptions { get; }
    public string Value
    {
        get => _value;
        set
        {
            if (Set(ref _value, value ?? "")) Changed(nameof(SelectedEnum));
        }
    }
    public QuickBenchEnumOption? SelectedEnum
    {
        get => _selectedEnum;
        set
        {
            if (!Set(ref _selectedEnum, value)) return;
            if (value is not null) Value = value.Value;
        }
    }
    public QuickBenchSignalValue(CanSignalDefinition signal, string initial)
    {
        Signal = signal;
        Display = string.IsNullOrWhiteSpace(signal.DisplayName) || signal.DisplayName == signal.Name
            ? signal.Name : signal.DisplayName + " / " + signal.Name;
        _value = initial;
        EnumOptions = new ObservableCollection<QuickBenchEnumOption>(
            signal.ValueTable.OrderBy(e => e.Key).Select(e =>
                new QuickBenchEnumOption(
                    e.Key.ToString(CultureInfo.InvariantCulture) + " · " + e.Value,
                    (e.Key * signal.Factor + signal.Offset).ToString("G", CultureInfo.InvariantCulture))));
    }
}

public sealed class QuickBenchKeyChoice : QuickBenchNotify
{
    private bool _checked;
    public int Number { get; }
    public string Label => "K" + Number.ToString("00", CultureInfo.InvariantCulture);
    public bool Checked { get => _checked; set => Set(ref _checked, value); }
    public QuickBenchKeyChoice(int number) => Number = number;
}

public sealed class QuickBenchKeyStep : QuickBenchNotify
{
    private uint _mask;
    private int _holdMs;
    public uint Mask
    {
        get => _mask;
        set
        {
            if (Set(ref _mask, value)) Changed(nameof(Summary));
        }
    }
    public int HoldMs
    {
        get => _holdMs;
        set
        {
            if (Set(ref _holdMs, value)) Changed(nameof(Summary));
        }
    }
    public string Summary
    {
        get
        {
            var keys = Mask == 0 ? "全部断开" :
                string.Join("+", Enumerable.Range(1, 20).Where(n => (Mask & (1u << (n - 1))) != 0)
                    .Select(n => "K" + n.ToString("00", CultureInfo.InvariantCulture)));
            return "0x" + Mask.ToString("X5", CultureInfo.InvariantCulture) + " · " + keys + " · " +
                   HoldMs.ToString(CultureInfo.InvariantCulture) + " ms";
        }
    }
    public QuickBenchKeyStep() { }
    public QuickBenchKeyStep(uint mask, int holdMs) { _mask = mask; _holdMs = holdMs; }
    public QuickBenchKeyStep Clone() => new(Mask, HoldMs);
}

public sealed class QuickBenchKeyAction : QuickBenchNotify
{
    private string _name = "按键动作";
    public string Name { get => _name; set => Set(ref _name, value ?? ""); }
    public ObservableCollection<QuickBenchKeyStep> Steps { get; set; } = new();
}

public sealed class QuickBenchMonitorRow : QuickBenchNotify
{
    private string _dataText = "", _periodText = "—", _lastTime = "";
    private int _count;
    private long _lastTick;
    public string Channel { get; }
    public string IdText { get; }
    public string DataText { get => _dataText; private set => Set(ref _dataText, value); }
    public string PeriodText { get => _periodText; private set => Set(ref _periodText, value); }
    public string LastTime { get => _lastTime; private set => Set(ref _lastTime, value); }
    public int Count { get => _count; private set => Set(ref _count, value); }
    public QuickBenchMonitorRow(CanFrame frame)
    {
        Channel = "CAN" + frame.Channel.ToString(CultureInfo.InvariantCulture);
        IdText = frame.IdText;
        Apply(frame);
    }
    public void Apply(CanFrame frame)
    {
        var now = frame.TimeMs;
        if (_lastTick > 0 && now > _lastTick)
            PeriodText = (now - _lastTick).ToString(CultureInfo.InvariantCulture) + " ms";
        _lastTick = now;
        DataText = frame.DataText;
        LastTime = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
        Count++;
    }
}

public sealed record QuickBenchRxCapture(DateTime CapturedAt, byte Channel, bool Extended, uint Id, byte Dlc, string Data);
public sealed record QuickBenchActionData(string Name, List<QuickBenchStepData> Steps);
public sealed record QuickBenchStepData(uint Mask, int HoldMs);
