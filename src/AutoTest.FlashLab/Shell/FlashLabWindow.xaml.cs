using System.ComponentModel;
using System.Windows;
using AutoTest.Core;
using AutoTest.Devices.Usb;
using AutoTest.FlashLab.Configuration;
using AutoTest.FlashLab.Evidence;
using AutoTest.FlashLab.Firmware;
using AutoTest.FlashLab.Hardware;
using AutoTest.FlashLab.Presentation;
using Microsoft.Win32;

namespace AutoTest.FlashLab.Shell;

public partial class FlashLabWindow : Window
{
    private readonly FlashLabViewModel _vm = new();
    private readonly FlashLabDeviceSession _device = new();
    private CancellationTokenSource? _didStop;
    private bool _closingApproved;

    public FlashLabWindow()
    {
        InitializeComponent();
        DataContext=_vm;
        _device.Message+=line=>
        {
            if(Dispatcher.CheckAccess()) AddLog(line);
            else _=Dispatcher.BeginInvoke(new Action(()=>AddLog(line)));
        };
        Loaded+=async (_,_)=>await RefreshDevicesAsync();
        UpdateButtons();
        AddLog("G4-F1 已启动：无真实烧录授权；当前仅允许离线配置、镜像检查及另行现场授权的0x22读取。");
    }
    private void AddLog(string text)
    {
        _vm.AddLog(text);
        if(_vm.Logs.Count>0) LogList.ScrollIntoView(_vm.Logs[^1]);
    }
    private void Error(string title,Exception ex)
    {
        _vm.Status=title+"失败："+ex.Message;
        AddLog($"{DateTimeOffset.Now:HH:mm:ss.fff}  {title}失败：{ex}");
        MessageBox.Show(this,ex.Message,title+"失败",MessageBoxButton.OK,MessageBoxImage.Warning);
    }
    private void Busy(bool value)
    {
        _vm.IsBusy=value;UpdateButtons();
    }
    private void UpdateButtons()
    {
        if(ConnectButton is null) return;
        ConnectButton.IsEnabled=!_vm.IsBusy && !_device.IsConnected && _vm.SelectedDevice is not null;
        DisconnectButton.IsEnabled=!_vm.IsBusy && _device.IsConnected;
        RefreshButton.IsEnabled=!_vm.IsBusy && !_device.IsConnected;
        ReadDidButton.IsEnabled=!_vm.IsBusy && _device.IsConnected;
        CancelDidButton.IsEnabled=_didStop is not null;
    }
    private async Task RefreshDevicesAsync()
    {
        if(_device.IsConnected || _vm.IsBusy) return;
        Busy(true);
        try
        {
            var candidates=await Task.Run(FlashLabDeviceSession.Discover);
            _vm.Candidates.Clear();
            foreach(var item in candidates) _vm.Candidates.Add(item);
            _vm.SelectedDevice=candidates.Count==1?candidates[0]:null;
            _vm.Status=candidates.Count==0?"未发现符合主控 VID/PID/接口 GUID 的 WinUSB 设备。"
                : candidates.Count==1?"识别到一台主控，等待手动连接。":
                    $"找到 {candidates.Count} 台主控，请明确选择设备。";
            AddLog($"{DateTimeOffset.Now:HH:mm:ss.fff}  USB 枚举：{_vm.Status}");
        }
        catch(Exception ex) { Error("刷新 WinUSB 主控",ex); }
        finally { Busy(false); }
    }
    private async void Refresh_Click(object sender,RoutedEventArgs e) =>
        await RefreshDevicesAsync();

