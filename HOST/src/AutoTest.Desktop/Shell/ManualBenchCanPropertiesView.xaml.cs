using System;
using System.Windows;
using System.Windows.Controls;
using AutoTest.Desktop.Testing;

namespace AutoTest.Desktop.Shell;

/// <summary>Only the contents of a CAN property editor; the bench owns its overlay.</summary>
public partial class ManualBenchCanPropertiesView : UserControl
{
    private readonly ManualBenchViewModel _model;
    public event EventHandler? CloseRequested;

    public ManualBenchCanPropertiesView(ManualBenchViewModel model)
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
        SignalSearch.Clear();
        row.DbcMessage = e.Choice;
        BenchDbcPicker.SetBenchChoices(_model.DbcMessages, row.DbcMessage);
    }

    private void SignalSearch_Changed(object sender, TextChangedEventArgs e)
    {
        if (SignalList is null) return;
        var query = SignalSearch.Text.Trim();
        SignalList.Items.Filter = item => query.Length == 0 ||
            item is ManualBenchViewModel.DbcSignalValue signal &&
            (signal.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             (signal.Definition?.ToString() ?? "").Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private void ClearDbc_Click(object sender, RoutedEventArgs e)
    {
        if (_model.SelectedCanRow is not { } row || !row.CanEditFields) return;
        SignalSearch.Clear();
        row.ClearDbcAssistance();
        BenchDbcPicker.SetBenchChoices(_model.DbcMessages, null);
    }

    private void Close_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);
}
