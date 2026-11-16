using System.ComponentModel;
using System.Windows;
using AutoTest.Core;
using AutoTest.Devices.Usb;
using AutoTest.FlashLab.Configuration;
using AutoTest.FlashLab.Evidence;
using AutoTest.FlashLab.Firmware;
using AutoTest.FlashLab.Hardware;
using AutoTest.FlashLab.Presentation;
using AutoTest.FlashLab.Programming;
using Microsoft.Win32;

namespace AutoTest.FlashLab.Shell;

public partial class FlashLabWindow : Window
{
    private readonly FlashLabViewModel _vm = new();
    private readonly FlashLabDeviceSession _device = new();
    private CancellationTokenSource? _didStop;
    private CancellationTokenSource? _flashStop;
    private string? _seedDllPath;
    private CanToolFlashJob? _previewFlash;
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
        AddLog("F2 源码候选已启动：可准备并现场批准 CanTool 实体烧录；当前未经 ECU 端到端验证。");
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
        PrepareFlashButton.IsEnabled=!_vm.IsBusy;
        StartFlashButton.IsEnabled=!_vm.IsBusy && _device.IsConnected && _previewFlash is not null;
        AbortFlashButton.IsEnabled=_flashStop is not null;
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
            InvalidateFlashPreview();
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
            InvalidateFlashPreview();
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
            InvalidateFlashPreview();
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

    private void InvalidateFlashPreview()
    {
        _previewFlash=null;
        if(ExpectedFlashConsentBox is not null) ExpectedFlashConsentBox.Text="";
        if(TypedFlashConsentBox is not null) TypedFlashConsentBox.Text="";
        if(FlashPlanSummary is not null)
            FlashPlanSummary.Text="配置/镜像已改变，请重新生成冻结任务。";
        UpdateButtons();
    }

