using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AutoTest.UiPreview;

/// <summary>
/// In-memory test-case selection for the approved preview tree. Both the tree
/// checkbox and the editor header bind to the same property. Not a run command.
/// </summary>
public sealed class PreviewCaseSelectionState : INotifyPropertyChanged
{
    private bool _first = true;
    private bool _second = true;

    public bool First
    {
        get => _first;
        set
        {
            if (_first == value) return;
            _first = value;
            Raise();
            Raise(nameof(CountLabel));
            if (IsMainActive) Raise(nameof(CurrentSelected));
        }
    }

    public bool Second
    {
        get => _second;
        set
        {
            if (_second == value) return;
            _second = value;
            Raise();
            Raise(nameof(CountLabel));
            if (IsDiagnosticActive) Raise(nameof(CurrentSelected));
        }
    }

    private string _activeKey = "main";
    public string ActiveKey => _activeKey;
    public bool IsMainActive => _activeKey == "main";
    public bool IsDiagnosticActive => _activeKey == "diagnostic";
    public bool CurrentSelected
    {
        get => IsMainActive ? First : Second;
        set { if (IsMainActive) First = value; else Second = value; }
    }
    public void Activate(string key)
    {
        if (_activeKey == key || key is not ("main" or "diagnostic")) return;
        _activeKey = key;
        Raise(nameof(ActiveKey));
        Raise(nameof(IsMainActive));
        Raise(nameof(IsDiagnosticActive));
        Raise(nameof(CurrentSelected));
    }
    public string CountLabel => (_first ? 1 : 0) + (_second ? 1 : 0) + "/2";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
