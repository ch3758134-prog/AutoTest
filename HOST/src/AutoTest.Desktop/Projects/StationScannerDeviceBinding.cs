using System.IO;
using System.Security;
using System.Text.Json;
using Microsoft.Win32;
using AutoTest.Specialists.Scan;

namespace AutoTest.Desktop.Projects;

/// <summary>
/// Windows USB serial scanner installation identity, not an ECU identity.
/// COM indices change; the selected USB PnP instance is explicitly bound once
/// and must be found uniquely before an automatic connection is attempted.
/// A composite USB modem and a scanner with no stable serial are not guessed.
/// </summary>
public static class StationScannerDeviceBinding
{
    public sealed record Device(string PnpInstanceId, string PortName, string Description);
    public sealed record Saved(int SchemaVersion, string PnpInstanceId);

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AutoTest", "scanner-device.json");

    public static IReadOnlyList<Device> DetectUsbSerialDevices()
    {
        var available = SerialPortBarcodeLineSource.GetAvailablePortNames()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var found = new List<Device>();
        // The Windows USB enumeration tree is read-only. A non-USB virtual
        // port is intentionally not claimed as the fixed scanner.
        using var usb = Registry.LocalMachine.OpenSubKey(
            @"SYSTEM\CurrentControlSet\Enum\USB", writable: false);
        if (usb is null) return found;
        foreach (var hardware in usb.GetSubKeyNames())
        {
            if (!hardware.Contains("VID_", StringComparison.OrdinalIgnoreCase) ||
                !hardware.Contains("PID_", StringComparison.OrdinalIgnoreCase))
                continue;
            using var hardwareKey = usb.OpenSubKey(hardware, writable: false);
            if (hardwareKey is null) continue;
            foreach (var instance in hardwareKey.GetSubKeyNames())
            {
                using var instanceKey = hardwareKey.OpenSubKey(instance, writable: false);
                if (instanceKey is null) continue;
                using var parameters = instanceKey.OpenSubKey("Device Parameters", writable: false);
                var port = parameters?.GetValue("PortName") as string;
                if (string.IsNullOrWhiteSpace(port) || !available.Contains(port)) continue;
                var pnp = @"USB\" + hardware + @"\" + instance;
                var description = (instanceKey.GetValue("FriendlyName") as string)
                    ?? (instanceKey.GetValue("DeviceDesc") as string)
                    ?? hardware;
                found.Add(new Device(pnp, port, description));
            }
        }
        return found;
    }

    public static Device? Match(Saved binding)
    {
        if (binding.SchemaVersion != 1 || string.IsNullOrWhiteSpace(binding.PnpInstanceId))
            throw new InvalidDataException("扫码枪设备绑定记录不合法。");
        var matches = DetectUsbSerialDevices().Where(d =>
            string.Equals(d.PnpInstanceId, binding.PnpInstanceId,
                StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length > 1)
            throw new InvalidDataException("固定扫码枪出现多个设备实例，拒绝任意选 COM。");
        return matches.SingleOrDefault();
    }

    public static Saved? Load()
    {
        if (!File.Exists(FilePath)) return null;
        var data = File.ReadAllBytes(FilePath);
        if (data.Length is <= 0 or > 8192)
            throw new InvalidDataException("扫码枪设备绑定文件大小无效。");
        var bound = JsonSerializer.Deserialize<Saved>(data)
            ?? throw new InvalidDataException("扫码枪设备绑定文件为空。");
        if (bound.SchemaVersion != 1 || string.IsNullOrWhiteSpace(bound.PnpInstanceId))
            throw new InvalidDataException("扫码枪设备绑定数据无效。");
        return bound;
    }

    public static Saved Bind(string selectedPort)
    {
        var matches = DetectUsbSerialDevices()
            .Where(x => string.Equals(x.PortName, selectedPort, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length != 1)
            throw new InvalidDataException(
                "无法将选中的 COM 唯一关联到 USB 设备实例。" +
                "请检查设备驱动/串口，不能仅凭 COM 序号冒充绑定成功。");
        var bound = new Saved(1, matches[0].PnpInstanceId);
        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);
        var temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(bound));
            File.Move(temp, FilePath, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        return bound;
    }
}
