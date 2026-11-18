using System.Globalization;
using AutoTest.Projects.Dbc;
using Prism.Mvvm;

namespace AutoTest.Desktop.Dbc;

/// <summary>
/// Common DBC-driven input model for TEST and standalone/manual CAN editors.
/// Names explain the UI; only the original DBC signal name is used for encoding.
/// VAL_ entries are raw integers and must be converted to physical values first.
/// </summary>
public record DbcSignalChoice(string Value, string Label);

public class DbcEditableSignal : BindableBase
{
    private string _value;
    public DbcEditableSignal(DbcSignal signal, string? label, string? description,
        IReadOnlyDictionary<long, string>? definitions, string value = "", string? shortSummary = null)
    {
        Signal = signal;
        Name = signal.Name;
        DisplayName = string.IsNullOrWhiteSpace(label) ? signal.Name : label;
        Definition = (description ?? "").Trim();
        var summary = !string.IsNullOrWhiteSpace(shortSummary) ? shortSummary.Trim() :
            Definition.Split(['；', '。', '\n'], StringSplitOptions.TrimEntries)
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";
        Summary = summary.Length <= 90 ? summary : summary[..90] + "…";
        Unit = signal.Unit;
        Range = signal.Minimum.ToString("G", CultureInfo.InvariantCulture) + " ～ " +
            signal.Maximum.ToString("G", CultureInfo.InvariantCulture) +
            (Unit.Length == 0 ? "" : " " + Unit);
        var options = new List<DbcSignalChoice>();
        if (definitions is not null)
        {
            foreach (var pair in definitions.OrderBy(x => x.Key))
            {
                if (!signal.Signed && pair.Key < 0) continue;
                ulong raw = unchecked((ulong)pair.Key);
                if (signal.BitLength < 64)
                    raw &= (1UL << signal.BitLength) - 1;
                var physical = DbcSignalCodec.ToPhysical(signal, raw);
                if (!double.IsFinite(physical) || physical < signal.Minimum ||
                    physical > signal.Maximum) continue;
                var text = physical.ToString("G17", CultureInfo.InvariantCulture);
                if (!options.Any(x => x.Value == text))
                    options.Add(new DbcSignalChoice(text, pair.Value + "（" + text +
                        (signal.Unit.Length == 0 ? "" : " " + signal.Unit) + "）"));
            }
        }
        Choices = options;
        _value = value;
    }

    public DbcSignal Signal { get; }
    public string Name { get; }
    public string DisplayName { get; }
    public string Summary { get; }
    public string Definition { get; }
    public string Unit { get; }
    public string Range { get; }
    public IReadOnlyList<DbcSignalChoice> Choices { get; }
    public bool HasChoices => Choices.Count > 0;
    public string Value { get => _value; set => SetProperty(ref _value, value ?? ""); }
}
