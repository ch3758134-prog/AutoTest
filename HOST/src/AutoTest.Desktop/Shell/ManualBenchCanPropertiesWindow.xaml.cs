using System.Windows;

namespace AutoTest.Desktop.Shell;

/// <summary>Full report configuration; quick operations stay in a transient Popup.</summary>
public partial class ManualBenchCanPropertiesWindow : Window
{
    public ManualBenchCanPropertiesWindow(ManualBenchViewModel model)
    {
        if (model.SelectedCanRow is null)
            throw new InvalidOperationException("请先选择 CAN 报文。");
        DataContext = model;
        InitializeComponent();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