    private void ChooseKeyDll_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFileDialog
        {
            Title="选择安全算法文件：SafetyFile.dll（托管）或 ZLGKey（原生）（此步骤不执行）",
            Filter="Windows DLL (*.dll)|*.dll|全部文件 (*.*)|*.*"
        };
        if(dialog.ShowDialog(this)!=true)return;
        try
        {
            var inspection=CanToolSecurityProvider.Inspect(dialog.FileName);
            _seedDllPath=inspection.Path;
            InvalidateFlashPreview();
            KeyDllPathLabel.Text=inspection.Path;
            KeyDllInfoLabel.Text=$"{inspection.Summary}\nSHA256={inspection.Sha256}";
            AddLog("算法库仅静态检查："+inspection.Path+"，SHA="+inspection.Sha256);
            // Managed SafetyFile and native ZLGKey use different verified APIs.
            // They are both selectable; this does not automatically prove ECU compatibility.
            if(inspection.KnownDemo)
                MessageBox.Show(this,
                    "已识别 sa_demo.dll 为演示 Seed/Key 算法。仅证明 ZLGKey 接口匹配，不证明 ECU 接受。\n未经真实 ECU 匹配验证不要尝试解锁。",
                    "演示算法提醒",MessageBoxButton.OK,MessageBoxImage.Warning);
        }
        catch(Exception ex){ Error("静态检查 Seed/Key DLL",ex); }
    }

    private void PrepareFlash_Click(object sender,RoutedEventArgs e)
    {
        try
        {
            if(_vm.Image is null || string.IsNullOrWhiteSpace(_vm.FirmwarePath))
                throw new InvalidOperationException("请先选择并离线校验 HEX/S19/BIN 镜像。");
            if(string.IsNullOrWhiteSpace(_seedDllPath))
                throw new InvalidOperationException("必须选择 Seed/Key DLL。");
            // Rebuild immutable on-wire snapshot using current config, current file
            // and current DLL; no stale UI values or automatic hardware actions.
            var plan=CanToolFlashJob.Freeze(_vm.Configuration,
                _vm.FirmwarePath,BinBaseBox.Text,_seedDllPath,TargetEcuBox.Text,
                LegacyTailSignatureCheck.IsChecked==true);
            _previewFlash=plan;
            ExpectedFlashConsentBox.Text=plan.Consent;
            TypedFlashConsentBox.Text="";
            FlashProgressBar.Value=0;
            FlashPlanSummary.Text=
                $"ECU（人工标识）: {plan.TargetLabel}\n"+
                $"APP TX/RX=0x{plan.App.RequestCanId:X}/0x{plan.App.ResponseCanId:X}\n"+
                $"BOOT TX/RX=0x{plan.Boot.RequestCanId:X}/0x{plan.Boot.ResponseCanId:X}\n"+
                $"CAN1 XML 波特率={plan.ConfiguredBitrate}（未自动配置）\n"+
                $"擦除: 0x{plan.FlashStart:X8}..0x{plan.FlashEndInclusive:X8} "+
                $"({plan.EraseLength:N0}B)\n"+
                $"镜像源字节={plan.SourceBytes:N0}，实际计划块字节={plan.PlannedBytes:N0}，"+
                $"对齐块=0x{plan.AlignmentBlockLength:X}（{plan.AlignmentSource}）\n"+
                $"参考末端签名={(plan.LegacyTailSignature ? "启用：12 34 56 78，可能覆盖原字节" : "未启用")}\n"+
                $"固件 SHA={plan.FirmwareSha256}\n安全算法类型={plan.SecurityProviderKind}\n算法文件 SHA={plan.SecuritySha256}";
            FlashStageLabel.Text="冻结任务已生成；尚未发往 ECU。只有现场完整许可与二次确认后才执行。";
            _vm.Status="烧录参数已冻结并生成 SHA256，等待实体台架单独授权。";
            AddLog("F2 冻结任务已生成 SHA="+plan.Fingerprint+
                "，擦除范围 0x"+plan.FlashStart.ToString("X8")+"..0x"+
                plan.FlashEndInclusive.ToString("X8")+"；当前没有 CAN 发送。");
            UpdateButtons();
        }
        catch(Exception ex)
        {
            InvalidateFlashPreview();
            Error("建立 CanTool 烧录计划",ex);
        }
    }

    private async void StartFlash_Click(object sender,RoutedEventArgs e)
    {
        if(_vm.IsBusy || !_device.IsConnected || _previewFlash is null)return;
        try
        {
            var frozen=_previewFlash;
            if(string.IsNullOrWhiteSpace(_seedDllPath))
                throw new InvalidOperationException("Seed/Key DLL 未选择。");
            // Any edited XML field, new disk bytes, DLL or selected BIN base MUST
            // invalidate the displayed physical consent fingerprint.
            var fresh=CanToolFlashJob.Freeze(_vm.Configuration,
                _vm.FirmwarePath,BinBaseBox.Text,_seedDllPath,TargetEcuBox.Text,
                LegacyTailSignatureCheck.IsChecked==true);
            if(!string.Equals(frozen.Fingerprint,fresh.Fingerprint,StringComparison.Ordinal))
                throw new InvalidOperationException("配置、固件或算法库在生成计划后已改变。请重新生成烧录计划。");
            if(BitrateVerifiedCheck.IsChecked!=true)
                throw new UnauthorizedAccessException("必须先在现场核实主控 CAN1 实际波特率。");
            if(!string.Equals(TypedFlashConsentBox.Text,frozen.Consent,StringComparison.Ordinal))
                throw new UnauthorizedAccessException("烧录精确 SHA256 许可字符串不一致。");
            var choice=MessageBox.Show(this,
                "这是实体 ECU 编程操作，不是仿真。\n\n"+
                $"型号/版本（人工核对）：{frozen.TargetLabel}\n"+
                $"擦除全部地址：0x{frozen.FlashStart:X8}..0x{frozen.FlashEndInclusive:X8}\n"+
                $"固件 SHA256：{frozen.FirmwareSha256}\n"+
                $"DLL SHA256：{frozen.SecuritySha256}\n"+
                $"末端参考签名：{(frozen.LegacyTailSignature ? "启用（覆盖4字节）" : "未启用")}\n\n"+
                "操作将执行 Boot 切换、SecurityAccess、实际擦除、下载、校验、复位。\n"+
                "断连/取消后 ECU 可能处于无法启动状态。已确认供电稳定、镜像/算法/地址"+
                "与真实 ECU 完全匹配，并允许本次写入吗？",
                "最终确认：真实 ECU Flash 擦写",MessageBoxButton.YesNo,
                MessageBoxImage.Warning,MessageBoxResult.No);
            if(choice!=MessageBoxResult.Yes)return;

            _flashStop=new CancellationTokenSource();
            Busy(true);
            FlashStageLabel.Text="真实烧录进行中；请保持稳定供电，不要拔线。";
            var outcome=await _device.ProgramCanToolAsync(frozen,TypedFlashConsentBox.Text,
                stationBitrateVerified:true,
                (tx,verified) =>
                {
                    void Refresh()
                    {
                        FlashProgressBar.Value=frozen.PlannedBytes==0?0:
                            Math.Min(100d,100d*tx/frozen.PlannedBytes);
                        FlashStageLabel.Text=$"TransferData ECU 0x76 ACK {tx:N0}/{frozen.PlannedBytes:N0}B；"+
                            $"FF01 校验确认 {verified:N0}B；整体状态尚取决于复位及 APP 响应。";
                    }
                    if(Dispatcher.CheckAccess())Refresh();
                    else _=Dispatcher.BeginInvoke(new Action(Refresh));
                },_flashStop.Token);
            FlashStageLabel.Text=
                $"参考流程执行结果：{outcome.Outcome}\n"+
                $"ACK={outcome.TransferAcknowledged:N0}B, 校验={outcome.VerifiedBytes:N0}B, "+
                $"复位ACK={outcome.ResetAcknowledged}, APP会话复现={outcome.AppSessionObserved}。"+
                "新固件版本尚未由身份/版本 DID 证明。";
            _vm.Status="参考 CanTool 烧录流程已得到阶段响应；最终固件身份仍待独立复验。";
        }
        catch(OperationCanceledException)
        {
            FlashStageLabel.Text="操作已取消；ECU 物理状态 UNKNOWN。禁止未经诊断直接重刷。";
            _vm.Status=FlashStageLabel.Text;
            AddLog(FlashStageLabel.Text);
        }
        catch(Exception ex)
        {
            FlashStageLabel.Text="烧录未确认成功；物理状态可能未知："+ex.Message;
            Error("实体 CanTool 烧录",ex);
        }
        finally
        {
            _flashStop?.Dispose();_flashStop=null;
            TypedFlashConsentBox.Text="";
            InvalidateFlashPreview();
            Busy(false);
        }
    }
    private void AbortFlash_Click(object sender,RoutedEventArgs e)
    {
        _flashStop?.Cancel();
        FlashStageLabel.Text="已请求取消烧录；不能保证 ECU 尚未发生擦写。";
        AddLog("操作员取消实体烧录；写入影响可能未知，保留原始日志。");
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
        if(_flashStop is not null) _flashStop.Cancel();
        try { await _device.DisposeAsync(); }
        catch(Exception ex) { AddLog("关闭清理异常："+ex.Message); }
        _closingApproved=true;
        Close();
    }
}
