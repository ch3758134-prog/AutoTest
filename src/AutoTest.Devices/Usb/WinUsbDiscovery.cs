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

public sealed record WinUsbCandidate(string DevicePath, string DeviceInstanceId, string? DriverService = null)
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
                    {
                        // Diagnostic evidence only: an absent/unreadable service is not
                        // grounds to guess the installed driver or bind a different one.
                        byte[] serviceBytes = new byte[512];
                        string? service = null;
                        if (WinUsbNative.SetupDiGetDeviceRegistryPropertyW(set, ref deviceData,
                                WinUsbNative.SpdrpService, out _, serviceBytes, (uint)serviceBytes.Length, out uint serviceLength)
                            && serviceLength >= 2 && serviceLength <= serviceBytes.Length)
                            service = Encoding.Unicode.GetString(serviceBytes, 0, (int)serviceLength).TrimEnd('\0');
                        found.Add(new(path, instance.ToString(), service));
                    }
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


/// <summary>Read-only PnP check, including the controller when an old driver
/// exposes VID/PID but not the current firmware's WinUSB interface GUID.</summary>
public sealed record ControllerPnPInstance(string InstanceId,string? Service);
public sealed record ControllerUsbDetection(string Summary,string Detail,int Devices,int Interfaces);

public static class ControllerUsbDetectionService
{
    private const uint AllClasses=0x04, Present=0x02;
    private static class PnpNative
    {
        [DllImport("setupapi.dll", EntryPoint="SetupDiGetClassDevsW", CharSet=CharSet.Unicode, SetLastError=true, ExactSpelling=true)]
        internal static extern IntPtr GetAll(IntPtr classGuid,string? enumerator,IntPtr hwnd,uint flags);
        [DllImport("setupapi.dll", EntryPoint="SetupDiEnumDeviceInfo", SetLastError=true, ExactSpelling=true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumInfo(IntPtr set,uint index,ref WinUsbNative.SP_DEVINFO_DATA data);
    }

    public static ControllerUsbDetection Explain(IReadOnlyList<WinUsbCandidate> exact,
        IReadOnlyList<ControllerPnPInstance> windowsDevices)
    {
        var present=windowsDevices
            .Where(x=>MainControllerUsbIdentity.IsControllerInstance(x.InstanceId)).ToArray();
        if(exact.Count>0)return new($"找到 {exact.Count} 个 WinUSB 主控接口",
            $"VID/PID=1183/5850, GUID={MainControllerUsbIdentity.InterfaceGuid}；可选择主控连接。",
            present.Length,exact.Count);
        if(present.Length==0)return new("Windows 未发现 VID_1183/PID_5850",
            "当前 PnP 可见设备中没有主控 VID/PID，也没有专用 WinUSB 接口。"+
            "先核对主控供电、USB 数据线以及 Windows 设备管理器。",0,0);
        string devices=string.Join(" | ",present.Select(x=>
            $"{x.InstanceId} [驱动服务：{x.Service??"UNKNOWN"}]"));
        bool bound=present.Any(x=>string.Equals(x.Service,"WinUSB",
            StringComparison.OrdinalIgnoreCase));
        return new("发现主控 VID/PID，但未注册 WinUSB 接口 GUID",
            $"PnP 主控数量={present.Length}；匹配 GUID={MainControllerUsbIdentity.InterfaceGuid}，数量=0；"+
            $"设备：{devices}。"+
            (bound
                ?"驱动服务显示 WinUSB，检查实际主控固件的 WCID 扩展属性、接口 GUID 注册及 Windows 安装缓存。"
                :"检查是否沿用旧版 libusb0/libusbK 驱动或未安装 WinUSB；先核对驱动和固件，禁止自动改绑。"),
            present.Length,0);
    }

    public static ControllerUsbDetection Probe()
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Windows PnP only");
        return Explain(new WinUsbDiscovery().ListCandidates(),FindAllPresentControllerInstances());
    }

    private static IReadOnlyList<ControllerPnPInstance> FindAllPresentControllerInstances()
    {
        IntPtr set=PnpNative.GetAll(IntPtr.Zero,null,IntPtr.Zero,AllClasses|Present);
        if(set==new IntPtr(-1))throw new Win32Exception(Marshal.GetLastWin32Error(),
            "Unable to enumerate Windows PnP devices");
        try
        {
            var result=new List<ControllerPnPInstance>();
            for(uint index=0;;index++)
            {
                var entry=new WinUsbNative.SP_DEVINFO_DATA
                {cbSize=(uint)Marshal.SizeOf<WinUsbNative.SP_DEVINFO_DATA>()};
                if(!PnpNative.EnumInfo(set,index,ref entry))
                {
                    int err=Marshal.GetLastWin32Error();
                    if(err==WinUsbNative.ErrorNoMoreItems)break;
                    throw new Win32Exception(err,"SetupDiEnumDeviceInfo");
                }
                var id=new StringBuilder(512);
                if(!WinUsbNative.SetupDiGetDeviceInstanceIdW(set,ref entry,id,id.Capacity,out _))
                    continue;
                string value=id.ToString();
                if(!MainControllerUsbIdentity.IsControllerInstance(value))continue;
                byte[] bytes=new byte[512];
                string? service=null;
                if(WinUsbNative.SetupDiGetDeviceRegistryPropertyW(set,ref entry,
                    WinUsbNative.SpdrpService,out _,bytes,(uint)bytes.Length,out uint len)
                    && len>=2 && len<=bytes.Length)
                    service=Encoding.Unicode.GetString(bytes,0,(int)len).TrimEnd('\0');
                result.Add(new(value,service));
            }
            return result;
        }
        finally{WinUsbNative.SetupDiDestroyDeviceInfoList(set);}
    }
}
