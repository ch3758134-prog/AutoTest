using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AutoTest.UiPreview;

/// <summary>
/// State for the always-visible step inspector. Assignments update the selected
/// in-memory step and notify the row projection. No WPF element references.
/// </summary>
public sealed class PreviewInspectorState : INotifyPropertyChanged
{
    private DemoStep? _step;
    private Action? _stepChanged;

    public string Name
    {
        get => _step?.Name ?? "";
        set
        {
            if (_step is null || _step.Name == value) return;
            _step.Name = value;
            Raise(nameof(Name));
            _stepChanged?.Invoke();
        }
    }

    public int EnabledIndex
    {
        get => _step?.Enabled == false ? 1 : 0;
        set
        {
            if (_step is null || _step.Enabled == (value != 1)) return;
            _step.Enabled = value != 1;
            Raise(nameof(EnabledIndex));
            _stepChanged?.Invoke();
        }
    }

    public int BreakpointIndex
    {
        get => _step?.Breakpoint == true ? 1 : 0;
        set
        {
            if (_step is null || _step.Breakpoint == (value == 1)) return;
            _step.Breakpoint = value == 1;
            Raise(nameof(BreakpointIndex));
            _stepChanged?.Invoke();
        }
    }

    public bool HasStep => _step is not null;

    public void Bind(DemoStep? step, Action stepChanged)
    {
        _step = step;
        _stepChanged = stepChanged;
        Raise(nameof(Name));
        Raise(nameof(EnabledIndex));
        Raise(nameof(BreakpointIndex));
        Raise(nameof(HasStep));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? property = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
