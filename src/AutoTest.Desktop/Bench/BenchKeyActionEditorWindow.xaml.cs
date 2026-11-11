using System;
using System.Windows;
using System.Windows.Threading;

namespace AutoTest.Desktop.Bench;

/// <summary>
/// The user's right-click action editor. It edits the SAME project-backed
/// action viewmodel as the manual bench cards, without creating new hardware
/// sessions or a separate set of KEY_SIM switch states.
/// </summary>
public partial class BenchKeyActionEditorWindow : Window
{
    public BenchKeyActionEditorWindow(BenchKeyActionEditorViewModel actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        InitializeComponent();
        DataContext = actions;
    }

    public void FocusActionName()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            ActionNameBox.Focus();
            ActionNameBox.SelectAll();
        }), DispatcherPriority.Input);
    }

    private void CloseEditor_Click(object sender, RoutedEventArgs e) => Close();
}
