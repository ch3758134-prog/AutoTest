using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AutoTest.Devices.Diagnostics;
using AutoTest.Devices.Usb;
using AutoTest.Specialists.Flash;
using Prism.Commands;
using Prism.Mvvm;

namespace AutoTest.Desktop.Tools.Flash;

public sealed record R1FlashSegmentRow(string Start, string EndExclusive, int Bytes);

/// <summary>
/// Formal R1 Flash setup and NON-DESTRUCTIVE evidence validation.
/// No simulated programming transport, fabricated ECU identity, dry-run job
/// or CAN/UDS write operation exists in this view model.
/// </summary>
public sealed class FlashToolViewModel : BindableBase
{
    private FirmwareImage? _image;
    private CanToolConfigurationSnapshot? _canTool;
    private bool _busy;
    private string _imagePath = "", _binAddress = "0",
        _target = "", _expectedIdentity = "", _allowedStart = "", _allowedEnd = "",
        _configStatus = "尚未导入 CanTool Configuration.xml",
        _physicalPreflight = "尚未做真实 ECU 条件检查",
        _status = "WAITING_FOR_REAL_CONFIG",
        _failure = "", _format = "—", _hash = "—", _bytes = "0", _preflight = "NOT_CHECKED";

    public FlashToolViewModel()
    {
        LoadCommand = new DelegateCommand(() => _ = LoadFromPathAsync(), () => !IsBusy);
        PreflightCommand = new DelegateCommand(ValidateImageAndConfiguration, () => !IsBusy);
    }

    public ObservableCollection<R1FlashSegmentRow> Segments { get; } = new();
    public ObservableCollection<CanToolConfigurationField> ConfigurationFields { get; } = new();
    public ObservableCollection<FlashPreflightItem> PhysicalChecks { get; } = new();
    public DelegateCommand LoadCommand { get; }
    public DelegateCommand PreflightCommand { get; }

    // Preflight VM cannot authorize programming. The P09 physical job owns a separate
    // session-scoped, explicit CanTool permission; this flag remains false by design.
    public bool PhysicalProgrammingEnabled => false;
    public CanToolConfigurationSnapshot? CurrentConfiguration => _canTool;
    public FirmwareImage? CurrentImage => _image;
    public string SourceLabel => "配置与固件检查不会自动烧录；实机写入必须经过独立授权和逐项确认。";
    public bool IsBusy
    {
        get => _busy;
        private set
        {
            if (!SetProperty(ref _busy, value)) return;
            LoadCommand.RaiseCanExecuteChanged();
            PreflightCommand.RaiseCanExecuteChanged();
        }
    }
    public string ImagePath { get => _imagePath; set => SetProperty(ref _imagePath, value); }
    public string BinAddress { get => _binAddress; set => SetProperty(ref _binAddress, value); }
    public string Target { get => _target; set => SetProperty(ref _target, value); }
    public string ExpectedIdentity { get => _expectedIdentity; set => SetProperty(ref _expectedIdentity, value); }
    public string AllowedStart { get => _allowedStart; private set => SetProperty(ref _allowedStart, value); }
    public string AllowedEnd { get => _allowedEnd; private set => SetProperty(ref _allowedEnd, value); }
    public string ConfigurationStatus { get => _configStatus; private set => SetProperty(ref _configStatus, value); }
    public string PhysicalPreflight { get => _physicalPreflight; private set => SetProperty(ref _physicalPreflight, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string Failure { get => _failure; private set => SetProperty(ref _failure, value); }
    public string Format { get => _format; private set => SetProperty(ref _format, value); }
    public string Hash { get => _hash; private set => SetProperty(ref _hash, value); }
    public string Bytes { get => _bytes; private set => SetProperty(ref _bytes, value); }
    public string Preflight { get => _preflight; private set => SetProperty(ref _preflight, value); }

    private static uint ParseHex(string text) => uint.Parse(
        text.Trim().Replace("0x", "", StringComparison.OrdinalIgnoreCase),
        NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    public void ImportCanToolConfiguration(string path)
    {
        if (IsBusy) throw new InvalidOperationException("Configuration cannot change while an image is loading.");
        var profile = CanToolConfigurationImporter.LoadFile(path);
        _canTool = profile;
        ConfigurationFields.Clear();
        foreach (var item in profile.Fields) ConfigurationFields.Add(item);
        AllowedStart = profile.FlashStart.ToString("X8", CultureInfo.InvariantCulture);
        AllowedEnd = profile.EndExclusive.ToString("X", CultureInfo.InvariantCulture);
        ConfigurationStatus = "配置已解析：" + profile.SourceName +
            " SHA256=" + profile.SourceSha256 +
            "；APP TX/RX 0x" + profile.AppRequestId.ToString("X") + "/0x" +
            profile.AppResponseId.ToString("X") +
            "；Boot TX/RX 0x" + profile.BootRequestId.ToString("X") + "/0x" +
            profile.BootResponseId.ToString("X") +
            "。XML 仅证明配置文本，不证明 ECU 兼容性。";
        Preflight = "CONFIGURATION_CHANGED";
        Status = "PROFILE_PARSED_NOT_VERIFIED";
    }

    public async Task LoadFromPathAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var path = ImagePath.Trim();
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("请选择固件文件。");
            var file = new FileInfo(path);
            if (!file.Exists || file.Length is <= 0 or > FirmwareImageParser.MaxImageBytes)
                throw new InvalidDataException("固件不存在、为空或大于 16 MiB。");
            var data = await File.ReadAllBytesAsync(path);
            LoadBytes(Path.GetFileName(path), data, ParseHex(BinAddress));
        }
        catch (Exception ex)
        {
            Status = "IMAGE_ERROR";
            Failure = ex.GetType().Name + ": " + ex.Message;
        }
        finally { IsBusy = false; }
    }

    public void LoadBytes(string name, byte[] bytes, uint binAddress)
    {
        if (bytes is null || bytes.Length is <= 0 or > FirmwareImageParser.MaxImageBytes)
            throw new FirmwareFormatException("Firmware image empty or exceeds size limit.");
        var image = Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".bin" => FirmwareImageParser.ParseBin(bytes, binAddress),
            ".hex" or ".ihex" or ".h86" => FirmwareImageParser.ParseHex(new UTF8Encoding(false, true).GetString(bytes)),
            ".s19" or ".s28" or ".s37" or ".srec" or ".mot" or ".sx" =>
                FirmwareImageParser.ParseS19(new UTF8Encoding(false, true).GetString(bytes)),
            _ => throw new FirmwareFormatException("只接受 BIN / Intel HEX / S-record。")
        };
        // No mutation until all file parse and segment overlap checks pass.
        _image = image;
        Segments.Clear();
        foreach (var segment in image.Segments)
            Segments.Add(new R1FlashSegmentRow(
                "0x" + segment.Address.ToString("X8"),
                "0x" + segment.EndExclusive.ToString("X8"),
                segment.Data.Length));
        Format = image.Format;
        Hash = image.SourceFingerprint.Sha256;
        Bytes = image.ByteCount.ToString(CultureInfo.InvariantCulture);
        Failure = "";
        Status = "IMAGE_PARSED_NOT_FLASHED";
        Preflight = "NOT_CHECKED";
    }

