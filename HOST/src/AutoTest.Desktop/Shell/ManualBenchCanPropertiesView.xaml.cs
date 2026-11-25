using System;
using System.Windows;
using System.Windows.Controls;
using AutoTest.Desktop.Testing;

namespace AutoTest.Desktop.Shell;

public sealed record ManualBenchCanEditorContext(ManualBenchViewModel.BenchCanRow SelectedCanRow);

/// <summary>
/// Shared detailed CAN editor, pinned to ONE specific row. The source row must
/// never silently switch merely because the engineer clicks another row in
/// the underlying bench while its floating editor remains open.
/// </summary>
public partial class ManualBenchCanPropertiesView : UserControl
{
    private readonly ManualBenchViewModel _model;
    private readonly ManualBenchViewModel.BenchCanRow _editedRow;
    public event EventHandler? CloseRequested;

    public ManualBenchCanPropertiesView(ManualBenchViewModel model,
        ManualBenchViewModel.BenchCanRow? editedRow = null)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _editedRow = editedRow ?? model.SelectedCanRow ??
            throw new InvalidOperationException("请先选择 CAN 报文。");
        DataContext = new ManualBenchCanEditorContext(_editedRow);
        InitializeComponent();
        _editedRow.AttachUniqueMatchingDbc();
        BenchDbcPicker.SetBenchChoices(_model.DbcMessages, _editedRow.DbcMessage);
    }

    private void BenchDbcPicker_ChoiceCommitted(
        object? sender, BenchDbcReferenceChosenEventArgs e)
    {
        if (!_editedRow.CanEditFields) return;
        var row = _editedRow;
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
        if (!_editedRow.CanEditFields) return;
        var row = _editedRow;
        SignalSearch.Clear();
        row.ClearDbcAssistance();
        BenchDbcPicker.SetBenchChoices(_model.DbcMessages, null);
    }

    private void Close_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);
}
