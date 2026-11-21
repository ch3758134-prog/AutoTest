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
        row.DbcMessage = e.Choice;
        BenchDbcPicker.SetBenchChoices(_model.DbcMessages, row.DbcMessage);
    }

    private void ClearDbc_Click(object sender, RoutedEventArgs e)
    {
        if (_model.SelectedCanRow is not { } row || !row.CanEditFields) return;
        row.ClearDbcAssistance();
        BenchDbcPicker.SetBenchChoices(_model.DbcMessages, null);
    }

    private void Close_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);
}
