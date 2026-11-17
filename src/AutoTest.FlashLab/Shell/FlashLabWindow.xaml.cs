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
    private string? _safetyFilePath;
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
        AddLog("CAN1 烧录工作台已启动。导入 CanTool 配置、固件、安全文件，连接 USB 后开始烧录。");
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
           StartFlashButton is null || AbortFlashButton is null) return;
        ConnectButton.IsEnabled=!_closingInProgress && !_vm.IsBusy && !_device.IsConnected && _vm.SelectedDevice is not null;
        DisconnectButton.IsEnabled=!_closingInProgress && !_vm.IsBusy && _device.IsConnected;
        RefreshButton.IsEnabled=!_closingInProgress && !_vm.IsBusy && !_device.IsConnected;
        // Never hide the reason behind a disabled Start button: when config
        // or safety is incomplete, Start runs offline validation and explains
        // the exact missing file without transmitting anything to the ECU.
        StartFlashButton.IsEnabled=!_closingInProgress && !_vm.IsBusy && _device.IsConnected &&
            _vm.Image is not null;
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
            WorkflowStatusLabel.Text=_vm.Image is not null
                ? "USB 已连接，固件已选择；点击开始烧录将自动核对文件。"
                : "USB 已连接，选择 ECU 固件后即可进行烧录预检。";
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
    /// <summary>Import CanTool XML, firmware, and the one required safety file.</summary>
    private async void ImportThreeFiles_Click(object sender,RoutedEventArgs e)
    {
        if(_vm.IsBusy || _closingInProgress || _closingApproved)return;
        var dialog=new OpenFolderDialog
        {
            Title="选择包含 CanTool XML、ECU 固件、SafetyFile.dll 的烧录资料目录",
            Multiselect=false
        };
        if(dialog.ShowDialog(this)!=true)return;
        string selectedFolder=dialog.FolderName;
        string binBase=BinBaseBox.Text; // Capture WPF value on UI thread.
        Busy(true);
        try
        {
            var files=await Task.Run(()=>
                CanToolBundleDiscovery.Discover(selectedFolder));
            var checkedFiles=await Task.Run(()=>
            {
                var cfg=CanToolXmlProfileReader.Load(files.ConfigurationXml);
                var library=CanToolSecurityProvider.Inspect(files.ManagedSafetyFile);
                if(library.Kind!=CanToolSecurityKind.CanToolReferenceBuiltInLfsr)
                    throw new FormatException(
                        "SafetyFile.dll 与已核查的 CanTool 参考安全文件不一致；"+
                        "当前不采用未知算法进行 ECU 解锁。");
                var image=FirmwareSelectionService.Load(files.FirmwareImage,
                    binBase,cfg);
                return (cfg,library,image);
            });
            if(_closingInProgress || _closingApproved)return;
            checkedFiles.cfg.Set("SafetyFilePath",checkedFiles.library.Path);
            _vm.ApplyConfiguration(checkedFiles.cfg);
            _vm.SetImage(files.FirmwareImage,checkedFiles.image);
            ApplySafetyFileFromConfiguration();
            InvalidateFlashPreview();
            _vm.Status="已导入烧录文件；USB 连接后直接点击开始烧录。";
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
            ApplySafetyFileFromConfiguration();
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
            ApplySafetyFileFromConfiguration();
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
            ApplySafetyFileFromConfiguration();
            _vm.Status="镜像解析及地址边界校验通过（离线）；不代表兼容真实 ECU。";
            AddLog("镜像检查："+result.Summary);
        }
        catch(Exception ex) { Error("固件镜像校验",ex); }
    }
    private void InvalidateFlashPreview()
    {
        _previewFlash=null;
        if(FlashPlanSummary is not null)
            FlashPlanSummary.Text="文件已更新；点击开始烧录时将自动校验配置和镜像。";
        if(WorkflowStatusLabel is not null)
            WorkflowStatusLabel.Text="文件就绪后直接开始烧录；程序自动校验参数。";
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
            if(value.Kind!=CanToolSecurityKind.CanToolReferenceBuiltInLfsr)
            {
                display.Text="非已核实 CanTool 参考文件";
                AddLog(field+"：SHA/算法来源未核实；当前不尝试其它 Seed/Key DLL。");
                return null;
            }
            display.Text=Path.GetFileName(path)+" · 原版参考内置算法";
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
    private void ApplySafetyFileFromConfiguration()
    {
        // Internal CanTool reference algorithm; the original SafetyFile.dll
        // is retained as a source-identity input, not invoked as a CLR DLL.
        // Never load KEY.dll or sa_demo.dll, and never silently guess a key.
        string configured=_vm.Configuration.Get("SafetyFilePath");
        string? explicitPath=null;
        if(!string.IsNullOrWhiteSpace(configured))
        {
            try { explicitPath=ResolveReferenceFile(configured); }
            catch(Exception ex)
            {
                SafetyDllStatusLabel.Text="安全文件路径有误："+ex.Message;
                _safetyFilePath=null;
                UpdateButtons();return;
            }
        }
        // An explicit existing file is authoritative, even if its hash is
        // unsupported: do not silently replace it with a different DLL.
        bool needsDiscovery=explicitPath is null || !File.Exists(explicitPath);
        if(needsDiscovery)
        {
            string? discovered=CanToolBundleDiscovery.FindSafetyFileNear(
                _vm.Configuration.SourcePath,
                _vm.Image is null?null:_vm.FirmwarePath);
            if(discovered is not null)
            {
                var info=CanToolSecurityProvider.Inspect(discovered);
                if(info.Kind==CanToolSecurityKind.CanToolReferenceBuiltInLfsr)
                {
                    if(!string.Equals(explicitPath,info.Path,
                        StringComparison.OrdinalIgnoreCase))
                        AddLog("根据 CanTool 配置文件目录自动发现参考安全文件："+info.Path);
                    _vm.Configuration.Set("SafetyFilePath",info.Path);
                    explicitPath=info.Path;
                }
                else
                {
                    SafetyDllStatusLabel.Text="发现 SafetyFile.dll，但文件版本不符合参考证据";
                    _safetyFilePath=null;
                    UpdateButtons();return;
                }
            }
        }
        _safetyFilePath=ResolveAndInspect("SafetyFilePath",
            CanToolSecurityKind.CanToolReferenceBuiltInLfsr,SafetyDllStatusLabel);
        if(_safetyFilePath is null)
        {
            SafetyDllStatusLabel.Text=explicitPath is null
                ? "未找到 SafetyFile.dll；可点击选择，或放在 XML 同目录"
                : "安全文件未就绪，请检查文件路径 / 文件身份";
            WorkflowStatusLabel.Text="已加载的资料缺少可用安全文件；请选择或放在 XML 同目录。";
        }
        else
        {
            SafetyDllStatusLabel.Text="SafetyFile.dll · 已自动匹配参考算法";
            SafetyDllStatusLabel.ToolTip=_safetyFilePath;
        }
        UpdateButtons();
    }

    private void ChooseSafetyFile_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFileDialog
        {
            Title="选择 CanTool 安全文件 SafetyFile.dll",
            Filter="安全文件 (*.dll)|*.dll|全部文件 (*.*)|*.*"
        };
        if(dialog.ShowDialog(this)!=true)return;
        try
        {
            var info=CanToolSecurityProvider.Inspect(dialog.FileName);
            if(info.Kind!=CanToolSecurityKind.CanToolReferenceBuiltInLfsr)
                throw new NotSupportedException(
                    "该安全文件不属于已经静态核实的 CanTool 参考组合。"+
                    "不会自动加载未知 DLL 或尝试错误 Key。");
            _vm.Configuration.Set("SafetyFilePath",info.Path);
            ApplySafetyFileFromConfiguration();
            InvalidateFlashPreview();
            AddLog("已导入烧录安全文件："+info.Path);
        }
        catch(Exception ex){Error("导入烧录安全文件",ex);}
    }

    private CanToolFlashJob BuildFlashJob()
    {
            if(_vm.Image is null || string.IsNullOrWhiteSpace(_vm.FirmwarePath))
                throw new InvalidOperationException("请先选择并离线校验 HEX/S19/BIN 镜像。");
            // Always reload both visible configuration file paths before freezing.
            ApplySafetyFileFromConfiguration();
            if(string.IsNullOrWhiteSpace(_safetyFilePath))
                throw new InvalidOperationException(
                    "缺少 CanTool 安全文件 SafetyFile.dll，或其接口检查未通过。");
            // Rebuild immutable on-wire snapshot using current config, current file
            // and current DLL; no stale UI values or automatic hardware actions.
            var plan=CanToolFlashJob.Freeze(_vm.Configuration,
                _vm.FirmwarePath,BinBaseBox.Text,_safetyFilePath,"",
                LegacyTailSignatureCheck.IsChecked==true);
            // Lock the exact original-reference builtin provider BEFORE any
            // CAN action. It does not dynamically load sa_demo or KEY.dll.
            using(var provider=CanToolSecurityProvider.Open(
                CanToolSecurityProvider.Inspect(plan.SecurityLibraryPath))) { }
            _previewFlash=plan;
            _vm.ResetStages();
            _activeStage=-1;
            FlashProgressBar.Value=0;
            FlashPlanSummary.Text=
                $"ECU（配置身份未自动核验）: {plan.TargetLabel}\n"+
                $"APP TX/RX=0x{plan.App.RequestCanId:X}/0x{plan.App.ResponseCanId:X}\n"+
                $"BOOT TX/RX=0x{plan.Boot.RequestCanId:X}/0x{plan.Boot.ResponseCanId:X}\n"+
                $"CAN1 XML 波特率={plan.ConfiguredBitrate}（未自动配置）\n"+
                $"擦除: 0x{plan.FlashStart:X8}..0x{plan.FlashEndInclusive:X8} "+
                $"({plan.EraseLength:N0}B)\n"+
                $"镜像源字节={plan.SourceBytes:N0}，实际计划块字节={plan.PlannedBytes:N0}，"+
                $"对齐块=0x{plan.AlignmentBlockLength:X}（{plan.AlignmentSource}）\n"+
                $"参考末端签名={(plan.LegacyTailSignature ? "启用：12 34 56 78，可能覆盖原字节" : "未启用")}\n"+
                $"固件 SHA={plan.FirmwareSha256}\n安全算法=CanTool 参考内置 LFSR（未验证目标 ECU 兼容）\n"+
                $"参考文件 SHA={plan.SecuritySha256}";
            FlashStageLabel.Text="文件及烧录地址检查完成；尚未向 ECU 发送请求。";
            WorkflowStatusLabel.Text="已自动检查；请核对实体 ECU、供电与擦除地址。";
            _vm.Status="开始烧录：配置已自动检查，等待最终确认。";
            AddLog("F2 冻结任务已生成 SHA="+plan.Fingerprint+
                "，擦除范围 0x"+plan.FlashStart.ToString("X8")+"..0x"+
                plan.FlashEndInclusive.ToString("X8")+"；当前没有 CAN 发送。");
            return plan;
    }

    private async void StartFlash_Click(object sender,RoutedEventArgs e)
    {
        if(_vm.IsBusy || !_device.IsConnected)return;
        bool physicalCallStarted=false;
        try
        {
            var frozen=BuildFlashJob();
            // Any edited XML field, new disk bytes, DLL or selected BIN base MUST
            // invalidate the displayed physical consent fingerprint.
            var fresh=CanToolFlashJob.Freeze(_vm.Configuration,
                _vm.FirmwarePath,BinBaseBox.Text,_safetyFilePath,"",
                LegacyTailSignatureCheck.IsChecked==true);
            if(!string.Equals(frozen.Fingerprint,fresh.Fingerprint,StringComparison.Ordinal))
                throw new InvalidOperationException("配置、固件或安全文件已改变；请重新点击开始烧录。");
            var choice=MessageBox.Show(this,
                "这是实体 ECU 编程操作，不是仿真。\n\n"+
                $"镜像：{Path.GetFileName(frozen.FirmwarePath)}\n"+
                $"ECU 身份未自动识别；请核对目标实物与配置。\n"+
                $"XML 波特率：{frozen.ConfiguredBitrate}；主控固件预期 500K，实际物理速率未测量。\n"+
                $"擦除全部地址：0x{frozen.FlashStart:X8}..0x{frozen.FlashEndInclusive:X8}\n"+
                $"固件 SHA256：{frozen.FirmwareSha256}\n"+
                $"安全算法：CanTool 参考内置 LFSR（不是外部 DLL，尚未证实 ECU 兼容）\n"+
                $"参考文件 SHA256：{frozen.SecuritySha256}\n"+
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
            physicalCallStarted=true;
            var outcome=await _device.ProgramCanToolAsync(frozen,frozen.Consent,
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
            if(outcome.TransferAcknowledged!=frozen.PlannedBytes ||
               outcome.VerifiedBytes!=frozen.PlannedBytes ||
               !outcome.ResetAcknowledged || !outcome.AppSessionObserved)
                throw new InvalidDataException(
                    "ECU 烧录流程返回但缺少完整传输、校验、复位或 APP 应答证据；不能判定成功。");
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
            if(!physicalCallStarted)
            {
                FlashStageLabel.Text="烧录准备失败（尚未开始 CAN 烧录请求）："+ex.Message;
                WorkflowStatusLabel.Text="准备阶段失败：检查配置、固件和安全文件。";
                Error("烧录准备",ex);
            }
            else
            {
                FlashStageLabel.Text="烧录未确认成功；ECU 物理状态可能未知："+ex.Message;
                if(_activeStage>=0)_vm.MarkStage(_activeStage,"失败");
                WorkflowStatusLabel.Text="烧录停止于："+(_activeStage>=0?_vm.Stages[_activeStage].Step:"编程请求阶段");
                Error("实体 CanTool 烧录",ex);
            }
        }
        finally
        {
            _flashStop?.Dispose();_flashStop=null;
            _previewFlash=null;
            Busy(false);
            UpdateButtons();
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
