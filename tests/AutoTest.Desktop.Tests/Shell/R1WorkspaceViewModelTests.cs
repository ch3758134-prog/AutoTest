using System;
using System.Collections.Generic;
using System.Linq;
using AutoTest.Desktop.Shell.R1;
using Xunit;

namespace AutoTest.Desktop.Tests.Shell;

public sealed class R1WorkspaceViewModelTests
{
    [Fact]
    public void NavigationContainsAllThirteenDistinctWorkspaces()
    {
        var vm = new R1WorkspaceViewModel(new FakeHost());
        Assert.Equal(13, vm.Navigation.Count);
        Assert.Equal(13, vm.Navigation.Select(x => x.Id).Distinct().Count());
        Assert.Contains(vm.Navigation, x => x.Id == "P06" && x.Owner == "UI/BENCH");
        Assert.Contains(vm.Navigation, x => x.Id == "P13" && x.Owner == "RECORD");
        Assert.Empty(vm.OpenTabs);
    }

    [Fact]
    public void UnwiredOrRejectedViewNeverCreatesFakeDocument()
    {
        var host = new FakeHost();
        var vm = new R1WorkspaceViewModel(host);
        var bench = vm.Navigation.Single(x => x.Id == "P06");
        vm.Open(bench);
        Assert.Empty(vm.OpenTabs);
        Assert.Equal(0, host.Activations);
        vm.UpdateAvailability("P06", WorkspaceAvailability.Available);
        host.AllowActivation = false;
        vm.Open(bench);
        Assert.Empty(vm.OpenTabs);
        Assert.Contains("未获授权", vm.Notice);
    }

    [Fact]
    public void ReopeningExistingDocumentDoesNotCreateSecondProfessionalSession()
    {
        var host = new FakeHost();
        var vm = new R1WorkspaceViewModel(host);
        var uds = vm.Navigation.Single(x => x.Id == "P08");
        vm.UpdateAvailability("P08", WorkspaceAvailability.Available);
        vm.AcceptContext("project-A", "rev-1", "station-1", 3, "Replay");
        vm.Open(uds);
        vm.Open(uds);
        Assert.Single(vm.OpenTabs);
        Assert.Equal(1, host.Activations);
        Assert.Equal("rev-1", vm.ActiveTab!.ContextRevision);
        vm.AcceptContext("project-A", "rev-2", "station-2", 4, "Physical");
        Assert.Equal("rev-1", vm.ActiveTab.ContextRevision);
        Assert.Equal("rev-2", vm.ResourceRevision);
    }

    [Fact]
    public void DirtyOrBusyTabCannotBeClosedByShellWithoutSpecialistConsent()
    {
        var host = new FakeHost();
        var vm = new R1WorkspaceViewModel(host);
        var project = vm.Navigation.Single(x => x.Id == "P01");
        vm.UpdateAvailability(project.Id, WorkspaceAvailability.Available);
        vm.Open(project);
        var tab = Assert.Single(vm.OpenTabs);
        tab.IsDirty = true;
        vm.Close(tab);
        Assert.Single(vm.OpenTabs);
        tab.IsDirty = false;
        tab.IsBusy = true;
        vm.Close(tab);
        Assert.Single(vm.OpenTabs);
        Assert.Equal(0, host.Closes);
        tab.IsBusy = false;
        vm.Close(tab);
        Assert.Empty(vm.OpenTabs);
        Assert.Equal(1, host.Closes);
    }

    [Fact]
    public void SearchFiltersByIdOwnerAndTitleWithoutAffectingOpenTabs()
    {
        var vm = new R1WorkspaceViewModel(new FakeHost());
        vm.Filter = "wave";
        Assert.Equal("P07", Assert.Single(vm.FilteredNavigation).Id);
        vm.Filter = "project";
        Assert.Contains(vm.FilteredNavigation, x => x.Id == "P01");
        vm.Filter = "P10";
        Assert.Equal("Test Case Editor", Assert.Single(vm.FilteredNavigation).Label);
        vm.Filter = "";
        Assert.Equal(13, vm.FilteredNavigation.Count);
    }

    [Fact]
    public void ContextOriginUnknownCannotMasqueradeAsPhysical()
    {
        var vm = new R1WorkspaceViewModel(new FakeHost());
        vm.AcceptContext("P", "rev", "station", 10, "Physical authorized");
        Assert.Equal("UNKNOWN", vm.Origin);
        vm.AcceptContext("P", "rev", "station", 11, "Mock");
        Assert.Equal("Mock", vm.Origin);
        Assert.Equal("11", vm.SessionGeneration);
    }

