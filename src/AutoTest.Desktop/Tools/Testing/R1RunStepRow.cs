using System.ComponentModel;
using System.Runtime.CompilerServices;
using AutoTest.Testing.Lab;

namespace AutoTest.Desktop.Tools.Testing;

/// <summary>
/// UI projection of the exact TEST plan. A planned row is NOT an observed
/// measurement or PASS. Only real LabStepFact records may populate Actual
/// and Verdict; the engineering authoring document remains authoritative.
/// </summary>
public sealed class R1RunStepRow : INotifyPropertyChanged
{
    private string _actual = "—";
    private string _verdict = "未执行";
    private string _quality = "UNKNOWN";
    private string _source = "—";
    private string _trace = "";
    private string _diagnostic = "";
    private string _time = "";
    public Guid StepId { get; }
    public string Phase { get; }
    public string Name { get; }
    public string Type { get; }
    public string Expected { get; }
    public int Order { get; }
    public string Number => Order.ToString("00");
    public string Actual { get => _actual; private set => Set(ref _actual, value); }
    public string Verdict { get => _verdict; private set => Set(ref _verdict, value); }
    public string Quality { get => _quality; private set => Set(ref _quality, value); }
    public string Source { get => _source; private set => Set(ref _source, value); }
    public string TraceReference { get => _trace; private set => Set(ref _trace, value); }
    public string Diagnostic { get => _diagnostic; private set => Set(ref _diagnostic, value); }
    public string Time { get => _time; private set => Set(ref _time, value); }
    public event PropertyChangedEventHandler? PropertyChanged;

    public R1RunStepRow(LabNode node, LabPhase phase, int index, int depth)
    {
        StepId = node.Id;
        Phase = phase.ToString();
        Order = index;
        Name = new string('　', Math.Min(8, depth)) + node.Name;
        Type = node.Type;
        Expected = string.Join("  ·  ", node.Parameters
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Take(3).Select(pair => pair.Key + "=" + pair.Value));
        if (node.Type == "Subroutine")
            Verdict = "定义";
    }

    public void Reset()
    {
        Actual = "—";
        Verdict = "未执行";
        Quality = "UNKNOWN";
        Source = "—";
        TraceReference = "";
        Diagnostic = "";
        Time = "";
    }
    public void Observe(LabStepFact fact)
    {
        if (fact.StepId != StepId) return;
        Actual = fact.Actual ?? "无实际观测";
        Verdict = fact.Verdict.ToString().ToUpperInvariant();
        Quality = fact.Quality.ToString().ToUpperInvariant();
        Source = fact.Source.ToString().ToUpperInvariant();
        TraceReference = fact.TraceReference ?? "";
        Diagnostic = fact.Diagnostic ?? "";
        Time = fact.AtUtc.ToLocalTime().ToString("HH:mm:ss.fff");
    }

    private void Set(ref string field, string value, [CallerMemberName] string? name = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
