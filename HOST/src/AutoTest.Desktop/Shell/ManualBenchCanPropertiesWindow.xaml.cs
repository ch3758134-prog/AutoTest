using System.Windows;
using AutoTest.Desktop.Testing;

namespace AutoTest.Desktop.Shell;

/// <summary>Full, non-transmitting CAN editor with shared DBC signal picker.</summary>
public partial class ManualBenchCanPropertiesWindow : Window
{
    private readonly ManualBenchViewModel _model;
    public ManualBenchCanPropertiesWindow(ManualBenchViewModel model)
    {
        if (model.SelectedCanRow is null)
            throw new InvalidOperationException("请先选择 CAN 报文。");
        _model = model;
        DataContext = model;
        InitializeComponent();
        model.SelectedCanRow.AttachUniqueMatchingDbc();
        BenchDbcPicker.SetBenchChoices(model.DbcMessages, model.SelectedCanRow.DbcMessage);
    }

    private void BenchDbcPicker_ChoiceCommitted(
        object? sender, BenchDbcReferenceChosenEventArgs e)
    {
        if (_model.SelectedCanRow is not { } row || !row.CanEditFields) return;
        row.DbcMessage = e.Choice;
        BenchDbcPicker.SetBenchChoices(_model.DbcMessages, row.DbcMessage);
    }

    private void ClearDbc_Click(object sender, RoutedEventArgs e)
    {
        if (_model.SelectedCanRow is not { } row || !row.CanEditFields) return;
        row.ClearDbcAssistance();
        BenchDbcPicker.SetBenchChoices(_model.DbcMessages, null);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
