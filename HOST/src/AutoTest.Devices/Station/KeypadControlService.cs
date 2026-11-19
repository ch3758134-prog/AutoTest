using AutoTest.Contracts;
using AutoTest.Core;
namespace AutoTest.Devices.Station;
public sealed class KeypadControlService
{
    public CanFrame PrepareMask(uint mask) => KeypadCommandCodec.EncodeMask(mask);
    public CanFrame PrepareReleaseAll() => KeypadCommandCodec.EncodeReleaseAll();
    public ValueTask<OperationResult<CanTxReceipt>> SetMaskAsync(uint mask,CancellationToken ct=default)
    {
        ct.ThrowIfCancellationRequested();
        _ = KeypadCommandCodec.EncodeMask(mask);
        return ValueTask.FromResult(OperationResult<CanTxReceipt>.Rejected("BENCH_NOT_AUTHORIZED","Only encoding and Mock are permitted."));
    }
}
