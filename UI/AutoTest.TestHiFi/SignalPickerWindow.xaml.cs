using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace AutoTest.TestHiFi;

public sealed class PreviewSignal
{
    public string Message { get; init; } = "BCM_Status";
    public string Name { get; init; } = "";
    public string Type { get; init; } = "无符号数";
    public int Bits { get; init; } = 1;
    public string Unit { get; init; } = "—";
    public string Description { get; init; } = "";
    public int StartBit { get; init; }
}

public partial class SignalPickerWindow : Window
{
    private readonly ObservableCollection<PreviewSignal> _signals =
    [
        new() { Name="BCM.DoorSts", Message="BCM_Status",Bits=2,StartBit=0,Description="车门状态：0=关闭，1=开启，2=未知" },
        new() { Name="BCM.ApplyState",Message="BCM_Status",Bits=2,StartBit=2,Description="驻车执行状态（演示）" },
        new() { Name="BCM.MotorCurrent",Message="BCM_Status",Bits=12,StartBit=8,Unit="A",Description="驱动电机电流反馈（演示）" },
        new() { Name="BCM.Mode",Message="BCM_Status",Type="枚举",Bits=3,StartBit=20,Description="ECU 当前工作模式（演示）" },
        new() { Name="BCM.FaultCode",Message="BCM_Status",Bits=8,StartBit=24,Description="当前故障代码（演示）" },
        new() { Name="BCM.LightSts",Message="BCM_Light",Type="枚举",Bits=2,StartBit=0,Description="灯光状态（演示）" },
        new() { Name="BCM.AliveCounter",Message="BCM_Status",Bits=4,StartBit=40,Description="计数器（演示）" }
    ];
    public string? SelectedSignal { get; private set; }
    public SignalPickerWindow()
    {
        InitializeComponent();
        Signals.ItemsSource = _signals;
        Signals.SelectedIndex = 0;
    }
    private void Signals_SelectionChanged(object sender, SelectionChangedEventArgs e) { }
    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (Signals?.ItemsSource is null) return;
        var term = Search.Text.Trim();
        var view = CollectionViewSource.GetDefaultView(_signals);
        view.Filter = entry => entry is PreviewSignal s &&
            (term.Length == 0 ||
             s.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
             s.Message.Contains(term, StringComparison.OrdinalIgnoreCase) ||
             s.Description.Contains(term, StringComparison.OrdinalIgnoreCase));
        view.Refresh();
    }
    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        if (Signals.SelectedItem is not PreviewSignal signal)
        {
            MessageBox.Show(this,"请先选择一个演示信号。","选择信号",MessageBoxButton.OK,MessageBoxImage.Information);
            return;
        }
        SelectedSignal = signal.Name;
        DialogResult = true;
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
