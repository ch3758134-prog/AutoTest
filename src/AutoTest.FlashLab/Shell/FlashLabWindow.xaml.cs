using System.ComponentModel;
using System.Collections.Concurrent;
using System.Windows.Threading;
using System.Diagnostics;
using System.IO;
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
    private CancellationTokenSource? _connectStop;
    private CancellationTokenSource? _flashStop;
    private string? _seedDllPath;
    private string? _safetyFilePath;
    private string? _seedKeyFilePath;
    private readonly ConcurrentQueue<CanLogRow> _pendingCan=new();
    private int _pendingCount;
    private int _flushRunning;
    private int _activeStage=-1;
    private CanToolFlashJob? _previewFlash;
    private bool _closingApproved;
    private bool _closingInProgress;

    public FlashLabWindow()
    {
        InitializeComponent();
        DataContext=_vm;
        _device.Message+=line=>
        {
            void Render()
            {
                if(_closingApproved || _closingInProgress)return;
                AddLog(line);
                AdvanceStage(line);
            }
            if(Dispatcher.CheckAccess())Render();
            else if(!Dispatcher.HasShutdownStarted)
                _=Dispatcher.BeginInvoke(new Action(Render));
        };
        _device.Can1Frame+=frame=>
        {
            if(_closingApproved || _closingInProgress)return;
            if(Interlocked.Increment(ref _pendingCount)>10000)
            {Interlocked.Decrement(ref _pendingCount);return;}
            _pendingCan.Enqueue(new CanLogRow(frame.Timestamp.ToString("HH:mm:ss.fff"),
                frame.Direction,"0x"+frame.Id.ToString(frame.Id>0x7FF?"X8":"X3"),
                frame.Dlc.ToString(),frame.Data,frame.Result));
            ScheduleCanFlush();
        };
        Loaded+=async (_,_)=>await RefreshDevicesAsync();
        UpdateButtons();
        AddLog("CAN1 烧录工作台已启动。先导入配置与固件，选择安全算法文件，连接 USB，检查后执行。");
    }

    private void ScheduleCanFlush()
    {
        if(Interlocked.CompareExchange(ref _flushRunning,1,0)!=0)return;
        if(Dispatcher.HasShutdownStarted){Interlocked.Exchange(ref _flushRunning,0);return;}
        _=Dispatcher.BeginInvoke(new Action(()=>
        {
            if(!_closingApproved && !_closingInProgress)
            {
                int count=0;
                while(count<200 && _pendingCan.TryDequeue(out var item))
                {
                    Interlocked.Decrement(ref _pendingCount);
                    _vm.AddCanLog(item);
                    count++;
                }
                if(count>0 && _vm.CanLogs.Count>0)
                    CanLogTable.ScrollIntoView(_vm.CanLogs[^1]);
            }
            Interlocked.Exchange(ref _flushRunning,0);
            if(!_pendingCan.IsEmpty && !_closingInProgress && !_closingApproved)ScheduleCanFlush();
        }),DispatcherPriority.Background);
    }

    private void AdvanceStage(string line)
    {
        int step=-1;
        if(line.Contains("S1 检查 Boot",StringComparison.Ordinal))step=0;
        else if(line.Contains("S1/S2 APP",StringComparison.Ordinal) ||
                line.Contains("S2 进入 Boot",StringComparison.Ordinal))step=1;
        else if(line.Contains("S3 SecurityAccess",StringComparison.Ordinal))step=2;
        else if(line.Contains("S4 擦除",StringComparison.Ordinal))step=3;
        else if(line.Contains("S5 Block",StringComparison.Ordinal))step=4;
        else if(line.Contains("UDS Transfer 36 请求",StringComparison.Ordinal))step=5;
        else if(line.Contains("UDS TransferExit 37 请求",StringComparison.Ordinal))step=6;
        else if(line.Contains("S8 校验",StringComparison.Ordinal) ||
                line.Contains("UDS Verify FF01 请求",StringComparison.Ordinal))step=7;
        else if(line.Contains("S9 Boot",StringComparison.Ordinal))step=8;
        else if(line.Contains("S10 复位后",StringComparison.Ordinal))step=9;
        if(step>=0)
        {
            _vm.MarkStage(step,"进行中");
            _activeStage=step;
            WorkflowStatusLabel.Text="当前步骤："+_vm.Stages[step].Step;
        }
    }

    private void Device_SelectionChanged(object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)=>UpdateButtons();

    private void ClearCan_Click(object sender,RoutedEventArgs e)
    {
        _vm.CanLogs.Clear();
        AddLog("已清空 CAN 显示表格；原始证据仍保留。");
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
        if(!_closingInProgress && !_closingApproved)
            MessageBox.Show(this,ex.Message,title+"失败",MessageBoxButton.OK,MessageBoxImage.Warning);
    }
    private void Busy(bool value)
    {
        _vm.IsBusy=value;UpdateButtons();
    }
    private void UpdateButtons()
    {
        // SelectionChanged can fire while InitializeComponent creates controls.
        if(ConnectButton is null || DisconnectButton is null || RefreshButton is null ||
           PrepareFlashButton is null || StartFlashButton is null || AbortFlashButton is null) return;
        ConnectButton.IsEnabled=!_closingInProgress && !_vm.IsBusy && !_device.IsConnected && _vm.SelectedDevice is not null;
        DisconnectButton.IsEnabled=!_closingInProgress && !_vm.IsBusy && _device.IsConnected;
        RefreshButton.IsEnabled=!_closingInProgress && !_vm.IsBusy && !_device.IsConnected;
        PrepareFlashButton.IsEnabled=!_closingInProgress && !_vm.IsBusy;
        StartFlashButton.IsEnabled=!_closingInProgress && !_vm.IsBusy && _device.IsConnected && _previewFlash is not null;
        AbortFlashButton.IsEnabled=!_closingInProgress && _flashStop is not null;
    }
    private async Task RefreshDevicesAsync()
    {
        if(_device.IsConnected || _vm.IsBusy || _closingInProgress || _closingApproved) return;
        Busy(true);
        try
        {
            var candidates=await Task.Run(FlashLabDeviceSession.Discover);
            if(_closingInProgress || _closingApproved)return;
            _vm.Candidates.Clear();
            foreach(var item in candidates) _vm.Candidates.Add(item);
            _vm.SelectedDevice=candidates.Count==1?candidates[0]:null;
            _vm.Status=candidates.Count==0?"未发现专用 WinUSB 接口，正在检查 Windows PnP…"
                : candidates.Count==1?"识别到一台主控，等待手动连接。":
                    $"找到 {candidates.Count} 台主控，请明确选择设备。";
            if(candidates.Count==0)
            {
                // Old src relied on libusb VID/PID, new WinUSB requires GUID.
                // This diagnostics-only lookup never opens or changes a device.
                var report=await Task.Run(ControllerUsbDetectionService.Probe);
                if(_closingInProgress || _closingApproved)return;
                _vm.Status=report.Summary;
                UsbStatusLabel.Text=report.Summary;
                AddLog("USB 枚举诊断："+report.Detail);
            }
            else UsbStatusLabel.Text=_vm.Status;
            AddLog($"{DateTimeOffset.Now:HH:mm:ss.fff}  USB 枚举：{_vm.Status}");
        }
        catch(Exception ex) { Error("刷新 WinUSB 主控",ex); }
        finally { Busy(false); }
    }
    private async void Refresh_Click(object sender,RoutedEventArgs e) =>
        await RefreshDevicesAsync();

    private async void Connect_Click(object sender,RoutedEventArgs e)
    {
        if(_vm.SelectedDevice is not { } device || _closingInProgress) return;
        _connectStop=new CancellationTokenSource();
        Busy(true);
        try
        {
            await _device.ConnectAsync(device,_connectStop.Token);
            _vm.Status="USB 主控连接成功，开始监听 CAN1。";
            UsbStatusLabel.Text="USB 已连接 · CAN1 / DUT";
            WorkflowStatusLabel.Text="USB 已连接；请选择完整烧录配置并检查。";
        }
        catch(Exception ex) { Error("主控连接",ex); }
        finally
        {
            _connectStop?.Dispose();_connectStop=null;
            Busy(false);
        }
    }
    private async void Disconnect_Click(object sender,RoutedEventArgs e)
    {
        Busy(true);
        try
        {
            await _device.DisconnectAsync();
            _vm.Status="USB 已断开，CAN1 监听已停止。";
            UsbStatusLabel.Text="USB 已断开";
            WorkflowStatusLabel.Text="USB 已断开，无法开始烧录。";
        }
        catch(Exception ex) { Error("主控断开",ex); }
        finally { Busy(false); }
    }
    /// <summary>One offline action imports the three actual CanTool inputs
    /// together; the alternate native Seed/Key DLL is never mandatory when
    /// the selected provider is the managed SafetyFile.dll.</summary>
    private async void ImportThreeFiles_Click(object sender,RoutedEventArgs e)
    {
        if(_vm.IsBusy || _closingInProgress || _closingApproved)return;
        var dialog=new OpenFolderDialog
        {
            Title="选择包含 CanTool XML、ECU 固件、SafetyFile.dll 的烧录资料目录",
            Multiselect=false
        };
        if(dialog.ShowDialog(this)!=true)return;
        Busy(true);
        try
        {
            var files=await Task.Run(()=>
                CanToolBundleDiscovery.Discover(dialog.FolderName));
            var checkedFiles=await Task.Run(()=>
            {
                var cfg=CanToolXmlProfileReader.Load(files.ConfigurationXml);
                var library=CanToolSecurityProvider.Inspect(files.ManagedSafetyFile);
                if(library.Kind!=CanToolSecurityKind.ManagedSafetyFile)
                    throw new FormatException("SafetyFile.dll 并非支持的托管安全文件接口。");
                var image=FirmwareSelectionService.Load(files.FirmwareImage,
                    BinBaseBox.Text,cfg);
                return (cfg,library,image);
            });
            if(_closingInProgress || _closingApproved)return;
            var decision=MessageBox.Show(this,
                "找到并验证以下烧录文件（尚未发送 ECU 指令）：\n\n"+
                "XML："+files.ConfigurationXml+"\n\n"+
                "固件："+files.FirmwareImage+"\n\n"+
                "安全文件："+checkedFiles.library.Path+"\n\n"+
                $"镜像地址：0x{checkedFiles.image.Start:X8}.."+
                $"0x{checkedFiles.image.EndExclusive-1:X8}\n"+
                "采用 SafetyFile.dll 托管算法，不需要另外导入 Seed/Key DLL。\n\n"+
                "确认将这些文件载入当前烧录配置？",
                "确认三文件导入",MessageBoxButton.YesNo,
                MessageBoxImage.Question,MessageBoxResult.No);
            if(decision!=MessageBoxResult.Yes)return;
            checkedFiles.cfg.Set("SafetyFilePath",checkedFiles.library.Path);
            _vm.ApplyConfiguration(checkedFiles.cfg);
            _vm.SetImage(files.FirmwareImage,checkedFiles.image);
            SecuritySourceSelector.SelectedIndex=0;
            ApplySecurityFilesFromConfiguration();
            InvalidateFlashPreview();
            _vm.Status="三文件已经一次载入并通过离线检查；请核对目标 ECU 后检查烧录参数。";
            AddLog($"三文件导入成功：XML={files.ConfigurationXml}，"+
                $"固件={files.FirmwareImage}，SafetyFile={checkedFiles.library.Path}");
        }
        catch(Exception ex){Error("导入三文件烧录资料",ex);}
        finally {Busy(false);}
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
            ApplySecurityFilesFromConfiguration();
            _vm.Status="已加载 CanTool XML；字段与未知项已保留。";
            AddLog("XML 配置载入："+dialog.FileName);
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
            ApplySecurityFilesFromConfiguration();
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
    private void InvalidateFlashPreview()
    {
        _previewFlash=null;
        if(FlashPlanSummary is not null)
            FlashPlanSummary.Text="配置或文件已更改，请重新检查烧录参数。";
        if(WorkflowStatusLabel is not null)
            WorkflowStatusLabel.Text="等待检查本次配置、固件和安全算法。";
        UpdateButtons();
    }

    private string? ResolveReferenceFile(string configValue)
    {
        if(string.IsNullOrWhiteSpace(configValue))return null;
        string path=Environment.ExpandEnvironmentVariables(configValue.Trim().Trim('"'));
        string? source=_vm.Configuration.SourcePath;
        if(!Path.IsPathRooted(path) && !string.IsNullOrWhiteSpace(source))
            path=Path.Combine(Path.GetDirectoryName(source)!,path);
        return Path.GetFullPath(path);
    }
    private string? ResolveAndInspect(string field,CanToolSecurityKind expected,
        System.Windows.Controls.TextBlock display)
    {
        string? path;
        try {path=ResolveReferenceFile(_vm.Configuration.Get(field));}
        catch(Exception ex){display.Text="配置路径无效："+ex.Message;return null;}
        if(string.IsNullOrWhiteSpace(path)){display.Text="未选择文件";return null;}
        if(!File.Exists(path))
        {
            display.Text="找不到："+Path.GetFileName(path);
            display.ToolTip=path;
            AddLog(field+" 文件不存在："+path);
            return null;
        }
        try
        {
            var value=CanToolSecurityProvider.Inspect(path);
            if(value.Kind!=expected)
            {
                display.Text="接口类型不匹配："+Path.GetFileName(path);
                AddLog(field+" 使用了 "+value.Kind+"；请放入对应的安全算法入口。");
                return null;
            }
            display.Text=Path.GetFileName(path)+" · 已识别";
            display.ToolTip=value.Path+" / SHA256="+value.Sha256;
            AddLog(field+" 已识别："+value.Summary+"；路径="+path);
            return value.Path;
        }
        catch(Exception ex)
        {
            display.Text="DLL 检查失败："+Path.GetFileName(path);
            display.ToolTip=ex.Message;
            AddLog(field+" 检查失败："+ex.Message);
            return null;
        }
    }
    private void ApplySecurityFilesFromConfiguration()
    {
        // The user's TWO independent CanTool file settings are never collapsed.
        _safetyFilePath=ResolveAndInspect("SafetyFilePath",
            CanToolSecurityKind.ManagedSafetyFile,SafetyDllStatusLabel);
        _seedKeyFilePath=ResolveAndInspect("SeedToKeyDll_Path",
            CanToolSecurityKind.NativeZlgKey,SeedKeyDllStatusLabel);
        UpdateSecuritySelection();
    }
    private void UpdateSecuritySelection()
    {
        if(SecuritySourceSelector is null)return;
        bool managed=SecuritySourceSelector.SelectedIndex!=1;
        _seedDllPath=managed?_safetyFilePath:_seedKeyFilePath;
        if(SecurityProviderStatusLabel is not null)
            SecurityProviderStatusLabel.Text=managed
                ? "本次使用 SafetyFile.dll 托管算法"+(_seedDllPath is null?"（尚未就绪）":"（已识别）")
                : "本次使用 Seed/Key 原生 DLL 算法"+(_seedDllPath is null?"（尚未就绪）":"（已识别）");
    }
    private void SecuritySource_SelectionChanged(object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if(PrepareFlashButton is null)return;
        UpdateSecuritySelection();InvalidateFlashPreview();
    }
    private void ChooseSafetyFile_Click(object sender,RoutedEventArgs e)=>
        SelectSecurityFile("SafetyFilePath","选择安全文件 SafetyFile.dll");
    private void ChooseSeedKey_Click(object sender,RoutedEventArgs e)=>
        SelectSecurityFile("SeedToKeyDll_Path","选择 Seed/Key 原生 DLL");
    private void SelectSecurityFile(string field,string title)
    {
        var dialog=new OpenFileDialog
        {
            Title=title,Filter="DLL 安全文件 (*.dll)|*.dll|所有文件 (*.*)|*.*"
        };
        if(dialog.ShowDialog(this)!=true)return;
        try
        {
            var info=CanToolSecurityProvider.Inspect(dialog.FileName);
            var expected=field=="SafetyFilePath"
                ? CanToolSecurityKind.ManagedSafetyFile : CanToolSecurityKind.NativeZlgKey;
            if(info.Kind!=expected)
                throw new FormatException("所选文件是 "+info.Kind+"；请使用对应的另一个导入入口。");
            _vm.Configuration.Set(field,info.Path);
            ApplySecurityFilesFromConfiguration();
            InvalidateFlashPreview();
            AddLog("已更新配置 "+field+"："+info.Path);
            if(info.KnownDemo)
                MessageBox.Show(this,
                    "该 DLL 为 Seed/Key 演示算法，接口匹配不代表目标 ECU 接受。"+
                    "请现场核对算法来源。","算法匹配提醒",
                    MessageBoxButton.OK,MessageBoxImage.Information);
        }
        catch(Exception ex){Error("导入安全算法文件",ex);}
    }

    private void PrepareFlash_Click(object sender,RoutedEventArgs e)
    {
        try
        {
            if(_vm.Image is null || string.IsNullOrWhiteSpace(_vm.FirmwarePath))
                throw new InvalidOperationException("请先选择并离线校验 HEX/S19/BIN 镜像。");
            // Always reload both visible configuration file paths before freezing.
            ApplySecurityFilesFromConfiguration();
            UpdateSecuritySelection();
            if(string.IsNullOrWhiteSpace(_seedDllPath))
                throw new InvalidOperationException(
                    "本次选用的安全算法 DLL 未准备好，请在配置区域选择对应的文件。");
            // Rebuild immutable on-wire snapshot using current config, current file
            // and current DLL; no stale UI values or automatic hardware actions.
            var plan=CanToolFlashJob.Freeze(_vm.Configuration,
                _vm.FirmwarePath,BinBaseBox.Text,_seedDllPath,TargetEcuBox.Text,
                LegacyTailSignatureCheck.IsChecked==true);
            _previewFlash=plan;
            _vm.ResetStages();
            _activeStage=-1;
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
            FlashStageLabel.Text="文件及烧录地址检查完成；尚未向 ECU 发送请求。";
            WorkflowStatusLabel.Text="准备就绪 · 请连接主控并核对本次擦除地址后开始烧录。";
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
            if(FlashSafetyCheck.IsChecked!=true)
                throw new UnauthorizedAccessException(
                    "请先核对 ECU、镜像、安全算法、擦除范围及供电，并勾选确认。");
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
            // Physical frame gate still binds exactly to this frozen job SHA;
            // the operator no longer has to type a 64-byte fingerprint.
            var outcome=await _device.ProgramCanToolAsync(frozen,frozen.Consent,
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
            _vm.MarkStage(9,"已完成");
            WorkflowStatusLabel.Text="参考烧录阶段已完成；请检查 APP 固件版本与身份。";
            _vm.Status="参考烧录阶段已完成；固件版本 DID 尚未复验。";
        }
        catch(OperationCanceledException)
        {
            FlashStageLabel.Text="操作已取消；ECU 物理状态 UNKNOWN。禁止未经诊断直接重刷。";
            _vm.Status=FlashStageLabel.Text;
            if(_activeStage>=0)_vm.MarkStage(_activeStage,"已取消");
            WorkflowStatusLabel.Text="烧录已取消，ECU 物理状态可能未知。";
            AddLog(FlashStageLabel.Text);
        }
        catch(Exception ex)
        {
            FlashStageLabel.Text="烧录未确认成功；物理状态可能未知："+ex.Message;
            if(_activeStage>=0)_vm.MarkStage(_activeStage,"失败");
            WorkflowStatusLabel.Text="烧录停止于："+(_activeStage>=0?_vm.Stages[_activeStage].Step:"准备阶段");
            Error("实体 CanTool 烧录",ex);
        }
        finally
        {
            _flashStop?.Dispose();_flashStop=null;
            _previewFlash=null; // every physical run must be prepared again
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
        { Title="导出 FlashLab USB/CAN/UDS 原始诊断 ZIP",
          Filter="ZIP 证据档案 (*.zip)|*.zip",
          FileName=$"FlashLab_CAN_UDS_{DateTime.Now:yyyyMMdd_HHmmss}.zip",
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
    private void Window_Closing(object sender,CancelEventArgs e)
    {
        if(_closingApproved)return;
        if(_closingInProgress) { e.Cancel=true; return; }
        _connectStop?.Cancel();
        _flashStop?.Cancel();
        // No WinUSB handle / CAN worker / trace writer exists: close immediately.
        if(!_device.HasActiveResources && _flashStop is null &&
           _connectStop is null)
        {
            _closingApproved=true;
            return;
        }
        e.Cancel=true;
        _closingInProgress=true;
        _vm.Status="正在安全退出：取消请求、关闭 USB/CAN1、保存诊断日志…";
        Title="Flash Lab · 正在安全退出";
        Busy(true);
        _=CloseAfterCleanupAsync();
    }
    private async Task CloseAfterCleanupAsync()
    {
        var timer=Stopwatch.StartNew();
        try
        {
            AddLog("开始安全退出；取消仍在执行的诊断/编程任务。");
            await _device.DisposeAsync();
            AddLog($"设备资源已清理，总耗时 {timer.ElapsedMilliseconds} ms。");
        }
        catch(Exception ex)
        {
            AddLog("退出清理失败："+ex);
            Trace.WriteLine("FlashLab close cleanup failure: "+ex);
            // Never claim a successful programming outcome after a cleanup fault.
        }
        finally
        {
            _closingApproved=true;
            _closingInProgress=false;
            Close();
        }
    }
}
