using AutoTest.Desktop.Tools.Flash;
using Xunit;

namespace AutoTest.Desktop.Tests.Tools.Flash;

public sealed class FlashToolViewModelTests
{
    [Fact]
    public async Task LoadedImagePreflightAndDryRunAreUsableWithoutHardware()
    {
        var vm=new FlashToolViewModel();
        Assert.False(vm.PhysicalProgrammingEnabled);
        vm.LoadBytes("test.bin",Enumerable.Range(0,180).Select(i=>(byte)i).ToArray(),0x1000);
        Assert.Equal("180",vm.Bytes);
        await vm.PreflightAsync();
        Assert.Equal("MOCK_READY",vm.Preflight);
        await vm.RunDryAsync();
        Assert.Equal("MOCK_COMPLETED",vm.Status);
        Assert.NotEmpty(vm.Stages);
    }

    [Fact]
    public async Task WrongIdentityRejectsAndPreventsRun()
    {
        var vm=new FlashToolViewModel();
        vm.LoadBytes("test.bin",new byte[]{1,2,3},0x1000);
        vm.ObservedIdentity="OTHER_ECU";
        await vm.RunDryAsync();
        Assert.Equal("PREFLIGHT_BLOCKED",vm.Status);
        Assert.Contains("ECU_IDENTITY_MISMATCH",vm.Failure);
    }

    [Fact]
    public async Task DryRunShowsCorrelatedUdsEventsAndLastConfirmedBlock()
    {
        var vm=new FlashToolViewModel();
        vm.LoadBytes("test.bin",Enumerable.Range(0,180).Select(i=>(byte)i).ToArray(),0x1000);
        await vm.RunDryAsync();
        Assert.Equal("MOCK_COMPLETED",vm.Status);
        Assert.Equal("180",vm.ConfirmedBytes);
        Assert.NotEqual("NONE",vm.LastAcknowledgedBlock);
        Assert.Contains(vm.DiagnosticTrace,x=>x.Contains("CorrelationId=") && x.Contains("SID=0x36"));
        Assert.Contains(vm.DiagnosticTrace,x=>x.Contains("REDACTED_FIRMWARE"));
        Assert.DoesNotContain(vm.DiagnosticTrace,x=>x.Contains("27 02"));
    }

    [Fact]
    public async Task InjectedTransferNrcRetainsFailureAndPartialTrace()
    {
        var vm=new FlashToolViewModel { FaultSid="36", FaultNrc="72" };
        vm.LoadBytes("test.bin",Enumerable.Range(0,180).Select(i=>(byte)i).ToArray(),0x1000);
        await vm.RunDryAsync();
        Assert.Equal("MOCK_FAILED",vm.Status);
        Assert.NotEmpty(vm.DiagnosticTrace);
        Assert.Contains(vm.DiagnosticTrace,x=>x.Contains("NRC=72"));
        Assert.NotEqual("MOCK_COMPLETED",vm.Status);
    }

    [Fact]
    public async Task FailureAtSecondBlockRetainsFirstAckAndNeverClaimsFullProgress()
    {
        var vm=new FlashToolViewModel { DropBlock="2" };
        vm.LoadBytes("test.bin",Enumerable.Range(0,180).Select(i=>(byte)i).ToArray(),0x1000);
        await vm.RunDryAsync();
        Assert.Equal("MOCK_UNCERTAIN",vm.Status);
        Assert.Equal("1",vm.LastAcknowledgedBlock);
        Assert.Equal("64",vm.ConfirmedBytes);
        Assert.Contains(vm.DiagnosticTrace,x=>x.Contains("Block=2"));
        Assert.Contains(vm.RecoveryAdvice,"不完整");
    }

    [Fact]
    public async Task UncertainStatePresentsExplicitRecoveryAndFailurePosition()
    {
        var vm=new FlashToolViewModel { DropBlock="2" };
        vm.LoadBytes("firmware.bin",Enumerable.Range(0,180)
            .Select(i=>(byte)i).ToArray(),0x1000);
        await vm.RunDryAsync();
        Assert.Equal("MOCK_UNCERTAIN",vm.Status);
        Assert.Contains("UNCONFIRMED_UDS_RESPONSE",vm.Failure);
        Assert.True(vm.Failure.Contains("block 2",StringComparison.OrdinalIgnoreCase));
        Assert.Contains("不得自动续写",vm.RecoveryAdvice);
        Assert.Equal("64",vm.ConfirmedBytes);
    }

    [Fact]
    public async Task AuditLabelRemainsPinnedToJobImageAfterAnotherImport()
    {
        var vm=new FlashToolViewModel();
        vm.LoadBytes("first.bin",Enumerable.Range(0,120).Select(i=>(byte)i).ToArray(),0x1000);
        await vm.RunDryAsync();
        Assert.Equal("MOCK_COMPLETED",vm.Status);
        Assert.Contains("first.bin",vm.AuditJobImage);
        vm.LoadBytes("second.bin",new byte[]{1,2,3,4},0x1000);
        Assert.Equal("IMAGE_LOADED",vm.Status);
        Assert.Contains("first.bin",vm.AuditJobImage);
        Assert.DoesNotContain("second.bin",vm.AuditJobImage);
    }
}
