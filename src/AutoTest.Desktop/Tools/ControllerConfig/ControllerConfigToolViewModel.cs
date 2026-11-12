using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using AutoTest.Projects.ControllerConfig;

namespace AutoTest.Desktop.Tools.ControllerConfig;

public sealed class ToolCommand(Action action) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => action();
    public void Requery() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>Editable visual switch bit, not an independent hardware output.</summary>
public sealed class KeyBitChoice : INotifyPropertyChanged
{
    private bool _isChecked;
    private readonly Action<int, bool> _changed;
    public int Number { get; }
    public string Label => $"K{Number:D2}";
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value) return;
            _isChecked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
            _changed(Number, value);
        }
    }
    public KeyBitChoice(int number, Action<int, bool> changed)
    {
        Number = number;
        _changed = changed;
    }
    public void SetFromMask(bool value)
    {
        if (_isChecked == value) return;
        _isChecked = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class ControllerConfigToolViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? key = null) => PropertyChanged?.Invoke(this, new(key));
    private ControllerEditSession? _session;
    private string? _lastSha;
    private ControllerDefinition? _selectedController;
    private ControllerState? _selectedState;
    private string _status = "尚未打开配置 / Unknown";
    private string _lastError = "";

    public ObservableCollection<ControllerDefinition> Controllers { get; } = [];
    public ObservableCollection<ControllerState> States { get; } = [];
    public ObservableCollection<VoltageWindow> VoltageWindows { get; } = [];
    public ObservableCollection<VoltageMeasurement> Measurements { get; } = [];
    public string FilePath { get; set; } = "";
    public string EcuName { get; set; } = "";
    public string BoardModel { get; set; } = "";
    public string HardwareProfileId { get; set; } = "";
    public string ControllerName { get; set; } = "";
    public string StateName { get; set; } = "";
    private string _stateMaskHex = "0x0";
    public ObservableCollection<KeyBitChoice> KeyBits { get; } = new();
    public string StateMaskHex
    {
        get => _stateMaskHex;
        set
        {
            if (_stateMaskHex == value) return;
            _stateMaskHex = value;
            Changed();
            UpdateKeySelections();
        }
    }
    private void UpdateKeySelections()
    {
        var input = _stateMaskHex.Trim();
        if (input.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) input = input[2..];
        if (!uint.TryParse(input, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var mask))
            return; // Editing an incomplete HEX string must not silently erase it.
        foreach (var bit in KeyBits)
            bit.SetFromMask((mask & (1u << (bit.Number - 1))) != 0);
    }
    private void SetKeyBit(int number, bool selected)
    {
        var input = _stateMaskHex.Trim();
        if (input.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) input = input[2..];
        if (!uint.TryParse(input, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var mask))
        {
            Error = "请先修正十六进制按键掩码，再编辑开关组合。";
            var attempted = KeyBits.FirstOrDefault(bit => bit.Number == number);
            attempted?.SetFromMask(!selected); // Invalid input must not make UI lie about stored bit state.
            return;
        }
        var flag = 1u << (number - 1);
        mask = selected ? mask | flag : mask & ~flag;
        StateMaskHex = $"0x{mask:X5}";
    }
    public string VoltageChannel { get; set; } = "1";
    public string TargetVolts { get; set; } = "";
    public string MinVolts { get; set; } = "";
    public string MaxVolts { get; set; } = "";
    public string MeasuredVolts { get; set; } = "";
    public string OperatorId { get; set; } = "";
    public string EvidenceRef { get; set; } = "";
    public string InteractionChoice { get; set; } = "Momentary";
    public string? ReturnStateId { get; set; }
    public string ExclusiveGroup { get; set; } = "";
    public IReadOnlyList<string> Interactions { get; } = Enum.GetNames<ControlInteraction>();
    public EcuControlProfile? CurrentProfile => _session?.Current;
    public int CurrentRevision => CurrentProfile?.Revision ?? 0;
    public string Status { get => _status; private set { _status = value; Changed(); } }
    public string Error { get => _lastError; private set { _lastError = value; Changed(); } }
    public ControllerDefinition? SelectedController
    {
        get => _selectedController;
        set { _selectedController = value; Changed(); LoadController(); }
    }
    public ControllerState? SelectedState
    {
        get => _selectedState;
        set { _selectedState = value; Changed(); LoadState(); }
    }

    public ICommand CreateCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand AddControllerCommand { get; }
    public ICommand RenameControllerCommand { get; }
    public ICommand RemoveControllerCommand { get; }
    public ICommand AddStateCommand { get; }
    public ICommand UpdateStateCommand { get; }
    public ICommand RemoveStateCommand { get; }
    public ICommand SetDefaultCommand { get; }
    public ICommand SetWindowCommand { get; }
    public ICommand RecordVoltageCommand { get; }
    public ICommand UndoCommand { get; }

    public ControllerConfigToolViewModel()
    {
        for (var number = 1; number <= 20; number++)
            KeyBits.Add(new KeyBitChoice(number, SetKeyBit));
        UpdateKeySelections();
        CreateCommand = new ToolCommand(() => Guard(Create));
        OpenCommand = new ToolCommand(() => _ = OpenGuardedAsync());
        SaveCommand = new ToolCommand(() => _ = SaveGuardedAsync());
        AddControllerCommand = new ToolCommand(() => Guard(() => { Session.AddController(ControllerName); Refresh(); }));
        RenameControllerCommand = new ToolCommand(() => Guard(() => { Session.RenameController(NeedController(), ControllerName); Refresh(); }));
        RemoveControllerCommand = new ToolCommand(() => Guard(() => { Session.RemoveController(NeedController()); Refresh(); }));
        AddStateCommand = new ToolCommand(() => Guard(() => { Session.AddState(NeedController(), StateName, ParseMask()); Refresh(); }));
        UpdateStateCommand = new ToolCommand(() => Guard(UpdateState));
        RemoveStateCommand = new ToolCommand(() => Guard(() => { Session.RemoveState(NeedController(), NeedState()); Refresh(); }));
        SetDefaultCommand = new ToolCommand(() => Guard(() => { Session.SetDefault(NeedController(), NeedState()); Refresh(); }));
        SetWindowCommand = new ToolCommand(() => Guard(SetVoltageWindow));
        RecordVoltageCommand = new ToolCommand(() => Guard(RecordVoltage));
        UndoCommand = new ToolCommand(() => Guard(() => { Session.Undo(); Refresh(); }));
    }

    private ControllerEditSession Session => _session ?? throw new InvalidOperationException("请先创建或打开 ECU 配置。");
    private Guid NeedController() => SelectedController?.Id ?? throw new InvalidOperationException("请先选择控制器。");
    private Guid NeedState() => SelectedState?.Id ?? throw new InvalidOperationException("请先选择状态。");
    private uint ParseMask()
    {
        var raw = StateMaskHex.Trim();
        if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) raw = raw[2..];
        if (!uint.TryParse(raw, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            throw new FormatException("Mask 必须是十六进制数，例如 0x00001。");
        return value;
    }
    private static double? Optional(string value) => string.IsNullOrWhiteSpace(value) ? null :
        double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
    private void Create()
    {
        var profile = EcuControlProfile.Create(EcuName, BoardModel, Guid.Parse(HardwareProfileId));
        _session = new ControllerEditSession(profile);
        _lastSha = null;
        Refresh();
    }
    private async Task OpenGuardedAsync()
    {
        try
        {
            Error = "";
            var loaded = await ControllerRepository.OpenAsync(FilePath);
            _session = new ControllerEditSession(loaded.Profile);
            _lastSha = loaded.Receipt.Sha256;
            EcuName = loaded.Profile.EcuName;
            BoardModel = loaded.Profile.HardwareModel;
            HardwareProfileId = loaded.Profile.HardwareProfileId.ToString("D");
            Changed(nameof(EcuName)); Changed(nameof(BoardModel)); Changed(nameof(HardwareProfileId));
            Refresh(); Status = "配置已打开（仅配置事实；没有连接硬件）";
        }
        catch (Exception e) { Error = e.Message; Status = "打开失败"; }
    }
    private async Task SaveGuardedAsync()
    {
        try
        {
            Error = "";
            var receipt = await ControllerRepository.SaveAsync(Session.Current, FilePath, _lastSha);
            _lastSha = receipt.Sha256;
            Status = $"已保存 Revision {receipt.Revision} / SHA256 {receipt.Sha256[..12]}…";
        }
        catch (Exception e) { Error = e.Message; Status = "保存失败（当前编辑未丢失）"; }
    }
    private void UpdateState()
    {
        var old = SelectedState ?? throw new InvalidOperationException("请先选择状态。");
        var choice = Enum.Parse<ControlInteraction>(InteractionChoice);
        Guid? target = string.IsNullOrWhiteSpace(ReturnStateId) ? null : Guid.Parse(ReturnStateId);
        Session.EditState(NeedController(), old with { Name = StateName, KeyMask = ParseMask(),
            Interaction = choice, ReturnStateId = target, ExclusiveGroup =
                string.IsNullOrWhiteSpace(ExclusiveGroup) ? null : ExclusiveGroup });
        Refresh();
    }
    private void SetVoltageWindow()
    {
        Session.SetWindow(NeedController(), NeedState(), new VoltageWindow(int.Parse(VoltageChannel),
            Optional(TargetVolts), Optional(MinVolts), Optional(MaxVolts)));
        Refresh();
    }
    private void RecordVoltage()
    {
        var fact = Session.RecordManualVoltage(NeedController(), NeedState(), int.Parse(VoltageChannel),
            double.Parse(MeasuredVolts, CultureInfo.InvariantCulture), OperatorId, EvidenceRef, DateTimeOffset.UtcNow);
        Refresh();
        Status = $"人工录入 {fact.ValueVolts} V：{fact.Status}（不是硬件自动读数）";
    }
    private void Guard(Action action)
    {
        try { Error = ""; action(); }
        catch (Exception e) { Error = e.Message; Status = "配置校验失败"; }
    }
    private void Refresh()
    {
        var controllerId = SelectedController?.Id;
        var stateId = SelectedState?.Id;
        Controllers.Clear();
        foreach (var controller in Session.Current.Controllers.OrderBy(c => c.Order)) Controllers.Add(controller);
        SelectedController = Controllers.FirstOrDefault(c => c.Id == controllerId) ?? Controllers.FirstOrDefault();
        SelectedState = States.FirstOrDefault(s => s.Id == stateId) ?? States.FirstOrDefault();
        Changed(nameof(CurrentProfile)); Changed(nameof(CurrentRevision));
        Status = $"配置修订版本 {CurrentRevision} · 当前仅修改配置，未操作实际设备";
    }
    private void LoadController()
    {
        if (SelectedController is not null)
        {
            ControllerName = SelectedController.Name;
            Changed(nameof(ControllerName));
        }
        States.Clear();
        foreach (var state in SelectedController is null ? Enumerable.Empty<ControllerState>() : SelectedController.States.OrderBy(s => s.Order)) States.Add(state);
        SelectedState = States.FirstOrDefault();
    }
    private void LoadState()
    {
        StateName = SelectedState?.Name ?? "";
        StateMaskHex = $"0x{SelectedState?.KeyMask ?? 0:X5}";
        InteractionChoice = (SelectedState?.Interaction ?? ControlInteraction.Momentary).ToString();
        ReturnStateId = SelectedState?.ReturnStateId?.ToString("D");
        ExclusiveGroup = SelectedState?.ExclusiveGroup ?? "";
        Changed(nameof(StateName)); Changed(nameof(StateMaskHex)); Changed(nameof(InteractionChoice));
        Changed(nameof(ReturnStateId)); Changed(nameof(ExclusiveGroup));
        VoltageWindows.Clear(); Measurements.Clear();
        foreach (var window in SelectedState?.Windows ?? []) VoltageWindows.Add(window);
        foreach (var reading in SelectedState?.Measurements ?? []) Measurements.Add(reading);
    }
}
