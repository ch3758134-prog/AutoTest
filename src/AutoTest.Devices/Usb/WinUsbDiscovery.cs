using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace AutoTest.Devices.Usb;

/// <summary>Only the firmware's private WinUSB interface GUID and USB VID/PID are eligible.
/// No COM-port or HID probing: keyboard-wedge barcode readers remain untouched.</summary>
public static class MainControllerUsbIdentity
{
    public const ushort VendorId = 0x1183;
    public const ushort ProductId = 0x5850;
    public static readonly Guid InterfaceGuid = new("4da6d065-1e3e-47e6-8cc3-c6977351defb");
    public const byte BulkIn = 0x82, BulkOut = 0x03;
    public const string FirmwareVersion = "EPB_TEST_CTRL v0.3";

    public static bool IsControllerInstance(string? instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId)) return false;
        // Device instance ID comes from SetupAPI, not a user supplied USB path or product name.
        const string prefix = "USB\\VID_1183&PID_5850";
        if (!instanceId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        return instanceId.Length == prefix.Length || instanceId[prefix.Length] is '\\' or '&';
    }
}

public sealed record WinUsbCandidate(string DevicePath, string DeviceInstanceId)
{
    public string DisplayIdentity => $"VID_1183 PID_5850 · {DeviceInstanceId}";
}

public interface IWinUsbDiscovery
{
    IReadOnlyList<WinUsbCandidate> ListCandidates();
}

/// <summary>Enumerates only present interfaces registered under the confirmed WCID GUID.</summary>
public sealed class WinUsbDiscovery : IWinUsbDiscovery
{
    public IReadOnlyList<WinUsbCandidate> ListCandidates()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("WinUSB needs Windows.");
        Guid guid = MainControllerUsbIdentity.InterfaceGuid;
        IntPtr set = WinUsbNative.SetupDiGetClassDevsW(ref guid, null, IntPtr.Zero,
            WinUsbNative.DigcfPresent | WinUsbNative.DigcfDeviceInterface);
        if (set == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error(), "USB enumeration failed.");
        try
        {
            var found = new List<WinUsbCandidate>();
            for (uint index = 0; ; index++)
            {
                var interfaceData = new WinUsbNative.SP_DEVICE_INTERFACE_DATA { cbSize = (uint)Marshal.SizeOf<WinUsbNative.SP_DEVICE_INTERFACE_DATA>() };
                if (!WinUsbNative.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, index, ref interfaceData))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error == WinUsbNative.ErrorNoMoreItems) break;
                    throw new Win32Exception(error, "USB interface enumeration failed.");
                }
                _ = WinUsbNative.SetupDiGetDeviceInterfaceDetailW(set, ref interfaceData, IntPtr.Zero, 0, out uint required, IntPtr.Zero);
                if (required < 8 || required > 65536)
                    throw new IOException("Invalid Windows USB device path size.");
                IntPtr detail = Marshal.AllocHGlobal((int)required);
                try
                {
                    // SP_DEVICE_INTERFACE_DETAIL_DATA_W.cbSize is 8 (x64), 6 (x86).
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    var deviceData = new WinUsbNative.SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<WinUsbNative.SP_DEVINFO_DATA>() };
                    if (!WinUsbNative.SetupDiGetDeviceInterfaceDetailW(set, ref interfaceData, detail, required,
                            out _, ref deviceData))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "USB interface detail failed.");
                    // DevicePath starts immediately after DWORD cbSize, at byte offset 4 on both architectures.
                    string path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4)) ?? "";
                    var instance = new StringBuilder(512);
                    if (!WinUsbNative.SetupDiGetDeviceInstanceIdW(set, ref deviceData, instance,
                            instance.Capacity, out _))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "USB instance identity failed.");
                    if (!string.IsNullOrWhiteSpace(path) && MainControllerUsbIdentity.IsControllerInstance(instance.ToString()))
                        found.Add(new(path, instance.ToString()));
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
            return found.OrderBy(x => x.DeviceInstanceId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.DevicePath, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        finally { WinUsbNative.SetupDiDestroyDeviceInfoList(set); }
    }
}

/// <summary>Require an exact instance selection when more than one controller is present.
/// Re-enumeration uses the Windows device instance ID to defend against accidental port drift.</summary>
public static class WinUsbSelection
{
    public static WinUsbCandidate Select(IReadOnlyList<WinUsbCandidate> candidates, string instanceId)
    {
        if (!MainControllerUsbIdentity.IsControllerInstance(instanceId))
            throw new UnauthorizedAccessException("Rejected: not the configured controller VID/PID.");
        var matching = candidates.Where(x => MainControllerUsbIdentity.IsControllerInstance(x.DeviceInstanceId)
            && string.Equals(x.DeviceInstanceId, instanceId, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matching.Length != 1)
            throw new IOException($"Controller selection must resolve to exactly one present interface (found {matching.Length}).");
        return matching[0];
    }
}
