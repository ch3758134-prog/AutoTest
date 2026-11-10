using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutoTest.Specialists.Scan;

namespace AutoTest.Desktop.Tools.Production;

/// <summary>
/// Product P12 combines real serial barcode acquisition with the existing
/// production catalog/recipe editor. No physical EOL run starts on scan.
/// </summary>
public partial class LabProductionToolView : UserControl, IAsyncDisposable
{
    private readonly SerialScannerSession _serial = new();
    private long _totalRx;
    private bool _closed;

    public LabProductionToolView() : this(new LabProductionToolViewModel()) { }
    public LabProductionToolView(LabProductionToolViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        R1ScannerBaud.ItemsSource = new [] {9600,19200,38400,57600,115200,230400};
        R1ScannerBaud.SelectedItem = 9600;
        RefreshPorts();
        _serial.BytesReceived += data => Dispatcher.BeginInvoke(() =>
        {
            var n=Interlocked.Add(ref _totalRx,data.Length);
            R1ScannerRxCount.Text=$"RX {n:N0} bytes";
            R1ScannerHex.Text="HEX "+Convert.ToHexString(data.AsSpan(0,Math.Min(data.Length,80)));
        });
        _serial.BarcodeReceived += packet => Dispatcher.BeginInvoke(() =>
        {
            try
            {
                var text = new UTF8Encoding(false,true).GetString(packet.Data.AsSpan());
                R1ScannerBarcode.Text="LAST BARCODE "+text;
                R1ScannerBoundary.Text=$"完整条码 / {packet.Boundary}";
                if (DataContext is LabProductionToolViewModel model)
                    model.AcceptSerialBarcode(text, "USB COM · "+(_serial.PortName ?? "disconnected"));
            }
            catch(DecoderFallbackException ex)
            {
                R1ScannerState.Text="编码不符合 UTF-8；原始 HEX 已显示，请检查设备字符编码："+ex.Message;
            }
        });
        _serial.Fault += e => Dispatcher.BeginInvoke(() => R1ScannerState.Text="串口接收停止："+e);
        _serial.FramingWarning += e => Dispatcher.BeginInvoke(() => R1ScannerState.Text="无效扫码帧："+e);
        _serial.ConnectionChanged += connected => Dispatcher.BeginInvoke(() =>
        {
            bool has = _serial.HasSession;
            R1ScannerConnect.IsEnabled=!has;
            R1ScannerDisconnect.IsEnabled=has;
            R1ScannerPorts.IsEnabled=!has;
            R1ScannerBaud.IsEnabled=!has;
            if (!connected) R1ScannerState.Text="扫码串口已停止读取；断开后可重新连接。";
        });
    }

    private void RefreshPorts()
    {
        try
        {
            var selected=R1ScannerPorts.SelectedItem as string;
            var ports=SerialScannerSession.AvailablePorts();
            R1ScannerPorts.ItemsSource=ports;
            if (selected is not null && ports.Contains(selected,StringComparer.OrdinalIgnoreCase))
                R1ScannerPorts.SelectedItem=selected;
            else if (ports.Length==1) R1ScannerPorts.SelectedIndex=0;
            R1ScannerState.Text=ports.Length==0
                ? "Windows 当前无可用串口；确认 USB 串行设备驱动已安装。"
                : $"检测到 {string.Join(", ",ports)}；选择扫描枪对应 COM。";
        }
        catch(Exception ex) { R1ScannerState.Text="串口发现失败："+ex.Message; }
    }
    private void ScannerRefresh_Click(object sender,RoutedEventArgs e) => RefreshPorts();
    private void ScannerConnect_Click(object sender,RoutedEventArgs e)
    {
        try
        {
            if (R1ScannerPorts.SelectedItem is not string port)
                throw new InvalidOperationException("必须选择真实扫码枪 COM。");
            if (R1ScannerBaud.SelectedItem is not int baud)
                throw new InvalidOperationException("必须选择扫码枪实际波特率。");
            _serial.Connect(port,baud);
            Interlocked.Exchange(ref _totalRx,0);
            R1ScannerRxCount.Text="RX 0 bytes";
            R1ScannerState.Text=$"{port} 已连接 · {baud} bps · 8N1 · 等待实际扫码";
        }
        catch(Exception ex) { R1ScannerState.Text="连接失败："+ex.Message; }
    }
    private async void ScannerDisconnect_Click(object sender,RoutedEventArgs e)
    {
        try
        {
            await _serial.DisconnectAsync();
            R1ScannerState.Text="扫码枪 COM 已断开；不影响主控 WinUSB。";
        }
        catch(Exception ex) { R1ScannerState.Text="断开失败："+ex.Message; }
    }
    private void BarcodeInput_OnKeyDown(object sender,KeyEventArgs e)
    {
        if (e.Key!=Key.Enter) return;
        e.Handled=true;
        if(DataContext is LabProductionToolViewModel vm) vm.PreviewScan();
    }
    public async ValueTask DisposeAsync()
    {
        if (_closed) return;
        _closed=true;
        await _serial.DisposeAsync();
    }
}
