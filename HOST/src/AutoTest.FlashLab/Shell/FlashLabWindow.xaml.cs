using System.ComponentModel;
using System.Collections.Concurrent;
using System.Windows.Threading;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
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
    private string? _nativeProfileRoot;
    private string? _nativeProfileSha;
    public string? NativeProjectRoot { get; set; }
    public string? NativeProjectModelCode { get; set; }
    private bool _closingApproved;
    private bool _closingInProgress;
    private bool? _compactFlashLayout;

    private void ApplyResponsiveLayout()
    {
        if (!IsLoaded || ActualWidth <= 0) return;
        var compact = ActualWidth < 1160;
        if (_compactFlashLayout == compact) return;
        _compactFlashLayout = compact;
        if (compact)
        {
            FlashConfigColumn.Width = new GridLength(292);
            FlashConfigGap.Width = new GridLength(6);
            FlashWorkflowColumn.Width = new GridLength(1, GridUnitType.Star);
            FlashMonitorGap.Width = new GridLength(0);
            FlashCanColumn.Width = new GridLength(0);
            FlashPrimaryRow.Height = new GridLength(1.3, GridUnitType.Star);
            FlashMonitorRow.Height = new GridLength(1, GridUnitType.Star);
            Grid.SetRowSpan(FlashConfigPanel, 2);
            Grid.SetRowSpan(FlashConfigSplitter, 2);
            FlashMonitorSplitter.Visibility = Visibility.Collapsed;
            Grid.SetRow(FlashWorkflowPanel, 0);
            Grid.SetColumn(FlashCanPanel, 2);
            Grid.SetRow(FlashCanPanel, 1);
            FlashCanPanel.Margin = new Thickness(0, 8, 0, 0);
        }
        else
        {
            Grid.SetRowSpan(FlashConfigPanel, 1);
            Grid.SetRowSpan(FlashConfigSplitter, 1);
            FlashMonitorSplitter.Visibility = Visibility.Visible;
            Grid.SetRow(FlashWorkflowPanel, 0);
            Grid.SetRow(FlashCanPanel, 0);
            Grid.SetColumn(FlashCanPanel, 4);
            FlashCanPanel.Margin = new Thickness(0);
            FlashConfigColumn.Width = new GridLength(304);
            FlashConfigGap.Width = new GridLength(6);
            FlashWorkflowColumn.Width = new GridLength(405);
            FlashMonitorGap.Width = new GridLength(6);
            FlashCanColumn.Width = new GridLength(1, GridUnitType.Star);
            FlashPrimaryRow.Height = new GridLength(1, GridUnitType.Star);
            FlashMonitorRow.Height = new GridLength(0);
        }
    }

    public FlashLabWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => FlashWindowSizing.Fit(this);
        DataContext=_vm;
        Loaded += (_, _) => ApplyResponsiveLayout();
        SizeChanged += (_, _) => ApplyResponsiveLayout();
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
        Loaded += async (_, _) =>
        {
            await RefreshDevicesAsync();
            await LoadEngineeringNativeProfileAsync();
        };
        UpdateButtons();
        AddLog("ECU 烧录工作台已启动。当前工程优先使用原生烧录配置；没有工程时仍可进入独立参考调试。");
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
        // Stage 09 is completed ONLY after the ECU's positive 51 01 reply,
        // not when the reset request is sent or when the next phase starts.
        if(line.Contains("S9 Boot ECUReset：已收到 51 01",StringComparison.Ordinal))
        {
            _vm.MarkStage(8,"已完成");
            _activeStage=8;
            WorkflowStatusLabel.Text="ECU 已确认 Boot 51 01 复位；APP 启动尚未验证。";
            return;
        }
        if(line.Contains("S10 参考 CanTool 烧录已结束",StringComparison.Ordinal))
        {
            _vm.MarkStage(8,"已完成");
            _vm.MarkStage(9,"待核验");
            _activeStage=9;
            WorkflowStatusLabel.Text="参考烧录完成；APP 启动与版本待单独核验。";
            return;
        }
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
        StartFlashButton.IsEnabled=!_closingInProgress && !_vm.IsBusy &&
            _device.IsConnected && _nativeProfileRoot is not null;
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
                // WinUsbDiscovery is the single authoritative enumerator.
                // There is no second PnP probe or generic USB scan here.
                _vm.Status="未找到主控 WinUSB 接口（VID_1183 / PID_5850）。请检查主控供电、USB 连接及 WinUSB 驱动。";
                UsbStatusLabel.Text=_vm.Status;
                AddLog("USB 枚举诊断："+_vm.Status+
                    " 仅检查主控已注册的 WinUSB 设备接口；未执行其他 USB 设备扫描。");
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
        UseLegacyConfiguration();
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
            // A new XML must never inherit the preceding ECU's tail option.
            LegacyTailSignatureCheck.IsChecked=false;
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
        UseLegacyConfiguration();
        try
        {
            var cfg=CanToolXmlProfileReader.Load(dialog.FileName);
            _vm.ApplyConfiguration(cfg);
            // A new ECU profile starts without the old profile's tail choice.
            LegacyTailSignatureCheck.IsChecked=false;
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
        UseLegacyConfiguration();
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
        UseLegacyConfiguration();
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
        UseLegacyConfiguration();
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

    private async Task LoadEngineeringNativeProfileAsync()
    {
        // Native-only FLASH: project uses its own parameters; standalone
        // tools use a persistent local native profile. Neither needs XML,
        // a user-selected safety DLL, or a second parameter editor.
        var root = NativeStorageRoot;
        try
        {
            var current = NativeProjectRoot is not null
                ? await NativeFlashProfileStore.EnsureHydEpbReferenceAsync(
                    root, NativeProjectModelCode)
                : await NativeFlashProfileStore.LoadAsync(root);
            if (current is null)
            {
                NativeProfileLabel.Text = "尚未设置 HEX 与烧录参数";
                AddLog("当前工程缺少烧录配置，请先编辑并保存。");
                return;
            }
            if (!string.IsNullOrWhiteSpace(NativeProjectModelCode) &&
                !string.Equals(current.Profile.EcuModelCode, NativeProjectModelCode,
                    StringComparison.Ordinal))
                throw new InvalidDataException(
                    "烧录配置与当前工程不匹配，请检查。");
            _nativeProfileRoot = root;
            _nativeProfileSha = current.ProfileSha256;
            NativeProfileLabel.Text = current.Profile.FirmwareDisplayName is { Length: > 0 } name
                ? "当前 HEX：" + name + " · ECU CAN " + current.Profile.EcuCanKbps + " kbit/s"
                : "已配置烧录参数；尚未选择 HEX";
            _vm.Status = "已读取原生烧录参数，开始前会核对实际 HEX 与地址。";
            InvalidateFlashPreview();
            AddLog("已载入工程原生配置（未发送 CAN）：" + current.FullPath);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or
            UnauthorizedAccessException or FormatException or NotSupportedException)
        {
            _nativeProfileRoot = null;
            _nativeProfileSha = null;
            _vm.Status = "工程烧录配置需要检查：" + error.Message;
            AddLog(_vm.Status);
        }
    }

    private string NativeStorageRoot => NativeProjectRoot ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AutoTest", "FlashLabStandalone");

    private async void OpenNativeProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.IsBusy || _closingInProgress) return;
        try
        {
            var root = NativeStorageRoot;
            var window = new NativeFlashProfileWindow(root) { Owner = this };
            window.ShowDialog();
            var current = await NativeFlashProfileStore.LoadAsync(root);
            if (current is null)
            {
                _vm.Status = "烧录配置尚未保存，当前设置保持不变。";
                return;
            }
            if (!string.IsNullOrWhiteSpace(NativeProjectModelCode) &&
                !string.Equals(NativeProjectModelCode, current.Profile.EcuModelCode,
                    StringComparison.Ordinal))
                throw new InvalidDataException("工程型号已改变；原生烧录配置必须重新审核。");
            _nativeProfileRoot = root;
            _nativeProfileSha = current.ProfileSha256;
            NativeProfileLabel.Text = current.Profile.FirmwareDisplayName is { Length: > 0 } name
                ? "当前 HEX：" + name + " · ECU CAN " + current.Profile.EcuCanKbps + " kbit/s"
                : "烧录参数已保存，尚未选择 HEX";
            _vm.Status = "烧录参数已更新；开始前会重新检查实际数据。";
            InvalidateFlashPreview();
            AddLog("原生 Profile 已载入：" + current.FullPath +
                "；当前只是离线配置，不执行 ECU 操作。");
        }
        catch (Exception error)
        {
            Error("原生烧录配置", error);
        }
    }

    [Obsolete("Legacy CanTool XML workflows are retired in AutoTest R1.")]
    private void UseLegacyConfiguration() =>
        throw new NotSupportedException(
            "旧 XML/三文件导入已停用。请使用当前工程的原生烧录参数。");

    private CanToolFlashJob BuildNativeJob()
    {
        var root = _nativeProfileRoot ??
            throw new InvalidOperationException("没有选中的原生烧录配置。");
        var latest = NativeFlashProfileStore.LoadAsync(root).GetAwaiter().GetResult()
            ?? throw new IOException("原生 Profile 文件已删除，请重新载入。");
        if (!string.Equals(latest.ProfileSha256, _nativeProfileSha,
                StringComparison.OrdinalIgnoreCase))
            throw new IOException("原生 Profile 保存后发生修改，请重新载入确认。");
        var job = NativeFlashJobBuilder.Freeze(latest.Profile, root);
        _previewFlash = job;
        _vm.ResetStages();
        _activeStage = -1;
        FlashProgressBar.Value = 0;
        FlashPlanSummary.Text =
            $"ECU：{latest.Profile.EcuModelCode}\n" +
            $"固件：{latest.Profile.FirmwareDisplayName ?? "已选 HEX"}\n" +
            $"APP TX/RX=0x{job.App.RequestCanId:X}/0x{job.App.ResponseCanId:X}\n" +
            $"BOOT TX/RX=0x{job.Boot.RequestCanId:X}/0x{job.Boot.ResponseCanId:X}\n" +
            $"ECU CAN={job.ConfiguredBitrate}\n" +
            $"擦除范围=0x{job.FlashStart:X8}..0x{job.FlashEndInclusive:X8}\n" +
            $"实际下载={job.Blocks.Length} 块 / {job.PlannedBytes:N0} 字节";
        FlashStageLabel.Text = "已从原生配置冻结真实 CanTool Job；尚未发送 CAN。";
        WorkflowStatusLabel.Text = "请核对实际 ECU 供电、固件及擦除地址，再单独授权开始。";
        _vm.Status = "烧录配置检查通过，可继续确认烧录。";
        return job;
    }

    private CanToolFlashJob BuildFlashJob()
    {
        // The legacy CanTool XML / per-project DLL route is retired. The same
        // native settings path is used by independent FLASH and production.
        if (_nativeProfileRoot is null)
            throw new InvalidOperationException(
                "请先在“编辑烧录配置”中保存 HEX 和烧录参数。");
        return BuildNativeJob();
    }

    private async void StartFlash_Click(object sender,RoutedEventArgs e)
    {
        if(_vm.IsBusy || !_device.IsConnected)return;
        bool physicalCallStarted=false;
        try
        {
            var frozen=BuildFlashJob();
            // Real programming always re-reads the ONE native configuration.
            // The old CanTool XML / external security DLL branch is retired.
            var root = _nativeProfileRoot ??
                throw new InvalidOperationException("请先配置 HEX 和原生烧录参数。");
            var latest = NativeFlashProfileStore.LoadAsync(root).GetAwaiter().GetResult()
                ?? throw new IOException("烧录参数已丢失，请重新打开配置。");
            if (!string.Equals(latest.ProfileSha256, _nativeProfileSha,
                    StringComparison.OrdinalIgnoreCase))
                throw new IOException("烧录参数已修改，请重新打开配置并重新确认。");
            var fresh = NativeFlashJobBuilder.Freeze(latest.Profile, root);
            if(!string.Equals(frozen.Fingerprint,fresh.Fingerprint,StringComparison.Ordinal))
                throw new InvalidOperationException("配置、固件或安全文件已改变；请重新点击开始烧录。");
            // The operator needs a decision, not a technical audit printout.
            // Complete hashes, source identity, CAN IDs, alignment and signature
            // remain in the read-only plan and diagnostics, not this modal.
            AddLog(
                $"烧录确认详情：planSHA={frozen.Fingerprint}; "+
                $"firmwareSHA={frozen.FirmwareSha256}; "+
                $"securitySource={frozen.SecurityProviderKind}; "+
                $"securityFileSHA={frozen.SecuritySha256}; "+
                $"CAN={frozen.ConfiguredBitrate}; "+
                $"endSignature={(frozen.LegacyTailSignature?"on":"off")}; "+
                $"erase=0x{frozen.FlashStart:X8}..0x{frozen.FlashEndInclusive:X8}");
            var choice=MessageBox.Show(this,
                $"固件：{Path.GetFileName(frozen.FirmwarePath)}\n"+
                $"擦除范围：0x{frozen.FlashStart:X8} - 0x{frozen.FlashEndInclusive:X8}\n\n"+
                "将对真实 ECU 擦除并写入，请确认型号与固件匹配、供电稳定。\n"+
                "请核对固件与 ECU 型号是否一致。\n\n"+
                "确定开始烧录？",
                "确认开始烧录",MessageBoxButton.YesNo,
                MessageBoxImage.Warning,MessageBoxResult.No);
            if(choice!=MessageBoxResult.Yes)return;

            _flashStop=new CancellationTokenSource();
            Busy(true);
            FlashStageLabel.Text="烧录中，请保持供电稳定，不要断开连接。";
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
            // The proven CanTool on-wire sequence ends at Boot 51 01.
            // Do not turn an OPTIONAL post-reset APP 10 03 timeout into an
            // incorrect claim that the fully ACKed/verified flash failed.
            if(outcome.TransferAcknowledged!=frozen.PlannedBytes ||
               outcome.VerifiedBytes!=frozen.PlannedBytes ||
               !outcome.ResetAcknowledged)
                throw new InvalidDataException(
                    "ECU 缺少完整 76/77、逐块 71 或 Boot 51 01 应答；写入未确认完成。");
            FlashStageLabel.Text=
                $"参考 CanTool 物理烧录结束：{outcome.Outcome}\n"+
                $"传输确认={outcome.TransferAcknowledged:N0}B, "+
                $"逐块校验={outcome.VerifiedBytes:N0}B, "+
                $"Boot复位确认={outcome.ResetAcknowledged}；"+
                $"APP会话确认={outcome.AppSessionObserved}。";
            // The reset row must not remain "进行中"/"已结束" after
            // validated 51 01, even when the optional APP state is unknown.
            _vm.MarkStage(8,"已完成");
            _activeStage=9;
            if(outcome.AppSessionObserved)
            {
                _vm.MarkStage(9,"已完成");
                WorkflowStatusLabel.Text="参考烧录完成；APP 已响应，版本及身份仍待核验。";
            }
            else
            {
                _vm.MarkStage(9,"待核验");
                WorkflowStatusLabel.Text=
                    "参考烧录的写入、逐块校验和复位已确认；APP 启动尚未确认，请实测核验。";
            }
            _vm.Status=WorkflowStatusLabel.Text;
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
