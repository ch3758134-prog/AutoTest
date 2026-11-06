using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace AutoTest.Devices.Usb;

/// <summary>Windows P/Invoke restricted to this one USB transport; no native dependency on libusb.</summary>
internal static class WinUsbNative
{
    internal const uint DigcfPresent = 0x2, DigcfDeviceInterface = 0x10;
    internal const int ErrorNoMoreItems = 259;
    internal const uint GenericRead = 0x80000000, GenericWrite = 0x40000000;
    internal const uint FileShareRead = 1, FileShareWrite = 2, OpenExisting = 3, FileAttributeNormal = 0x80;
    internal const uint PipeTransferTimeout = 0x03;
    internal const int PipeTypeBulk = 2;

    [StructLayout(LayoutKind.Sequential)]
    internal struct SP_DEVICE_INTERFACE_DATA
    {
        public uint cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct USB_INTERFACE_DESCRIPTOR
    {
        public byte bLength, bDescriptorType, bInterfaceNumber, bAlternateSetting, bNumEndpoints;
        public byte bInterfaceClass, bInterfaceSubClass, bInterfaceProtocol, iInterface;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct WINUSB_PIPE_INFORMATION
    {
        public int PipeType;
        public byte PipeId;
        public ushort MaximumPacketSize;
        public byte Interval;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    internal static extern IntPtr SetupDiGetClassDevsW(ref Guid classGuid, string? enumerator,
        IntPtr parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo,
        ref Guid interfaceGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA data);
    [DllImport("setupapi.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr set, ref SP_DEVICE_INTERFACE_DATA iface,
        IntPtr detail, uint detailSize, out uint required, IntPtr devInfo);
    [DllImport("setupapi.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr set, ref SP_DEVICE_INTERFACE_DATA iface,
        IntPtr detail, uint detailSize, out uint required, ref SP_DEVINFO_DATA devInfo);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiGetDeviceInstanceIdW(IntPtr set, ref SP_DEVINFO_DATA devInfo,
        StringBuilder id, int idSize, out int required);
    [DllImport("setupapi.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    internal static extern SafeFileHandle CreateFileW(string path, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CancelIoEx(SafeFileHandle handle, IntPtr overlapped);
    [DllImport("winusb.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WinUsb_Initialize(SafeFileHandle device, out IntPtr interfaceHandle);
    [DllImport("winusb.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WinUsb_Free(IntPtr interfaceHandle);
    [DllImport("winusb.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WinUsb_QueryInterfaceSettings(IntPtr handle, byte alternate,
        out USB_INTERFACE_DESCRIPTOR descriptor);
    [DllImport("winusb.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WinUsb_QueryPipe(IntPtr handle, byte alternate, byte index,
        out WINUSB_PIPE_INFORMATION info);
    [DllImport("winusb.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WinUsb_SetPipePolicy(IntPtr handle, byte pipe, uint policy,
        uint valueLength, ref uint value);
    [DllImport("winusb.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WinUsb_ReadPipe(IntPtr handle, byte pipe, [Out] byte[] bytes,
        uint length, out uint transferred, IntPtr overlapped);
    [DllImport("winusb.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WinUsb_WritePipe(IntPtr handle, byte pipe, byte[] bytes,
        uint length, out uint transferred, IntPtr overlapped);
}