    public Task PreflightAsync()
    {
        ValidateImageAndConfiguration();
        return Task.CompletedTask;
    }

    private void ValidateImageAndConfiguration()
    {
        Failure = "";
        if (_image is null)
        {
            Preflight = "BLOCKED";
            Failure = "IMAGE_NOT_SELECTED";
        }
        else if (_canTool is null)
        {
            Preflight = "BLOCKED";
            Failure = "CANTOOL_CONFIG_REQUIRED";
        }
        else
        {
            var evidence = new FlashFieldEvidence(false, false, 0, false, false, false,
                false, false, false, false, false);
            var result = FlashFieldPreflightEvaluator.Evaluate(_canTool, _image, evidence);
            PhysicalChecks.Clear();
            foreach (var item in result.Items) PhysicalChecks.Add(item);
            Preflight = "PHYSICAL_WRITE_BLOCKED";
            Failure = string.Join("; ", result.Items.Where(x => !x.Passed).Select(x => x.Category));
        }
        Status = "已完成只读检查。要向 ECU 实际烧录，请在实机烧录区完成授权确认。";
    }

    public void EvaluatePhysicalPreflight(HardwareBenchController? controller)
    {
        if (IsBusy) throw new InvalidOperationException("当前正在读取固件。");
        var verified = controller is
            { State: UsbSessionState.Connected, ProtocolVersionVerified: true, Generation: > 0 };
        var evidence = new FlashFieldEvidence(
            UsbConnected: verified, ControllerVersionVerified: controller?.ProtocolVersionVerified == true,
            UsbGeneration: controller?.Generation ?? 0,
            LiveUdsReadAccepted: false, LiveUdsEndpointMatches: false,
            CanBitrateVerified: false, EcuIdentityMatched: false,
            MeasuredPowerInRange: false, SecurityProviderValidated: false,
            OemProgrammingContractValidated: false, RecoveryProcedureValidated: false);
        var result = FlashFieldPreflightEvaluator.Evaluate(_canTool, _image, evidence);
        PhysicalChecks.Clear();
        foreach (var item in result.Items) PhysicalChecks.Add(item);
        PhysicalPreflight = $"真实预检：{result.Items.Count(x => x.Passed)} 项通过、" +
            $"{result.Items.Count(x => !x.Passed)} 项待完成；物理写入始终锁定。";
        Preflight = "PHYSICAL_WRITE_BLOCKED";
        Status = "PHYSICAL_FLASH_REQUIRES_EXPLICIT_P09_JOB";
    }
}