    [Fact]
    public void KeyboardNavigationCyclesTabsWithoutReactivatingProfessionalHost()
    {
        var host = new FakeHost();
        var vm = new R1WorkspaceViewModel(host);
        var a = vm.Navigation.Single(x => x.Id == "P01");
        var b = vm.Navigation.Single(x => x.Id == "P07");
        vm.UpdateAvailability(a.Id, WorkspaceAvailability.Available);
        vm.UpdateAvailability(b.Id, WorkspaceAvailability.Available);
        vm.Open(a);
        vm.Open(b);
        Assert.Equal("P07", vm.ActiveTab!.Id);
        vm.NextTabCommand.Execute();
        Assert.Equal("P01", vm.ActiveTab!.Id);
        vm.PreviousTabCommand.Execute();
        Assert.Equal("P07", vm.ActiveTab!.Id);
        Assert.Equal(2, host.Activations);
    }

    [Fact]
    public void RevisionChangeMarksOldDocumentStaleAndClosingUsesExistingGuard()
    {
        var host = new FakeHost();
        var vm = new R1WorkspaceViewModel(host);
        var project = vm.Navigation.Single(x => x.Id == "P01");
        vm.UpdateAvailability(project.Id, WorkspaceAvailability.Available);
        vm.AcceptContext("p", "rev-A", null, null, "Mock");
        vm.Open(project);
        var tab = Assert.Single(vm.OpenTabs);
        Assert.Equal("READY", tab.StateLabel);
        vm.AcceptContext("p", "rev-B", null, null, "Replay");
        Assert.True(tab.ContextStale);
        Assert.Equal("STALE", tab.StateLabel);
        Assert.Equal("rev-A", tab.ContextRevision);
        tab.IsDirty = true;
        Assert.Equal("DIRTY", tab.StateLabel);
        vm.CloseActiveTabCommand.Execute();
        Assert.Single(vm.OpenTabs);
        tab.IsDirty = false;
        vm.CloseActiveTabCommand.Execute();
        Assert.Empty(vm.OpenTabs);
        Assert.Equal(1, host.Closes);
    }

    [Fact]
    public void NoTabsKeyboardActionsAreSafe()
    {
        var vm = new R1WorkspaceViewModel(new FakeHost());
        vm.NextTabCommand.Execute();
        vm.PreviousTabCommand.Execute();
        vm.CloseActiveTabCommand.Execute();
        Assert.Empty(vm.OpenTabs);
        Assert.Null(vm.ActiveTab);
    }

    [Fact]
    public void ChangingThemeAndDensityDoesNotChangeAnyOpenedDocumentOrSession()
    {
        var host = new FakeHost();
        var vm = new R1WorkspaceViewModel(host);
        var wave = vm.Navigation.Single(x => x.Id == "P07");
        vm.UpdateAvailability(wave.Id, WorkspaceAvailability.Available);
        vm.AcceptContext("project", "rev-3", "station", 21, "Replay");
        vm.Open(wave);
        var tab = Assert.Single(vm.OpenTabs);
        Assert.Equal(R1Theme.Dark, vm.Theme);
        Assert.Equal(R1Density.Compact, vm.Density);
        vm.ToggleThemeCommand.Execute();
        vm.ToggleDensityCommand.Execute();
        Assert.Equal(R1Theme.Light, vm.Theme);
        Assert.Equal(R1Density.Comfortable, vm.Density);
        Assert.Equal("浅色", vm.ThemeLabel);
        Assert.Equal("舒适", vm.DensityLabel);
        Assert.Same(tab, vm.ActiveTab);
        Assert.Equal("rev-3", vm.ActiveTab!.ContextRevision);
        Assert.Equal("Replay", vm.Origin);
        Assert.Equal("21", vm.SessionGeneration);
        Assert.Equal(1, host.Activations);
        Assert.Equal(0, host.Closes);
    }

    [Fact]
    public void ThemeAndDensityAreReversibleWithoutChangingOpenTabState()
    {
        var vm = new R1WorkspaceViewModel(new FakeHost());
        vm.ToggleThemeCommand.Execute();
        vm.ToggleThemeCommand.Execute();
        vm.ToggleDensityCommand.Execute();
        vm.ToggleDensityCommand.Execute();
        Assert.Equal(R1Theme.Dark, vm.Theme);
        Assert.Equal(R1Density.Compact, vm.Density);
        Assert.Empty(vm.OpenTabs);
        Assert.Equal("UNKNOWN", vm.ResourceRevision);
    }

    private sealed class FakeHost : IWorkspaceDocumentHost
    {
        public bool AllowActivation { get; set; } = true;
        public int Activations { get; private set; }
        public int Closes { get; private set; }
        public bool TryActivate(string workspaceId, out string failureReason)
        {
            Activations++;
            failureReason = AllowActivation ? "" : "未获授权";
            return AllowActivation;
        }
        public bool TryClose(string workspaceId, out string failureReason)
        {
            Closes++;
            failureReason = "";
            return true;
        }
    }
}
