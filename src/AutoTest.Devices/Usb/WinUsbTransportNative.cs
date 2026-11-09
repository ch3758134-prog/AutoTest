using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace AutoTest.Devices.Usb;

/// <summary>Thin native-call seam. The production implementation remains WinUSB; the test
/// implementation permits a full failure/close/reconnect lifecycle without connecting a DUT.</summary>
internal interface IWinUsbTransportNative
{
    bool IsSupported { get; }
    int LastError { get; }
    SafeFileHandle Open(string path, uint flags);
    bool Initialize(SafeFileHandle file, out IntPtr interfaceHandle);
    bool QueryInterfaceSettings(IntPtr handle, out WinUsbNative.USB_INTERFACE_DESCRIPTOR descriptor);
    bool QueryPipe(IntPtr handle, byte index, out WinUsbNative.WINUSB_PIPE_INFORMATION pipe);
    bool SetPipeTimeout(IntPtr handle, byte endpoint, ref uint timeoutMs);
    bool Read(IntPtr handle, byte[] buffer, out uint transferred);
    bool Write(IntPtr handle, byte[] bytes, out uint transferred);
    bool Cancel(SafeFileHandle handle);
    void Free(IntPtr interfaceHandle);
}

internal sealed class SystemWinUsbTransportNative : IWinUsbTransportNative
{
    public static readonly SystemWinUsbTransportNative Instance = new();
    private SystemWinUsbTransportNative() { }
    public bool IsSupported => OperatingSystem.IsWindows();
    public int LastError => Marshal.GetLastWin32Error();
    public SafeFileHandle Open(string path, uint flags) => WinUsbNative.CreateFileW(path,
        WinUsbNative.GenericRead | WinUsbNative.GenericWrite,
        WinUsbNative.FileShareRead | WinUsbNative.FileShareWrite,
        IntPtr.Zero, WinUsbNative.OpenExisting, flags, IntPtr.Zero);
    public bool Initialize(SafeFileHandle file, out IntPtr handle) => WinUsbNative.WinUsb_Initialize(file, out handle);
    public bool QueryInterfaceSettings(IntPtr handle, out WinUsbNative.USB_INTERFACE_DESCRIPTOR descriptor) =>
        WinUsbNative.WinUsb_QueryInterfaceSettings(handle, 0, out descriptor);
    public bool QueryPipe(IntPtr handle, byte index, out WinUsbNative.WINUSB_PIPE_INFORMATION pipe) =>
        WinUsbNative.WinUsb_QueryPipe(handle, 0, index, out pipe);
    public bool SetPipeTimeout(IntPtr handle, byte endpoint, ref uint ms) =>
        WinUsbNative.WinUsb_SetPipePolicy(handle, endpoint, WinUsbNative.PipeTransferTimeout, sizeof(uint), ref ms);
    // NULL OVERLAPPED is the documented synchronous WinUsb_ReadPipe/WritePipe mode;
    // CreateFile still needs FILE_FLAG_OVERLAPPED for WinUsb_Initialize.
    public bool Read(IntPtr handle, byte[] buffer, out uint n) =>
        WinUsbNative.WinUsb_ReadPipe(handle, MainControllerUsbIdentity.BulkIn, buffer, (uint)buffer.Length, out n, IntPtr.Zero);
    public bool Write(IntPtr handle, byte[] bytes, out uint n) =>
        WinUsbNative.WinUsb_WritePipe(handle, MainControllerUsbIdentity.BulkOut, bytes, (uint)bytes.Length, out n, IntPtr.Zero);
    public bool Cancel(SafeFileHandle file) => WinUsbNative.CancelIoEx(file, IntPtr.Zero);
    public void Free(IntPtr handle) => WinUsbNative.WinUsb_Free(handle);
}