    private async void Connect_Click(object sender,RoutedEventArgs e)
    {
        if(_vm.SelectedDevice is not { } device) return;
        Busy(true);
        try
        {
            await _device.ConnectAsync(device,CancellationToken.None);
            _vm.Status="真实主控已连接：CAN1=DUT；未执行现场 DID 授权。";
        }
        catch(Exception ex) { Error("主控连接",ex); }
        finally { Busy(false); }
    }
    private async void Disconnect_Click(object sender,RoutedEventArgs e)
    {
        _didStop?.Cancel();
        Busy(true);
        try
        {
            await _device.DisconnectAsync();
            _vm.TypedConfirmation="";
            _vm.Status="主控已断开，诊断许可已撤销。";
        }
        catch(Exception ex) { Error("主控断开",ex); }
        finally { Busy(false); }
    }
    private void LoadXml_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFileDialog
        { Title="选择 CanTool Configuration.xml",Filter="CanTool XML (*.xml)|*.xml|全部文件 (*.*)|*.*" };
        if(dialog.ShowDialog(this)!=true)return;
        try
        {
            var cfg=CanToolXmlProfileReader.Load(dialog.FileName);
            _vm.ApplyConfiguration(cfg);
            _vm.Status="已加载 CanTool XML；字段与未知项已保留。";
            AddLog("XML 配置载入："+dialog.FileName);
            ApplyEndpointFromConfiguration(logError:false);
        }
        catch(Exception ex) { Error("读取配置 XML",ex); }
    }
    private void LoadJson_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFileDialog
        { Title="导入 CanTool settings.json（不会覆盖 XML 字段）",
          Filter="JSON (*.json)|*.json|全部文件 (*.*)|*.*" };
        if(dialog.ShowDialog(this)!=true)return;
        try
        {
            _vm.Configuration.LoadRelatedJson(dialog.FileName);
            _vm.ApplyConfiguration(_vm.Configuration);
            _vm.Status="已导入 settings.json 非冲突字段；未知字段不驱动硬件。";
            AddLog("JSON 参考配置："+dialog.FileName);
        }
        catch(Exception ex) { Error("读取 settings.json",ex); }
    }
    private void SaveXml_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new SaveFileDialog
        { Title="保存 CanTool 配置",Filter="CanTool XML (*.xml)|*.xml",
          FileName="Configuration.xml",OverwritePrompt=true };
        if(dialog.ShowDialog(this)!=true)return;
        try
        {
            _vm.Configuration.ValidateConfiguredRange();
            _vm.Configuration.SaveAs(dialog.FileName);
            _vm.Status="配置已保存（仅文件，不会发送任何 ECU 指令）。";
            AddLog("配置保存："+dialog.FileName);
        }
        catch(Exception ex) { Error("保存配置",ex); }
    }
    private void ChooseImage_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFileDialog
        { Title="选择固件镜像（离线，只验证文件）",
          Filter="支持的固件|*.hex;*.h86;*.s19;*.s28;*.s37;*.sx;*.mot;*.bin|HEX|*.hex;*.h86|S19|*.s19;*.s28;*.s37;*.sx;*.mot|BIN|*.bin|全部文件|*.*" };
        if(dialog.ShowDialog(this)!=true)return;
        try
        {
            var result=FirmwareSelectionService.Load(dialog.FileName,BinBaseBox.Text,_vm.Configuration);
            _vm.SetImage(dialog.FileName,result);
            _vm.Status="镜像解析及地址边界校验通过（离线）；不代表兼容真实 ECU。";
            AddLog("镜像检查："+result.Summary);
        }
        catch(Exception ex) { Error("固件镜像校验",ex); }
    }
    private void Endpoint_SelectionChanged(object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        // Never rewrite operator edits simply because a mode tab changed.
    }
    private void ApplyEndpoint_Click(object sender,RoutedEventArgs e) =>
        ApplyEndpointFromConfiguration(logError:true);
    private void ApplyEndpointFromConfiguration(bool logError)
    {
        try
        {
            bool boot=EndpointSelector.SelectedIndex==1;
            var ep=_vm.Configuration.GetEndpoint(boot);
            _vm.RequestId=$"0x{ep.Request:X}";
            _vm.ResponseId=$"0x{ep.Response:X}";
            _vm.TypedConfirmation="";
            AddLog($"只从配置填入 CAN1 {(boot?"Boot":"APP")} 地址：TX=0x{ep.Request:X}, RX=0x{ep.Response:X}；未发送");
        }
        catch(Exception ex)
        {
            if(logError) Error("载入诊断地址",ex);
            // Missing or invalid reference fields are not filled with sample IDs.
        }
    }
    private async void ReadDid_Click(object sender,RoutedEventArgs e)
    {
        if(!_device.IsConnected || _vm.IsBusy)return;
        try
        {
            uint tx=CanToolXmlProfileReader.ParseHexUInt(_vm.RequestId,"CAN1 请求 ID");
            uint rx=CanToolXmlProfileReader.ParseHexUInt(_vm.ResponseId,"CAN1 响应 ID");
            ushort did=CanToolXmlProfileReader.ParseDid(_vm.Did);
            string expected=PhysicalCommandFence.ReadDidConfirmation(tx,rx,did);
            if(!string.Equals(_vm.TypedConfirmation,expected,StringComparison.Ordinal))
                throw new UnauthorizedAccessException(
                    "尚未取得本次现场单独许可：必须手动输入完整精确确认字符串。");
            if(MessageBox.Show(this,
                $"将向实体 DUT 的 CAN1 发送一次 0x22 DID 查询。\n\nTX=0x{tx:X}  RX=0x{rx:X}  DID=0x{did:X4}\n\nF1 开发批准不等于现场授权；确认此刻已获准诊断发送？",
                "再次确认实体 CAN1 发送",MessageBoxButton.YesNo,MessageBoxImage.Warning,
                MessageBoxResult.No)!=MessageBoxResult.Yes)return;
            _didStop=new CancellationTokenSource(TimeSpan.FromSeconds(30));
            Busy(true);
            _vm.DidResult="现场 0x22 只读请求进行中（并非烧录）…";
            var result=await _device.ReadOneDidAsync(tx,rx,did,_vm.TypedConfirmation,
                _didStop.Token);
            _vm.DidResult=result.Display;
            _vm.Status=result.Outcome==Outcome.Accepted
                ?"DID 有正响应；USB/CAN/UDS 原始记录可导出。"
                :"DID 未取得可确认成功的正响应；请查看原始证据。";
        }
        catch(OperationCanceledException)
        {
            _vm.DidResult="本次 DID 操作已取消；物理发送是否发生需检查日志。";
            _vm.Status="读取已取消，物理效果 UNKNOWN。";
            AddLog(_vm.DidResult);
        }
        catch(Exception ex) { _vm.DidResult="失败："+ex.Message;Error("读取只读 DID",ex); }
        finally
        {
            _didStop?.Dispose();_didStop=null;Busy(false);
            _vm.TypedConfirmation=""; // One-shot consent is never carried to the next read.
        }
    }
    private void CancelDid_Click(object sender,RoutedEventArgs e)
    {
        _didStop?.Cancel();
        AddLog("操作员请求取消 DID；底层物理许可在本操作结束时撤销。");
    }
    private void Export_Click(object sender,RoutedEventArgs e)
    {
        var dlg=new SaveFileDialog
        { Title="导出 FlashLab F1 原始诊断 ZIP",
          Filter="ZIP 证据档案 (*.zip)|*.zip",
          FileName=$"FlashLab_F1_{DateTime.Now:yyyyMMdd_HHmmss}.zip",
          OverwritePrompt=true };
        if(dlg.ShowDialog(this)!=true)return;
        try
        {
            FlashLabEvidenceExporter.Export(dlg.FileName,_device.EvidencePath,
                _vm.Configuration,_vm.Image,_vm.Logs.ToArray());
            _vm.Status="诊断 ZIP 导出成功；真实 ECU 标识信息请妥善保存。";
            AddLog("导出诊断证据："+dlg.FileName);
        }
        catch(Exception ex) { Error("导出诊断 ZIP",ex); }
    }
    private void ClearLog_Click(object sender,RoutedEventArgs e)
    {
        _vm.Logs.Clear();
        AddLog("界面日志已清空；硬件原始 JSONL 证据仍保留，不会删除。");
    }
    private async void Window_Closing(object sender,CancelEventArgs e)
    {
        if(_closingApproved)return;
        e.Cancel=true;
        if(_didStop is not null) _didStop.Cancel();
        try { await _device.DisposeAsync(); }
        catch(Exception ex) { AddLog("关闭清理异常："+ex.Message); }
        _closingApproved=true;
        Close();
    }
}
