using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using AutoTest.Devices.Diagnostics;
using AutoTest.Devices.Usb;

namespace AutoTest.Desktop.Tools.Flash.Physical;

/// <summary>Standalone physical diagnostic bench. One HardwareBenchController owns the
/// only USB session and CAN1 receiver. No mock responses are presented as ECU evidence.</summary>
public sealed class FlashLabWindow : Window
{
    private readonly HardwareBenchController _device = new();
    private readonly ComboBox _devices = new() { MinWidth = 550, Margin = new Thickness(5) };
    private readonly TextBox _trace = new() { IsReadOnly=true, FontFamily=new FontFamily("Consolas"),
        FontSize=12, AcceptsReturn=true, TextWrapping=TextWrapping.NoWrap,
        VerticalScrollBarVisibility=ScrollBarVisibility.Auto, HorizontalScrollBarVisibility=ScrollBarVisibility.Auto };
    private readonly TextBlock _state = new() { Text="DISCONNECTED · physical CAN1 DUT RX only",
        Foreground=Brushes.DarkOrange, Margin=new Thickness(6) };
    private readonly TextBox _config = new() { IsReadOnly=true, Height=130,
        TextWrapping=TextWrapping.Wrap, VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
    private readonly TextBox _firmware = new() { IsReadOnly=true, Height=95,
        TextWrapping=TextWrapping.Wrap, VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
    private readonly TextBox _steps = new() { IsReadOnly=true, AcceptsReturn=true,
        VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
        Text="0. Detect APP / Boot mode (distinct CAN IDs)\n1. Enter programming session\n2. Seed/Key from approved DLL\n3. Erase ECU flash\n4. Download each address block: 34 / 36 / 37 / 31 FF01\n5. ECU reset and post-program verification\n\nPHYSICAL WRITE DISABLED pending CAN1 ISO-TP transport and ECU protocol validation." };
    private CanToolFlashProfile? _profile;
    private string? _imagePath;
    private int _traceRows;
    private bool _busy;

    public FlashLabWindow()
    {
        Title="AutoTest Flash Lab · USB / CAN1 / UDS / FLASH";
        Width=1240; Height=840; MinWidth=950; MinHeight=650;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;
        Background=Brushes.WhiteSmoke;
        var root=new DockPanel { LastChildFill=true, Margin=new Thickness(14) };
        Content=root;
        var title=new StackPanel { Margin=new Thickness(0,0,0,10) };
        title.Children.Add(new TextBlock { Text="AutoTest Flash Lab", FontSize=23, FontWeight=FontWeights.Bold });
        title.Children.Add(new TextBlock { Text="USB main controller → physical CAN1 → DUT; CAN0 belongs to KEY_SIM. No simulated ECU evidence.",
            Foreground=Brushes.DimGray });
        DockPanel.SetDock(title,Dock.Top);root.Children.Add(title);

        var bar=new WrapPanel { Margin=new Thickness(0,0,0,10) };
        DockPanel.SetDock(bar,Dock.Top);root.Children.Add(bar);
        bar.Children.Add(Button("枚举 USB",async ()=>await RunAsync(EnumerateAsync)));
        bar.Children.Add(_devices);
        bar.Children.Add(Button("连接",async ()=>await RunAsync(ConnectAsync)));
        bar.Children.Add(Button("固件版本",async ()=>await RunAsync(ReadVersionAsync)));
        bar.Children.Add(Button("GPIO 只读",async ()=>await RunAsync(ReadGpioAsync)));
        bar.Children.Add(Button("断开",async ()=>await RunAsync(DisconnectAsync)));
        DockPanel.SetDock(_state,Dock.Top);root.Children.Add(_state);
        var tabs=new TabControl();
        root.Children.Add(tabs);

        var connection=new Grid();
        connection.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        connection.RowDefinitions.Add(new RowDefinition());
        var note=new TextBlock { Text="物理 CAN1 接收监视：显示 USB 0x41 真实通知与端口；USB ACK 不等于 ECU 响应。",
            Margin=new Thickness(8),Foreground=Brushes.DarkSlateGray };
        connection.Children.Add(note);
        Grid.SetRow(_trace,1);connection.Children.Add(_trace);
        tabs.Items.Add(new TabItem { Header="USB / CAN1 实时监视",Content=connection });

        var cfgPanel=new StackPanel { Margin=new Thickness(12) };
        cfgPanel.Children.Add(Button("导入 CanTool Configuration.xml",async ()=>await RunAsync(LoadConfigAsync)));
        cfgPanel.Children.Add(new TextBlock { Text="配置：APP / Boot CAN ID、Flash 地址、块长、超时、Seed/Key DLL",Margin=new Thickness(0,8,0,4) });
        cfgPanel.Children.Add(_config);
        cfgPanel.Children.Add(Button("选择固件 HEX / S19 / BIN",async ()=>await RunAsync(LoadFirmwareAsync)));
        cfgPanel.Children.Add(new TextBlock { Text="镜像：文件大小与 SHA-256，仅做导入；实际地址段还需经过固件解析与 Profile 匹配。",Margin=new Thickness(0,8,0,4) });
        cfgPanel.Children.Add(_firmware);
        tabs.Items.Add(new TabItem { Header="ECU 配置 / 固件",Content=new ScrollViewer { Content=cfgPanel } });

        var programming=new DockPanel { Margin=new Thickness(12) };
        var warning=new TextBlock { Text="物理烧录受控：当前仅开放 USB 只读与 CAN1 接收。尚未接入真实 ISO-TP、UDS TX 和烧录授权，不允许假装烧录通过。",
            TextWrapping=TextWrapping.Wrap, Foreground=Brushes.DarkRed,Margin=new Thickness(0,0,0,12) };
        DockPanel.SetDock(warning,Dock.Top);programming.Children.Add(warning);
        programming.Children.Add(_steps);
        tabs.Items.Add(new TabItem { Header="UDS / FLASH 流程与门禁",Content=programming });

        Closed += async (_,_) => { try { await _device.DisposeAsync(); } catch { } };
        _device.UsbTrace += record => Append("USB",record.Kind+" SEQ="+record.Sequence+" CMD="+record.Command+" RAW="+record.RawHex);
        _device.CanTrace += record => Append("CAN", "port="+record.PhysicalPort+
            " role="+record.Role+" valid="+record.Valid+" GEN="+record.SessionGeneration+" "+record.RawUsbPayloadHex);
        _device.ConnectionChanged += state => Dispatcher.BeginInvoke(() => _state.Text="USB "+state+" · GEN="+_device.Generation);
    }

    private static Button Button(string caption,Func<Task> action)
    {
        var button=new Button { Content=caption,Padding=new Thickness(12,7,12,7),Margin=new Thickness(3) };
        button.Click += async (_,_) => await action();
        return button;
    }
    private void Append(string layer,string text)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => Append(layer,text)); return; }
        // Bounded UI text; authoritative unbounded raw evidence is the device trace file.
        if (_traceRows++>2500) { _trace.Clear();_traceRows=0; }
        _trace.AppendText(DateTimeOffset.Now.ToString("HH:mm:ss.fff")+" "+layer+" "+text+Environment.NewLine);
        _trace.ScrollToEnd();
    }
    private async Task RunAsync(Func<Task> action)
    {
        if (_busy) return;
        _busy=true;
        try { await action(); }
        catch(Exception ex) { Append("ERROR",ex.ToString()); MessageBox.Show(this,ex.Message,"Flash Lab",MessageBoxButton.OK,MessageBoxImage.Warning); }
        finally { _busy=false; }
    }
    private Task EnumerateAsync()
    {
        _devices.Items.Clear();
        foreach(var c in _device.Enumerate()) _devices.Items.Add(c);
        _devices.DisplayMemberPath=nameof(WinUsbCandidate.DisplayIdentity);
        if (_devices.Items.Count>0) _devices.SelectedIndex=0;
        Append("DEVICE",_devices.Items.Count+" eligible WinUSB main controllers found");
        return Task.CompletedTask;
    }
    private async Task ConnectAsync()
    {
        if (_devices.SelectedItem is not WinUsbCandidate selected)
            throw new InvalidOperationException("Enumerate and select the main controller first.");
        string dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AutoTest","FlashLab");
        Directory.CreateDirectory(dir);
        string file=Path.Combine(dir,"physical-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".jsonl");
        await _device.ConnectAsync(selected.DeviceInstanceId,file);
        Append("DEVICE","Connected; raw trace="+file+"; Physical CAN1 RX enabled");
    }
    private async Task ReadVersionAsync()
    {
        var ack=await _device.ReadOnlyCommandAsync(FirmwareReadOnlyProtocol.GetVersion,TimeSpan.FromSeconds(3));
        Append("DEVICE","VERSION "+FirmwareReadOnlyProtocol.Explain(ack.Command,ack.Payload));
    }
    private async Task ReadGpioAsync()
    {
        var ack=await _device.ReadOnlyCommandAsync(FirmwareReadOnlyProtocol.GetAllPower,TimeSpan.FromSeconds(3));
        Append("DEVICE","READBACK "+FirmwareReadOnlyProtocol.Explain(ack.Command,ack.Payload));
    }
    private async Task DisconnectAsync()
    {
        await _device.DisconnectAsync();
        Append("DEVICE","Disconnected; write remains disabled.");
    }
    private Task LoadConfigAsync()
    {
        var dlg=new OpenFileDialog { Filter="CanTool XML|*.xml|All files|*.*" };
        if(dlg.ShowDialog(this)!=true) return Task.CompletedTask;
        _profile=CanToolFlashProfile.Load(dlg.FileName);
        _config.Text=$"Source: {dlg.FileName}\nAPP TX/RX: {_profile.AppRequestId:X} / {_profile.AppResponseId:X}\n"+
            $"BOOT TX/RX: {_profile.BootRequestId:X} / {_profile.BootResponseId:X}\n"+
            $"Flash: 0x{_profile.FlashStart:X8} .. 0x{_profile.FlashEndInclusive:X8}\n"+
            $"Baud={_profile.BaudRate} Block=0x{_profile.BlockLength:X} P2={_profile.P2TimeoutMs}ms "+
            $"P2*={_profile.P2StarTimeoutMs}ms Erase={_profile.EraseTimeoutMs}ms\n"+
            $"SeedKey DLL: {(_profile.SeedKeyDll is null ? "NOT CONFIGURED" : "Configured; not loaded")}";
        Append("PROFILE","CanTool XML validated: "+dlg.FileName);
        return Task.CompletedTask;
    }
    private async Task LoadFirmwareAsync()
    {
        var dlg=new OpenFileDialog { Filter="Firmware|*.hex;*.s19;*.srec;*.bin|All files|*.*" };
        if(dlg.ShowDialog(this)!=true) return;
        var info=new FileInfo(dlg.FileName);
        if (info.Length is <=0 or >16777216) throw new InvalidDataException("Image must be 1..16 MiB.");
        await using var stream=File.OpenRead(info.FullName);
        string hash=Convert.ToHexString(await SHA256.HashDataAsync(stream));
        _imagePath=info.FullName;
        _firmware.Text=$"File: {_imagePath}\nBytes: {info.Length}\nSHA-256: {hash}\n"+
            "NOT YET PROGRAMMABLE: verify parsed address segments and ECU identity first.";
        Append("IMAGE","Loaded metadata "+info.Name+" SHA256 "+hash);
    }
}
