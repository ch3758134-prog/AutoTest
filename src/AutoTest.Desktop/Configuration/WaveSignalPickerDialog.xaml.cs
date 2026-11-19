using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutoTest.Desktop.Testing;
using AutoTest.Testing.Authoring;

namespace AutoTest.Desktop.Configuration;

/// <summary>Choose a DBC-backed Signal mapping, without typing arbitration IDs or units.</summary>
public partial class WaveSignalPickerDialog : Window
{
    private readonly IReadOnlyList<MappingChoice> _all;
    public ObservableCollection<MappingChoice> Filtered { get; } = new();
    public TestTargetMapping? Selected { get; private set; }

    public WaveSignalPickerDialog(IReadOnlyList<MappingChoice> choices)
    {
        _all = choices ?? throw new ArgumentNullException(nameof(choices));
        InitializeComponent();
        DataContext = this;
        Refresh("");
    }

    private void Refresh(string filter)
    {
        var selectedName = (SignalList.SelectedItem as MappingChoice)?.Mapping.Name;
        Filtered.Clear();
        foreach (var item in _all.Where(x => x.Label.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            Filtered.Add(item);
        SignalList.SelectedItem = Filtered.FirstOrDefault(x => x.Mapping.Name == selectedName)
            ?? Filtered.FirstOrDefault();
        CountLabel.Text = "找到 " + Filtered.Count +
            " 个信号。选择后会同步 DBC 资源、报文 ID、信号名及物理单位。";
    }

    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        // XAML may raise a TextChanged event before later named controls exist.
        if (SignalList is null || CountLabel is null) return;
        Refresh(SearchBox.Text.Trim());
    }

    private void SignalList_DoubleClick(object sender, MouseButtonEventArgs e) =>
        Confirm();

    private void Confirm_Click(object sender, RoutedEventArgs e) => Confirm();

    private void Confirm()
    {
        if (SignalList.SelectedItem is not MappingChoice item) return;
        Selected = item.Mapping;
        DialogResult = true;
    }
}
