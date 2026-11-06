namespace AutoTest.Devices.Usb;
// No production libusb implementation in V1; explicit simulation link must be injected.
public interface IUsbByteLink : IAsyncDisposable
{
    ValueTask OpenAsync(CancellationToken ct);
    ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken ct);
    ValueTask WriteAsync(ReadOnlyMemory<byte> source, CancellationToken ct);
    ValueTask CloseAsync(CancellationToken ct);
}
