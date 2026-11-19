using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AutoTest.Frontend;

// UI exploration data only. Not PROJECT, TEST DSL, CAN frames, USB commands,
// persisted ECU configurations or a substitute for production domain contracts.
public abstract class DraftObject : INotifyPropertyChanged
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

public sealed class CaseDraft : DraftObject
{
    private string _name = "新测试用例";
    public string Name { get => _name; set => Set(ref _name, value); }
    public ObservableCollection<StepDraft> Steps { get; set; } = [];
}
public sealed class StepDraft : DraftObject
{
    private string _phase = "测试", _kind = "CAN 发送", _name = "新步骤";
    private string _target = "", _value = "", _note = "";
    private bool _enabled = true;
    public string Phase { get => _phase; set => Set(ref _phase, value); }
    public string Kind { get => _kind; set => Set(ref _kind, value); }
    public string Name { get => _name; set => Set(ref _name, value); }
    public string Target { get => _target; set => Set(ref _target, value); }
    public string Value { get => _value; set => Set(ref _value, value); }
    public string Note { get => _note; set => Set(ref _note, value); }
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
}
public sealed class CanDraft : DraftObject
{
    private string _name = "新报文", _id = "", _data = "";
    private bool _included = true;
    public string Name { get => _name; set => Set(ref _name, value); }
    public string Id { get => _id; set => Set(ref _id, value); }
    public string Data { get => _data; set => Set(ref _data, value); }
    public bool Included { get => _included; set => Set(ref _included, value); }
}
public sealed class KeyCell : DraftObject
{
    private bool _checked;
    public int Number { get; set; }
    public bool Checked { get => _checked; set => Set(ref _checked, value); }
    public string Label => "K" + Number.ToString("00");
}
public sealed class KeyStepDraft : DraftObject
{
    private int _holdMs = 200;
    public int HoldMs { get => _holdMs; set => Set(ref _holdMs, value); }
    public ObservableCollection<KeyCell> Keys { get; set; } =
        new(Enumerable.Range(1,20).Select(i => new KeyCell { Number = i }));
}
public sealed class KeyActionDraft : DraftObject
{
    private string _name = "新按键动作";
    public string Name { get => _name; set => Set(ref _name, value); }
    public ObservableCollection<KeyStepDraft> Steps { get; set; } = [];
}
public sealed class FrontendDraftDocument
{
    public List<CaseDraft> Cases { get; set; } = [];
    public List<CanDraft> CanRows { get; set; } = [];
    public List<KeyActionDraft> KeyActions { get; set; } = [];
}
