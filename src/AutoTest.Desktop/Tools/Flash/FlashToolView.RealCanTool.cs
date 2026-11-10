using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using AutoTest.Devices.Can;
using AutoTest.Devices.Diagnostics;
using AutoTest.Devices.Usb;
using AutoTest.Flash;
using AutoTest.Specialists.Flash;
using Microsoft.Win32;

namespace AutoTest.Desktop.Tools.Flash;

/// <summary>
/// P09 physical CanTool job. This is a VIEW-only coordinator: the engine lives
/// in AutoTest.Flash, the physical session belongs exclusively to R1 Station,
/// and the project's preflight model can never issue physical write rights.
/// No local HardwareBenchController, fake ECU or second CAN hub is constructed.
/// </summary>
public partial class FlashToolView
{
    private readonly ObservableCollection<CanToolFlashStage> _physicalStages = new();
    private readonly ObservableCollection<string> _physicalEvents = new();
    private string? _r1ConfigPath;
    private string? _preparedPhrase;
    private string? _preparedConfigHash;
    private string? _preparedImageHash;
    private string? _preparedOptionsFingerprint;
    private long _preparedGeneration;
    private CancellationTokenSource? _physicalCancel;
    private bool _physicalRunning;
    private bool _exitRequested;

    public bool IsPhysicalCan1Busy => _physicalRunning;
    public event EventHandler? PhysicalOperationStopped;

    private void InitializeCanToolPanel()
    {
        FlashStagesGrid.ItemsSource = _physicalStages;
        FlashEventsList.ItemsSource = _physicalEvents;
        RefreshStationInfo();
    }

