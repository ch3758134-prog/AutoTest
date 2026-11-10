using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using AutoTest.Devices.Can;
using AutoTest.Devices.Diagnostics;
using AutoTest.Devices.Usb;
using AutoTest.Specialists.Flash;
using Microsoft.Win32;

namespace AutoTest.FlashDiag;

public sealed record FlashStageRow(string When, string Stage, string Outcome, string Detail, string Confirmed);
public sealed record FlashSegmentRow(string Start, string End, long Bytes);

/// <summary>
/// Independent CanTool flash debug GUI. It has exactly one HardwareBenchController,
/// one WinUSB session and one physical CAN1 bus, not an R1 Desktop Shell dependency.
/// </summary>
public partial class MainWindow : Window
{
    private readonly HardwareBenchController _device = new();
    public ObservableCollection<string> Events { get; } = new();
    public ObservableCollection<string> CanEvents { get; } = new();
    public ObservableCollection<FlashStageRow> Stages { get; } = new();
    public ObservableCollection<CanToolConfigurationField> ConfigurationFields { get; } = new();
    public ObservableCollection<FlashSegmentRow> FirmwareSegments { get; } = new();

    private CanToolConfigurationSnapshot? _config;
    private FirmwareImage? _image;
    private CancellationTokenSource? _flashCancellation;
    private bool _closing;
    private bool _connectionBusy;
    private bool _flashRunning;
    private bool _preflightValid;
    private string _imageOriginalPath = "";

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        _device.ConnectionChanged += OnConnection;
        _device.UsbTrace += OnUsb;
        _device.NativeOperation += OnNative;
        _device.CanTrace += OnCan;
        RefreshDevices();
        Append("STARTUP", "CanTool independent Windows flash test GUI. No R1 production TEST is running.");
    }

    private void Append(string category, string detail)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => Append(category, detail));
            return;
        }
        if (_closing) return;
        Events.Add($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{category}] {detail}");
        if (Events.Count > 2000) Events.RemoveAt(0);
        if (Events.Count != 0) Logs.ScrollIntoView(Events[^1]);
    }

    private void AddStage(CanToolFlashStage item)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => AddStage(item));
            return;
        }
        if (_closing) return;
        Stages.Add(new(item.Utc.ToLocalTime().ToString("HH:mm:ss.fff"),
            item.Stage, item.Outcome, item.Detail,
            $"{item.ConfirmedBytes:N0}/{item.TotalBytes:N0}"));
        if (Stages.Count > 8000) Stages.RemoveAt(0);
        StagesGrid.ScrollIntoView(Stages[^1]);
        ProgressLine.Text = item.Stage + " · " + item.Outcome + " · " +
                            $"{item.ConfirmedBytes:N0}/{item.TotalBytes:N0} B";
        FlashProgress.Value = item.TotalBytes > 0
            ? 100.0 * Math.Clamp((double)item.ConfirmedBytes / item.TotalBytes, 0, 1)
            : 0;
        Append("FLASH " + item.Stage, item.Outcome + ": " + item.Detail);
    }

    private void InvalidatePreflight(string reason)
    {
        _preflightValid = false;
        AuthorizationExpected.Text = "";
        AuthorizationEntered.Text = "";
        StartButton.IsEnabled = false;
        FlashResultLine.Text = "请重新预检：" + reason;
        FlashProgress.Value = 0;
    }

    private void RefreshDevices()
    {
        try
        {
            var previous = (Devices.SelectedItem as WinUsbCandidate)?.DeviceInstanceId;
            var devices = _device.Enumerate();
            Devices.ItemsSource = devices;
            Devices.SelectedItem = devices.FirstOrDefault(x => x.DeviceInstanceId == previous) ??
                (devices.Count == 1 ? devices[0] : null);
            Append("USB_DISCOVERY", $"已发现 {devices.Count} 个目标主控 WinUSB 设备。");
        }
        catch (Exception ex) { Append("USB_DISCOVERY_ERROR", ex.ToString()); }
    }
    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshDevices();

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (_connectionBusy || _flashRunning) return;
        if (Devices.SelectedItem is not WinUsbCandidate chosen)
        {
            StatusLine.Text = "请选择主控板 WinUSB 设备。";
            return;
        }
        _connectionBusy = true;
        ConnectButton.IsEnabled = false;
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
                "AutoTest", "CanToolFlashLab", "Sessions");
            Directory.CreateDirectory(folder);
            var trace = Path.Combine(folder, "usb-can-" +
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff") + "-" +
                Guid.NewGuid().ToString("N") + ".jsonl");
            Append("USB_CONNECT", chosen.DisplayIdentity);
            await _device.ConnectAsync(chosen.DeviceInstanceId, trace);
            TracePathLine.Text = "USB/CAN 原始证据：" + (_device.TraceFile ?? trace);
            StatusLine.Text = $"真实主控已连接；Generation={_device.Generation}；协议版本验证={_device.ProtocolVersionVerified}";
            DisconnectButton.IsEnabled = true;
            InvalidatePreflight("USB 会话已改变");
            Append("USB_CONNECTED", StatusLine.Text);
        }
        catch (Exception ex)
        {
            StatusLine.Text = "USB 连接失败：" + ex.Message;
            Append("USB_CONNECT_ERROR", ex.ToString());
            ConnectButton.IsEnabled = true;
        }
        finally { _connectionBusy = false; }
    }

    private async void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        if (_connectionBusy || _flashRunning)
        {
            Append("USB_DISCONNECT_REJECTED", "烧录期间请先请求取消并等待物理作业结束。");
            return;
        }
        _connectionBusy = true;
        DisconnectButton.IsEnabled = false;
        try
        {
            await _device.DisconnectAsync();
            StatusLine.Text = "主控已断开";
            TracePathLine.Text = "最近证据：" + (_device.TraceFile ?? "无");
            ConnectButton.IsEnabled = true;
            InvalidatePreflight("断开设备");
            Append("USB_DISCONNECTED", "主控 WinUSB 已释放");
        }
        catch (Exception ex)
        {
            StatusLine.Text = "USB 断开失败：" + ex.Message;
            Append("USB_DISCONNECT_ERROR", ex.ToString());
        }
        finally { _connectionBusy = false; }
    }

    private void OpenConfig_Click(object sender, RoutedEventArgs e)
    {
        if (_flashRunning) return;
        var picker = new OpenFileDialog
        {
            Filter = "CanTool 配置 (*.xml)|*.xml|全部文件 (*.*)|*.*",
            Title = "选择实际 CanTool Configuration.xml"
        };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            var config = CanToolConfigurationImporter.LoadFile(picker.FileName);
            _config = config;
            ConfigPath.Text = picker.FileName;
            ConfigurationFields.Clear();
            foreach (var item in config.Fields) ConfigurationFields.Add(item);
            BlockLength.Text = config.Fields
                .FirstOrDefault(x => x.Field == "BlockLength" && x.Source == "XML")?.Value
                ?.Replace("0x", "", StringComparison.OrdinalIgnoreCase) ?? "200";
            ConfigSummary.Text =
                $"APP TX/RX: 0x{config.AppRequestId:X}/0x{config.AppResponseId:X} · " +
                $"Boot TX/RX: 0x{config.BootRequestId:X}/0x{config.BootResponseId:X}\n" +
                $"Flash: 0x{config.FlashStart:X8} .. 0x{config.FlashEndInclusive:X8} (End inclusive) · " +
                $"CAN: {config.BaudRate}（请求值，非主控实测速率）\n" +
                $"XML SHA256: {config.SourceSha256}";
            InvalidatePreflight("配置改变");
            Append("XML_IMPORTED", ConfigSummary.Text.Replace("\n", " | "));
        }
        catch (Exception ex)
        {
            _config = null;
            ConfigSummary.Text = "配置导入失败：" + ex.Message;
            InvalidatePreflight("XML 错误");
            Append("XML_ERROR", ex.ToString());
        }
    }

    private void OpenFirmware_Click(object sender, RoutedEventArgs e)
    {
        if (_flashRunning) return;
        var picker = new OpenFileDialog
        {
            Filter = "固件文件 (*.hex;*.h86;*.s19;*.sx;*.mot;*.srec;*.bin)|*.hex;*.h86;*.s19;*.sx;*.mot;*.srec;*.bin|全部文件 (*.*)|*.*",
            Title = "选择 ECU 固件镜像"
        };
        if (picker.ShowDialog(this) != true) return;
        ImagePath.Text = picker.FileName;
        _imageOriginalPath = picker.FileName;
        _image = null;
        ImageSummary.Text = "等待解析镜像";
        FirmwareSegments.Clear();
        InvalidatePreflight("固件文件改变");
        Append("IMAGE_SELECTED", Path.GetFileName(picker.FileName));
    }

    private void OpenKeyDll_Click(object sender, RoutedEventArgs e)
    {
        if (_flashRunning) return;
        var picker = new OpenFileDialog
        {
            Filter = "原生安全访问库 (*.dll)|*.dll",
            Title = "选择授权 ECU 对应的原生 ZLGKey DLL"
        };
        if (picker.ShowDialog(this) != true) return;
        SecurityDllPath.Text = picker.FileName;
        InvalidatePreflight("安全算法库改变");
        Append("KEY_DLL_SELECTED", Path.GetFileName(picker.FileName) +
            "；不会回退内置演示 Seed/Key 算法。");
    }

    private static int ParseDecimal(string text, string parameter) =>
        int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
            out var value) ? value : throw new FormatException(parameter + " 必须是十进制整数");

    private static uint ParseHex(string text, string parameter)
    {
        var cleaned = text.Trim();
        if (cleaned.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[2..];
        return uint.TryParse(cleaned, NumberStyles.HexNumber,
            CultureInfo.InvariantCulture, out var value) ? value :
            throw new FormatException(parameter + " 必须是十六进制整数");
    }

    private CanToolFlashOptions Options() => new(
        SecurityDllPath.Text.Trim(), AuthorizationEntered.Text.Trim(),
        checked((int)ParseHex(BlockLength.Text, "BlockLength")),
        ParseDecimal(P2Timeout.Text, "P2"),
        ParseDecimal(P2StarTimeout.Text, "P2*"),
        ParseDecimal(EraseTimeout.Text, "Erase"),
        ParseDecimal(FlowControlTimeout.Text, "FlowControl"),
        InsertSignature.IsChecked == true,
        ConfirmPower.IsChecked == true,
        ConfirmBitrate.IsChecked == true,
        AuthorizeErase.IsChecked == true);

    private void ParseFirmware()
    {
        if (string.IsNullOrWhiteSpace(_imageOriginalPath))
            throw new InvalidOperationException("必须先选择固件镜像。");
        var file = new FileInfo(_imageOriginalPath);
        if (!file.Exists || file.Length is <= 0 or > FirmwareImageParser.MaxImageBytes)
            throw new InvalidDataException("固件文件不存在、为空或超出 16 MiB 限制。");
        byte[] raw = File.ReadAllBytes(file.FullName);
        var suffix = file.Extension.ToLowerInvariant();
        FirmwareImage image = suffix switch
        {
            ".hex" or ".h86" => FirmwareImageParser.ParseHex(
                new UTF8Encoding(false, true).GetString(raw)),
            ".s19" or ".sx" or ".mot" or ".srec" =>
                FirmwareImageParser.ParseS19(new UTF8Encoding(false, true).GetString(raw)),
            ".bin" => FirmwareImageParser.ParseBin(raw, ParseHex(BinAddress.Text, "BIN 地址")),
            _ => throw new InvalidDataException("只能选择 HEX / S19 / BIN 固件。")
        };
        _image = image;
        FirmwareSegments.Clear();
        foreach (var seg in image.Segments)
            FirmwareSegments.Add(new("0x" + seg.Address.ToString("X8"),
                "0x" + seg.EndExclusive.ToString("X8"), seg.Data.Length));
        ImageSummary.Text =
            $"{image.Format} · 原始字节 {image.ByteCount:N0} · 段 {image.Segments.Length}\n" +
            $"SHA256: {image.SourceFingerprint.Sha256}";
        Append("IMAGE_PARSED", ImageSummary.Text.Replace("\n", " | "));
    }

    private void Preflight_Click(object sender, RoutedEventArgs e)
    {
        if (_flashRunning) return;
        InvalidatePreflight("正在校验");
        try
        {
            if (_config is null) throw new InvalidOperationException("缺少 CanTool XML。");
            ParseFirmware();
            if (_image is null || !_config.ImageFits(_image))
                throw new InvalidDataException("固件段超出 XML Flash 地址范围。");
            var opts = Options();
            var blocks = CanToolFlashEngine.BuildCanToolAlignedBlocks(
                _config, _image, opts.AlignedBlockLength, opts.InsertCanToolEndSignature);
            foreach (var block in blocks)
                if ((ulong)block.Address < _config.FlashStart ||
                    block.EndExclusive > _config.EndExclusive)
                    throw new InvalidDataException("对齐后内存段超出目标地址范围。");
            if (_device.State != UsbSessionState.Connected ||
                !_device.ProtocolVersionVerified || _device.Generation < 1)
                throw new InvalidOperationException("主控未通过当前真实 USB 连接与固件协议检查。");
            if (_device.CanDropped != 0 || _device.CanMalformed != 0 ||
                _device.TraceDropped != 0 || _device.TraceError is not null)
                throw new IOException("USB/CAN 数据/日志存在丢失，不允许进入烧录。");
            _preflightValid = true;
            FlashResultLine.Text =
                $"镜像已解析：对齐 {blocks.Length} 块；预计写入 {blocks.Sum(x => (long)x.Data.Length):N0} 字节。\n" +
                $"擦除范围：0x{_config.FlashStart:X8} .. 0x{_config.FlashEndInclusive:X8}（包含结束）。\n" +
                "请确认实际 ECU、CAN1 速率、Security DLL，并生成人工授权短语。";
            ProgressLine.Text = "PREPARED · 未发送任何擦除或下载报文";
            Append("PREFLIGHT", FlashResultLine.Text.Replace("\n", " | "));
        }
        catch (Exception ex)
        {
            _preflightValid = false;
            FlashResultLine.Text = "预检失败：" + ex.Message;
            Append("PREFLIGHT_ERROR", ex.ToString());
        }
    }

    private void PrepareAuthorization_Click(object sender, RoutedEventArgs e)
    {
        if (_flashRunning) return;
        try
        {
            if (!_preflightValid || _config is null || _image is null)
                throw new InvalidOperationException("必须先导入并预检当前实际 XML 与固件。");
            var o = Options();
            o.Validate();
            if (_device.State != UsbSessionState.Connected ||
                !_device.ProtocolVersionVerified || _device.Generation < 1)
                throw new InvalidOperationException("必须连接且核验当前 WinUSB 主控。");
            // Ensure chosen DLL actually exports the native symbol BEFORE asking
            // an operator to authorize an ECU erase. We do not compute any key yet.
            using (var keyProvider = new NativeCanToolKeyProvider(o.SecurityDllPath))
                Append("KEY_PROVIDER", "已加载原生 ZLGKey; DLL SHA256=" +
                    keyProvider.Sha256 + "; 目标 ECU 兼容性仍需实测。");
            var expected = HardwareBenchController.PhysicalCanToolConfirmation(
                _config.SourceSha256, _image.SourceFingerprint.Sha256,
                _config.AppRequestId, _config.BootRequestId, _device.Generation);
            AuthorizationExpected.Text = expected;
            AuthorizationEntered.Text = "";
            StartButton.IsEnabled = true;
            FlashResultLine.Text = "准备完成：请核对擦除地址范围和当前 ECU，并输入完全相同的授权短语。";
            Append("AUTHORIZATION", "已生成绑定 XML/固件 SHA/CAN ID/USB 代次的授权短语；尚未发 CAN 写帧。");
        }
        catch (Exception ex)
        {
            StartButton.IsEnabled = false;
            Append("AUTHORIZATION_ERROR", ex.ToString());
            FlashResultLine.Text = "无法获得物理烧录授权：" + ex.Message;
        }
    }

    private async void StartFlash_Click(object sender, RoutedEventArgs e)
    {
        if (_flashRunning || !_preflightValid || _config is null || _image is null) return;
        if (!string.Equals(AuthorizationExpected.Text, AuthorizationEntered.Text.Trim(),
            StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(AuthorizationExpected.Text))
        {
            FlashResultLine.Text = "输入的授权短语与本次冻结作业不一致。";
            return;
        }
        if (_device.State != UsbSessionState.Connected || !_device.ProtocolVersionVerified)
        {
            FlashResultLine.Text = "主控会话已经失效；需要重新预检。";
            return;
        }
        var confirm =
            $"即将对实际 ECU 发送编程会话、安全解锁和擦除/下载命令。\n\n" +
            $"APP TX=0x{_config.AppRequestId:X}, Boot TX=0x{_config.BootRequestId:X}\n" +
            $"擦除：0x{_config.FlashStart:X8} .. 0x{_config.FlashEndInclusive:X8}\n" +
            $"固件 SHA256：{_image.SourceFingerprint.Sha256}\n" +
            $"XML SHA256：{_config.SourceSha256}\n\n" +
            "该操作会改变 ECU Flash，途中取消/断连可能需要重新恢复 ECU。确定执行？";
        if (MessageBox.Show(this, confirm, "真实 CAN1 ECU 烧录最终确认",
            MessageBoxButton.YesNo, MessageBoxImage.Warning,
            MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        _flashRunning = true;
        _flashCancellation = new CancellationTokenSource();
        StartButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        ConnectButton.IsEnabled = false;
        DisconnectButton.IsEnabled = false;
        Stages.Clear();
        FlashProgress.Value = 0;
        try
        {
            var source = _config;
            var image = _image;
            var options = Options();
            var progress = new Progress<CanToolFlashStage>(AddStage);
            var engine = new CanToolFlashEngine(_device, progress);
            Append("FLASH_BEGIN", $"real CAN1; XML={source.SourceSha256}; IMAGE={image.SourceFingerprint.Sha256}");
            var outcome = await engine.RunAsync(source, image, options, _flashCancellation.Token);
            FlashResultLine.Text =
                $"{outcome.Status}\n已确认 {outcome.ConfirmedBytes:N0}/{outcome.TotalBytes:N0} 字节\n" +
                $"{outcome.LastStage}: {outcome.Detail}";
            Append("FLASH_END", FlashResultLine.Text.Replace("\n", " | "));
        }
        catch (Exception ex)
        {
            FlashResultLine.Text = "烧录作业异常中止，ECU 状态可能不确定：" + ex.Message;
            Append("FLASH_UNCERTAIN", ex.ToString());
        }
        finally
        {
            _flashRunning = false;
            _flashCancellation.Dispose();
            _flashCancellation = null;
            CancelButton.IsEnabled = false;
            ConnectButton.IsEnabled = _device.State != UsbSessionState.Connected;
            DisconnectButton.IsEnabled = _device.State == UsbSessionState.Connected;
            InvalidatePreflight("本次烧录作业结束，重新执行前必须重新核对真实目标与状态");
        }
    }

    private void CancelFlash_Click(object sender, RoutedEventArgs e)
    {
        if (!_flashRunning) return;
        _flashCancellation?.Cancel();
        CancelButton.IsEnabled = false;
        Append("FLASH_CANCEL_REQUESTED",
            "已请求取消；可能已发生 ECU 擦除/写入，结果不得当作未操作。");
    }

    private void OnConnection(UsbSessionState state)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_closing) return;
            Append("USB_STATE", $"state={state}, generation={_device.Generation}");
            if (state is UsbSessionState.Disconnected or UsbSessionState.Faulted)
            {
                if (_flashRunning)
                {
                    _flashCancellation?.Cancel();
                    FlashResultLine.Text = "USB 会话失效；烧录结果不确定，必须检查 ECU。";
                }
                else
                {
                    ConnectButton.IsEnabled = true;
                    DisconnectButton.IsEnabled = false;
                }
                InvalidatePreflight("设备状态变化");
            }
        });
    }

    private void OnUsb(UsbTraceRecord trace) =>
        Append("USB " + trace.Kind,
            $"generation={trace.Generation}, sequence={trace.Sequence}, " +
            trace.Detail + " RAW=" + trace.RawHex);

    private void OnNative(WinUsbOperationEvent item) =>
        Append("WINUSB " + item.Outcome,
            $"{item.Stage}: {item.Detail}; NativeErrorCode={item.NativeErrorCode}");

    private void OnCan(HardwareCanTrace trace)
    {
        if (trace.PhysicalPort != 1) return;
        var line = $"{trace.HostUtc:HH:mm:ss.fff} [CAN1] " +
            $"gen={trace.SessionGeneration}; valid={trace.Valid}; " +
            $"{trace.Detail}; rawUSB={trace.RawUsbPayloadHex}";
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => AppendCan(line));
            return;
        }
        AppendCan(line);
    }

    private void AppendCan(string line)
    {
        if (_closing) return;
        CanEvents.Add(line);
        if (CanEvents.Count > 4000) CanEvents.RemoveAt(0);
        CanLogs.ScrollIntoView(CanEvents[^1]);
    }

    private void CopyLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine,
                Events.Concat(CanEvents)));
        }
        catch (Exception ex) { Append("COPY_ERROR", ex.Message); }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new SaveFileDialog
            {
                Filter = "CanTool 实测日志文本 (*.txt)|*.txt",
                FileName = "CanTool-RealCAN1-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt"
            };
            if (picker.ShowDialog(this) != true) return;
            var report = new StringBuilder()
                .AppendLine("AutoTest independent CanTool ECU Flash Lab / REAL CAN1")
                .AppendLine("UTC=" + DateTimeOffset.UtcNow.ToString("O"))
                .AppendLine("USB Session Generation=" + _device.Generation)
                .AppendLine("USB State=" + _device.State)
                .AppendLine("Controller version verified=" + _device.ProtocolVersionVerified)
                .AppendLine("XML SHA256=" + (_config?.SourceSha256 ?? "NOT_LOADED"))
                .AppendLine("Firmware SHA256=" + (_image?.SourceFingerprint.Sha256 ?? "NOT_LOADED"))
                .AppendLine("ECU APP/Boot=" + ConfigSummary.Text)
                .AppendLine("USB Trace=" + (_device.TraceFile ?? "NOT_AVAILABLE"))
                .AppendLine("CAN Published=" + _device.CanPublished)
                .AppendLine("CAN Dropped=" + _device.CanDropped)
                .AppendLine("CAN Malformed=" + _device.CanMalformed)
                .AppendLine("Hub Dropped=" + _device.HubDropped)
                .AppendLine("Trace Dropped=" + _device.TraceDropped)
                .AppendLine("Trace Error=" + (_device.TraceError ?? "NONE"))
                .AppendLine("OUTCOME=" + FlashResultLine.Text)
                .AppendLine()
                .AppendLine("STAGES");
            foreach (var entry in Stages)
                report.AppendLine($"{entry.When} {entry.Stage} {entry.Outcome} {entry.Confirmed} {entry.Detail}");
            report.AppendLine().AppendLine("EVENTS");
            foreach (var entry in Events) report.AppendLine(entry);
            report.AppendLine().AppendLine("CAN1 TRACE");
            foreach (var entry in CanEvents) report.AppendLine(entry);
            File.WriteAllText(picker.FileName, report.ToString(), new UTF8Encoding(false));
            Append("EXPORT", picker.FileName);
        }
        catch (Exception ex) { Append("EXPORT_ERROR", ex.ToString()); }
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_closing) return;
        if (_flashRunning)
        {
            e.Cancel = true;
            _flashCancellation?.Cancel();
            Append("CLOSE_DENIED", "已请求取消真实烧录；等待作业明确退出后再关闭。");
            return;
        }
        e.Cancel = true;
        _closing = true;
        IsEnabled = false;
        try
        {
            await _device.DisposeAsync();
            _device.ConnectionChanged -= OnConnection;
            _device.UsbTrace -= OnUsb;
            _device.NativeOperation -= OnNative;
            _device.CanTrace -= OnCan;
            Close();
        }
        catch (Exception ex)
        {
            _closing = false;
            IsEnabled = true;
            MessageBox.Show(this, ex.Message, "设备会话关闭失败");
        }
    }
}
