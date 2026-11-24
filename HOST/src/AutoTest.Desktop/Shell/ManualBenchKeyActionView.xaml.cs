using System;
using System.Windows;
using System.Windows.Controls;

namespace AutoTest.Desktop.Shell;

/// <summary>KEY action edit form hosted by the manual bench overlay, not a child window.</summary>
public partial class ManualBenchKeyActionView : UserControl
{
    public event EventHandler? CloseRequested;

    public ManualBenchKeyActionView(ManualBenchViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        DataContext = model;
        InitializeComponent();
    }

    private void SaveAndClose_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);

}