    private void AppendPhysical(string category, string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            if (!Dispatcher.HasShutdownStarted)
                Dispatcher.BeginInvoke(new Action(() => AppendPhysical(category, message)));
            return;
        }
        _physicalEvents.Add($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{category}] {message}");
        if (_physicalEvents.Count > 3000) _physicalEvents.RemoveAt(0);
        if (_physicalEvents.Count > 0) FlashEventsList.ScrollIntoView(_physicalEvents[^1]);
    }

    private void OnPhysicalStage(CanToolFlashStage stage)
    {
        if (!Dispatcher.CheckAccess())
        {
            if (!Dispatcher.HasShutdownStarted)
                Dispatcher.BeginInvoke(new Action(() => OnPhysicalStage(stage)));
            return;
        }
        _physicalStages.Add(stage);
        if (_physicalStages.Count > 8000) _physicalStages.RemoveAt(0);
        FlashStagesGrid.ScrollIntoView(stage);
        FlashProgressLine.Text = $"{stage.Stage} · {stage.Outcome} · {stage.ConfirmedBytes:N0}/{stage.TotalBytes:N0} B";
        FlashProgressBar.Value = stage.TotalBytes == 0 ? 0 :
            100 * Math.Clamp((double)stage.ConfirmedBytes / stage.TotalBytes, 0, 1);
        AppendPhysical("STAGE", $"{stage.Stage} {stage.Outcome} {stage.Detail}");
    }

    private void OnPhysicalUsb(UsbTraceRecord record) =>
        AppendPhysical("USB " + record.Kind,
            $"gen={record.Generation}; seq={record.Sequence}; {record.Detail}; raw={record.RawHex}");

    private void OnPhysicalCan(HardwareCanTrace record)
    {
        if (record.PhysicalPort != 1) return;
        AppendPhysical("CAN1", $"gen={record.SessionGeneration}; valid={record.Valid}; " +
            $"{record.Detail}; rawUSB={record.RawUsbPayloadHex}");
    }

    private void SubscribePhysical(HardwareBenchController device)
    {
        device.UsbTrace += OnPhysicalUsb;
        device.CanTrace += OnPhysicalCan;
    }

    private void UnsubscribePhysical(HardwareBenchController device)
    {
        device.UsbTrace -= OnPhysicalUsb;
        device.CanTrace -= OnPhysicalCan;
    }

    private void RefreshStationInfo()
    {
        var device = _physicalProvider?.Invoke();
        PhysicalSessionLine.Text = device is null
            ? "未打开 R1 Station，请先在 P02 建立唯一主控会话。"
            : $"Station: {device.State} · USB Gen={device.Generation} · GET_VERSION verified={device.ProtocolVersionVerified} · DUT CAN1";
    }

    private void ResetPhysicalApproval(string reason)
    {
        _preparedPhrase = null;
        _preparedConfigHash = null;
        _preparedImageHash = null;
        _preparedOptionsFingerprint = null;
        _preparedGeneration = 0;
        FlashExpectedAuthorization.Text = "";
        FlashTypedAuthorization.Text = "";
        FlashStartButton.IsEnabled = false;
        AppendPhysical("AUTH_REVOKED", reason);
    }

    private void SetPhysicalBusy(bool busy)
    {
        _physicalRunning = busy;
        FlashProbeButton.IsEnabled = !busy;
        FlashPrepareButton.IsEnabled = !busy;
        FlashCancelButton.IsEnabled = busy;
        FlashStartButton.IsEnabled = !busy && !string.IsNullOrEmpty(_preparedPhrase);
    }

    private async Task<HardwareBenchController> GetVerifiedControllerAsync(CancellationToken ct)
    {
        var controller = _physicalProvider?.Invoke()
            ?? throw new InvalidOperationException("R1 Station 尚未创建：请先打开 P02 设备连接。");
        if (controller.State != UsbSessionState.Connected || controller.Generation <= 0)
            throw new IOException("主控没有建立有效 WinUSB 会话。请在 R1 Station 连接主控。");
        if (!controller.ProtocolVersionVerified)
            await controller.ReadOnlyCommandAsync(FirmwareReadOnlyProtocol.GetVersion,
                TimeSpan.FromSeconds(3), ct);
        if (!controller.ProtocolVersionVerified)
            throw new IOException("主控 GET_VERSION 验证失败；拒绝 CAN1 诊断发送。");
        RefreshStationInfo();
        return controller;
    }

    private CanToolConfigurationSnapshot CurrentVerifiedConfig()
    {
        if (string.IsNullOrWhiteSpace(_r1ConfigPath))
            throw new InvalidOperationException("请先在 P09 导入真实 CanTool Configuration.xml。");
        if (DataContext is not FlashToolViewModel vm || vm.CurrentConfiguration is null)
            throw new InvalidOperationException("R1 尚未完成 XML 解析。");
        var current = CanToolConfigurationImporter.LoadFile(_r1ConfigPath);
        if (!string.Equals(vm.CurrentConfiguration.SourceSha256, current.SourceSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new IOException("XML 自导入后已在磁盘上改变；请重新导入并核对。");
        return current;
    }

    private static FirmwareImage ParseSourceImage(string path, uint binAddress)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length is <= 0 or > FirmwareImageParser.MaxImageBytes)
            throw new IOException("固件不存在、为空或超过 16 MiB 上限。");
        var bytes = File.ReadAllBytes(file.FullName);
        return file.Extension.ToLowerInvariant() switch
        {
            ".bin" => FirmwareImageParser.ParseBin(bytes, binAddress),
            ".hex" or ".ihex" or ".h86" => FirmwareImageParser.ParseHex(
                new UTF8Encoding(false, true).GetString(bytes)),
            ".s19" or ".s28" or ".s37" or ".srec" or ".mot" or ".sx" =>
                FirmwareImageParser.ParseS19(new UTF8Encoding(false, true).GetString(bytes)),
            _ => throw new InvalidDataException("仅支持 BIN / Intel HEX / S19。")
        };
    }

    private FirmwareImage CurrentVerifiedImage()
    {
        if (DataContext is not FlashToolViewModel vm || vm.CurrentImage is null ||
            string.IsNullOrWhiteSpace(vm.ImagePath))
            throw new InvalidOperationException("请先选取并解析固件镜像。");
        var address = uint.Parse(vm.BinAddress.Trim().Replace("0x", "",
            StringComparison.OrdinalIgnoreCase), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var image = ParseSourceImage(vm.ImagePath, address);
        if (!string.Equals(vm.CurrentImage.SourceFingerprint.Sha256, image.SourceFingerprint.Sha256,
                StringComparison.OrdinalIgnoreCase) ||
            image.ByteCount != vm.CurrentImage.ByteCount ||
            image.Segments.Length != vm.CurrentImage.Segments.Length)
            throw new IOException("固件内容或 BIN 地址自上次解析后发生变化；必须重新解析。");
        // A BIN image may have identical raw-file SHA but a DIFFERENT flash base
        // address. Physical authorization must pin the complete address map.
        for (int i = 0; i < image.Segments.Length; i++)
        {
            var previouslyParsed = vm.CurrentImage.Segments[i];
            var currentlyOnDisk = image.Segments[i];
            if (previouslyParsed.Address != currentlyOnDisk.Address ||
                !previouslyParsed.Data.AsSpan().SequenceEqual(currentlyOnDisk.Data.AsSpan()))
                throw new IOException("镜像地址映射或固件数据已改变；拒绝使用旧授权烧录。");
        }
        return image;
    }

    private static int Number(string value, string name) =>
        int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result : throw new FormatException(name + " 必须是十进制整数。");

    private CanToolFlashOptions ReadOptions() => new(
        SecurityDllPath.Text.Trim(), FlashTypedAuthorization.Text.Trim(),
        checked((int)uint.Parse(FlashBlockLength.Text.Trim().Replace("0x", "",
            StringComparison.OrdinalIgnoreCase), NumberStyles.HexNumber, CultureInfo.InvariantCulture)),
        Number(FlashP2.Text, "P2"), Number(FlashP2Star.Text, "P2*"),
        Number(FlashErase.Text, "Erase"), Number(FlashFc.Text, "ISO-TP FC"),
        FlashSignature.IsChecked == true, FlashPowerConfirmed.IsChecked == true,
        FlashRateConfirmed.IsChecked == true, FlashEraseConfirmed.IsChecked == true);

    private string OptionsFingerprint(CanToolFlashOptions options) =>
        string.Join("|",
            (DataContext as FlashToolViewModel)?.BinAddress ?? "",
            (DataContext as FlashToolViewModel)?.Target ?? "",
            (DataContext as FlashToolViewModel)?.ExpectedIdentity ?? "",
            options.SecurityDllPath,
            options.AlignedBlockLength,
            options.P2TimeoutMs,
            options.P2StarTimeoutMs,
            options.EraseTimeoutMs,
            options.FlowControlTimeoutMs,
            options.InsertCanToolEndSignature,
            options.OperatorConfirmedEcuAndPower,
            options.OperatorConfirmedCanBitrate,
            options.AuthorizePhysicalProgramming,
            File.Exists(options.SecurityDllPath)
                ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(options.SecurityDllPath)))
                : "DLL_MISSING");

    private void OpenStationFromFlash_Click(object sender, RoutedEventArgs e)
    {
        var shell = Window.GetWindow(this);
        if (shell?.FindName("WorkbenchTabs") is TabControl tabs)
            tabs.SelectedIndex = 1;
        else
            AppendPhysical("STATION", "未定位主 Shell 的 Station 页面。");
    }

    private void BrowseSecurityDll_Click(object sender, RoutedEventArgs e)
    {
        if (_physicalRunning) return;
        var picker = new OpenFileDialog
        {
            Title = "选择与真实 ECU 匹配的原生 ZLGKey 安全算法 DLL",
            Filter = "原生 DLL (*.dll)|*.dll",
            CheckFileExists = true
        };
        if (picker.ShowDialog(Window.GetWindow(this)) != true) return;
        SecurityDllPath.Text = picker.FileName;
        ResetPhysicalApproval("Seed/Key DLL 选择改变。");
    }

    private async void ProbePhysical_Click(object sender, RoutedEventArgs e)
    {
        if (_physicalRunning) return;
        HardwareBenchController? controller = null;
        try
        {
            var config = CurrentVerifiedConfig();
            controller = await GetVerifiedControllerAsync(CancellationToken.None);
            if (MessageBox.Show(Window.GetWindow(this),
                $"向实际 DUT CAN1 发送 Boot / APP 0x10 03 探测。\n" +
                $"BOOT TX/RX：0x{config.BootRequestId:X}/0x{config.BootResponseId:X}\n" +
                $"APP TX/RX：0x{config.AppRequestId:X}/0x{config.AppResponseId:X}\n\n" +
                "此操作改变 ECU 诊断会话，不解锁、不擦除、不下载。确认发送？",
                "R1 / 真实 CAN1 诊断会话探测", MessageBoxButton.YesNo,
                MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;

            ResetPhysicalApproval("实际 ECU 会话探测会使此前的授权失效。");
            _physicalStages.Clear();
            SetPhysicalBusy(true);
            _physicalCancel = new CancellationTokenSource();
            SubscribePhysical(controller);
            AppendPhysical("PROBE_BEGIN", "开始真实 CAN1 Boot/APP 10 03，非模拟。");
            var engine = new CanToolFlashEngine(controller,
                new Progress<CanToolFlashStage>(OnPhysicalStage));
            var outcome = await engine.ProbeAsync(config, _physicalCancel.Token);
            PhysicalResultLine.Text = $"{outcome.Mode} · UDS={outcome.ResponseHex} · {outcome.Error ?? "ECU 实际响应已记录"}";
            AppendPhysical("PROBE_END", PhysicalResultLine.Text);
        }
        catch (Exception ex)
        {
            PhysicalResultLine.Text = "CAN1 探测失败：" + ex.Message;
            AppendPhysical("PROBE_ERROR", ex.ToString());
        }
        finally
        {
            if (controller is not null) UnsubscribePhysical(controller);
            _physicalCancel?.Dispose();
            _physicalCancel = null;
            SetPhysicalBusy(false);
            if (_exitRequested) PhysicalOperationStopped?.Invoke(this, EventArgs.Empty);
        }
    }

    private async void PreparePhysicalFlash_Click(object sender, RoutedEventArgs e)
    {
        if (_physicalRunning) return;
        ResetPhysicalApproval("重新核对本次烧录资料及会话。");
        try
        {
            var controller = await GetVerifiedControllerAsync(CancellationToken.None);
            var config = CurrentVerifiedConfig();
            var image = CurrentVerifiedImage();
            var options = ReadOptions();
            options.Validate();
            if (!config.ImageFits(image))
                throw new InvalidDataException("镜像地址段不在 CanTool XML 的 Flash 区间内。");
            var blocks = CanToolFlashEngine.BuildCanToolAlignedBlocks(
                config, image, options.AlignedBlockLength, options.InsertCanToolEndSignature);
            using var key = new NativeCanToolKeyProvider(options.SecurityDllPath);
            var phrase = HardwareBenchController.PhysicalCanToolConfirmation(
                config.SourceSha256, image.SourceFingerprint.Sha256,
                config.AppRequestId, config.BootRequestId, controller.Generation);
            _preparedPhrase = phrase;
            _preparedConfigHash = config.SourceSha256;
            _preparedImageHash = image.SourceFingerprint.Sha256;
            _preparedGeneration = controller.Generation;
            _preparedOptionsFingerprint = OptionsFingerprint(options);
            FlashExpectedAuthorization.Text = phrase;
            FlashTypedAuthorization.Text = "";
            FlashStartButton.IsEnabled = true;
            PhysicalResultLine.Text = "已完成配置/固件/原生 DLL 和当前 USB Generation 核对。" +
                $"实际刷写范围 0x{config.FlashStart:X8}..0x{config.FlashEndInclusive:X8}；" +
                $"对齐块={blocks.Length}，总计={blocks.Sum(x => x.Data.Length):N0} B。" +
                " 仍未对实际 ECU 执行任何擦除。";
            AppendPhysical("AUTH_PREPARED",
                $"XML={config.SourceSha256} IMAGE={image.SourceFingerprint.Sha256} " +
                $"DLL={key.Sha256} GEN={controller.Generation} BLOCKS={blocks.Length}");
        }
        catch (Exception ex)
        {
            PhysicalResultLine.Text = "烧录预检 / 授权失败：" + ex.Message;
            AppendPhysical("AUTH_BLOCKED", ex.ToString());
        }
    }

    private async void StartPhysicalFlash_Click(object sender, RoutedEventArgs e)
    {
        if (_physicalRunning) return;
        HardwareBenchController? controller = null;
        try
        {
            controller = await GetVerifiedControllerAsync(CancellationToken.None);
            var config = CurrentVerifiedConfig();
            var image = CurrentVerifiedImage();
            var options = ReadOptions();
            options.Validate();
            if (string.IsNullOrWhiteSpace(_preparedPhrase) ||
                !string.Equals(options.TypedAuthorization, _preparedPhrase, StringComparison.Ordinal) ||
                !string.Equals(_preparedConfigHash, config.SourceSha256, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(_preparedImageHash, image.SourceFingerprint.Sha256, StringComparison.OrdinalIgnoreCase) ||
                _preparedGeneration != controller.Generation ||
                !string.Equals(_preparedOptionsFingerprint, OptionsFingerprint(options), StringComparison.Ordinal))
                throw new UnauthorizedAccessException(
                    "本次人工授权、固件、XML、DLL、参数或 USB 世代不匹配；请重新生成授权。");
            if (!config.ImageFits(image))
                throw new InvalidDataException("镜像超出 XML Flash 区间。");
            if (MessageBox.Show(Window.GetWindow(this),
                $"即将真实操作 ECU 的 Flash，不能保证失败后自动恢复。\n\n" +
                $"APP TX/RX=0x{config.AppRequestId:X}/0x{config.AppResponseId:X}\n" +
                $"BOOT TX/RX=0x{config.BootRequestId:X}/0x{config.BootResponseId:X}\n" +
                $"擦除范围：0x{config.FlashStart:X8}..0x{config.FlashEndInclusive:X8}\n" +
                $"Image SHA256={image.SourceFingerprint.Sha256}\n" +
                $"XML SHA256={config.SourceSha256}\n\n" +
                "确认本次对真实 ECU 执行安全解锁、擦除、下载和复位？",
                "R1 P09 / 最终物理刷写确认", MessageBoxButton.YesNo,
                MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;

            SetPhysicalBusy(true);
            _physicalStages.Clear();
            FlashProgressBar.Value = 0;
            _physicalCancel = new CancellationTokenSource();
            SubscribePhysical(controller);
            AppendPhysical("FLASH_BEGIN", "ACTUAL CAN1 · " + options.TypedAuthorization);
            var engine = new CanToolFlashEngine(controller,
                new Progress<CanToolFlashStage>(OnPhysicalStage));
            var result = await engine.RunAsync(config, image, options, _physicalCancel.Token);
            PhysicalResultLine.Text =
                $"{result.Status} · {result.LastStage} · ACK={result.ConfirmedBytes:N0}/{result.TotalBytes:N0} B\n" +
                result.Detail;
            AppendPhysical("FLASH_END", PhysicalResultLine.Text.Replace("\n", " | "));
        }
        catch (Exception ex)
        {
            PhysicalResultLine.Text =
                "真实刷写失败或作业状态不确定。不要直接重试；先检查原始 CAN/USB 证据及 ECU 状态。" +
                "\n" + ex.GetType().Name + ": " + ex.Message;
            AppendPhysical("FLASH_ERROR_UNCERTAIN", ex.ToString());
        }
        finally
        {
            if (controller is not null) UnsubscribePhysical(controller);
            _physicalCancel?.Dispose();
            _physicalCancel = null;
            ResetPhysicalApproval("本次真实作业结束，所有权限已撤销。");
            SetPhysicalBusy(false);
            if (_exitRequested) PhysicalOperationStopped?.Invoke(this, EventArgs.Empty);
        }
    }

    private void CancelPhysicalFlash_Click(object sender, RoutedEventArgs e) => RequestPhysicalCancel();

    public void RequestPhysicalCancel()
    {
        if (!_physicalRunning) return;
        _physicalCancel?.Cancel();
        FlashCancelButton.IsEnabled = false;
        AppendPhysical("CANCEL_REQUESTED", "已请求取消；如曾擦除或写入，ECU 状态可能 UNCERTAIN。");
    }

    public void RequestCloseAfterPhysicalStop()
    {
        _exitRequested = true;
        RequestPhysicalCancel();
    }

    private void ExportPhysicalFlashLog_Click(object sender, RoutedEventArgs e)
    {
        var picker = new SaveFileDialog
        {
            Title = "导出 R1 P09 CAN1 烧录诊断日志",
            Filter = "文本日志 (*.txt)|*.txt",
            FileName = "R1-P09-CanTool-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt"
        };
        if (picker.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            File.WriteAllLines(picker.FileName, _physicalEvents, new UTF8Encoding(false));
            AppendPhysical("EXPORT", picker.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), ex.Message, "日志导出失败",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
