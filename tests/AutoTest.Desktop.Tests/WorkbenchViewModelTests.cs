using AutoTest.Application.Composition;
using AutoTest.Contracts;
using AutoTest.Desktop.Shell;
using Xunit;

namespace AutoTest.Desktop.Tests;

public sealed class WorkbenchViewModelTests
{
    [Fact]
    public async Task VmOnlyReadsGatewayAndSeparatesSimulationStatus()
    {
        var mock = ApplicationComposition.RegisterMock();
        var vm = new MainWindowViewModel(mock.Gateway);
        Assert.True(((System.Windows.Input.ICommand)vm.NormalCommand).CanExecute(null));
        await vm.ExecuteScenarioAsync(ApplicationCommandKind.MockNormal);
        Assert.Equal("SIMULATOR_ACK_ONLY", vm.AckStatus);
        Assert.Equal("SIMULATED_INDICATOR_READY", vm.ObservationStatus);
        Assert.Equal("Inconclusive", vm.Verdict);
        Assert.True(((System.Windows.Input.ICommand)vm.NormalCommand).CanExecute(null));
        Assert.NotEmpty(vm.FactLines);
        await vm.ExecuteScenarioAsync(ApplicationCommandKind.MockTimeout);
        Assert.Equal("NO_ACK", vm.AckStatus);
        Assert.Equal("MOCK_TIMEOUT", vm.FailureReason);
    }

    [Fact]
    public async Task UiUnsafeKindCannotBeDispatchedThroughVm()
    {
        var mock = ApplicationComposition.RegisterMock();
        var vm = new MainWindowViewModel(mock.Gateway);
        await vm.ExecuteScenarioAsync(ApplicationCommandKind.RealProgramming);
        Assert.StartsWith("V0_SCOPE_DENIED", vm.FailureReason);
        Assert.Equal(0, mock.Station.CurrentGeneration);
    }
}
