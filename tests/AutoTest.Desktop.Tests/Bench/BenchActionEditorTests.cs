using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoTest.Desktop.Bench;
using Xunit;

namespace AutoTest.Desktop.Tests.Bench;

public sealed class BenchActionEditorTests
{
    [Fact]
    public async Task UserCanCreateAnyNamedOrderedActionAndSaveWithoutPhysicalOutput()
    {
        var fake = new ActionPort();
        var vm = new BenchKeyActionEditorViewModel(fake);
        vm.NewActionCommand.Execute(null);
        Assert.Single(vm.Actions);
        vm.SelectedAction!.Name = "工程定义的任意真实操作";
        vm.AddSetKeysCommand.Execute(null);
        vm.Switches[0].IsSelected = true;
        vm.Switches[12].IsSelected = true;
        vm.AddHoldCommand.Execute(null);
        vm.SelectedStep!.DurationMs = 380;
        vm.AddSetKeysCommand.Execute(null); // explicit all-open switch state, NOT a hardcoded idle
        Assert.Equal(0u, vm.SelectedStep!.Mask);
        Assert.Equal(0, fake.Runs);
        await vm.SaveAsync();
        Assert.False(vm.HasUnsavedChanges);
        Assert.Equal(3, fake.Saved!.Single().Steps.Count);
        Assert.Equal((1u << 0) | (1u << 12), fake.Saved.Single().Steps[0].Mask);
        Assert.Equal(380, fake.Saved.Single().Steps[1].DurationMs);
        Assert.Equal(0u, fake.Saved.Single().Steps[2].Mask);
        await vm.RunAsync();
        Assert.Equal(1, fake.Runs);
        Assert.Equal("工程定义的任意真实操作", fake.LastAction!.Name);
    }

    [Fact]
    public async Task ActionCannotRunUnsavedOrHoldUnknownKeyState()
    {
        var fake = new ActionPort();
        var vm = new BenchKeyActionEditorViewModel(fake);
        vm.NewActionCommand.Execute(null);
        vm.AddHoldCommand.Execute(null);
        await vm.SaveAsync();
        Assert.True(vm.HasUnsavedChanges);
        Assert.Null(fake.Saved);
        await vm.RunAsync();
        Assert.Equal(0, fake.Runs);
        vm.AddSetKeysCommand.Execute(null); // inserts after the selected hold
        vm.MoveStepUpCommand.Execute(null);  // now starts with SetKeys
        await vm.SaveAsync();
        Assert.False(vm.HasUnsavedChanges);
        await vm.RunAsync();
        Assert.Equal(1, fake.Runs);
    }

    [Fact]
    public async Task ReloadCannotDiscardDirtyProjectAction()
    {
        var fake = new ActionPort
        {
            Saved = new[] { new BenchKeyAction(Guid.NewGuid().ToString(), "previous",
                new[] { BenchActionStep.SetKeys(2) }) }
        };
        var vm = new BenchKeyActionEditorViewModel(fake);
        await vm.LoadAsync();
        Assert.Single(vm.Actions);
        vm.NewActionCommand.Execute(null);
        await vm.LoadAsync();
        Assert.Equal(2, vm.Actions.Count);
        Assert.True(vm.HasUnsavedChanges);
    }

    [Fact]
    public async Task StopCancelsLongActionWithoutInventingRelease()
    {
        var fake = new ActionPort { WaitUntilCancelled = true };
        var vm = new BenchKeyActionEditorViewModel(fake);
        vm.NewActionCommand.Execute(null);
        vm.AddSetKeysCommand.Execute(null);
        await vm.SaveAsync();
        var running = vm.RunAsync();
        Assert.True(vm.IsActionRunning);
        vm.StopActionCommand.Execute(null);
        await running;
        Assert.False(vm.IsActionRunning);
        Assert.Contains("取消", vm.Status);
        Assert.Equal(1, fake.Runs);
    }

    [Fact]
    public async Task PersistedActionsBelongToProjectIdAndRejectCrossProjectReuse()
    {
        var root = Path.Combine(Path.GetTempPath(), "autotest-bench-actions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "demo.autotest-project.json");
        File.WriteAllText(file, "{}");
        var id = Guid.NewGuid().ToString("D");
        var active = id;
        try
        {
            var store = new ProjectBenchActionsStore(() => file, () => active);
            var action = new BenchKeyAction(Guid.NewGuid().ToString("D"), "custom",
                new[] { BenchActionStep.SetKeys(0x80001), BenchActionStep.Hold(175),
                    BenchActionStep.SetKeys(0) });
            await store.SaveAsync(new[] { action }, CancellationToken.None);
            Assert.Equal(action, (await store.LoadAsync(CancellationToken.None)).Single() with
            {
                Steps = action.Steps
            });
            active = Guid.NewGuid().ToString("D");
            await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync(CancellationToken.None));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void InvalidMasksOrDurationsNeverBecomeValidActions()
    {
        var id = Guid.NewGuid().ToString("D");
        Assert.Throws<ArgumentException>(() => BenchActionValidation.Validate(
            new(id, "bad", new[] { BenchActionStep.SetKeys(0x100000) })));
        Assert.Throws<ArgumentException>(() => BenchActionValidation.Validate(
            new(id, "bad", new[] { BenchActionStep.Hold(300) })));
        Assert.Throws<ArgumentException>(() => BenchActionValidation.Validate(
            new(id, "bad", new[] { BenchActionStep.SetKeys(2), BenchActionStep.Hold(0) })));
        BenchActionValidation.Validate(new(id, "all-open is valid", new[] { BenchActionStep.SetKeys(0) }));
    }

    private sealed class ActionPort : IBenchWorkbenchPort
    {
        public IReadOnlyList<BenchKeyAction>? Saved { get; set; }
        public BenchKeyAction? LastAction { get; private set; }
        public int Runs { get; private set; }
        public bool WaitUntilCancelled { get; set; }

        public Task<IReadOnlyList<BenchKeyAction>> LoadKeyActionsAsync(CancellationToken ct) =>
            Task.FromResult(Saved ?? (IReadOnlyList<BenchKeyAction>)Array.Empty<BenchKeyAction>());
        public Task SaveKeyActionsAsync(IReadOnlyList<BenchKeyAction> actions, CancellationToken ct)
        { Saved = actions; return Task.CompletedTask; }
        public async Task<BenchActionExecution> RunKeyActionAsync(
            BenchKeyAction action, IProgress<BenchActionProgress>? progress, CancellationToken ct)
        {
            LastAction = action; Runs++;
            if (WaitUntilCancelled) await Task.Delay(TimeSpan.FromSeconds(20), ct);
            progress?.Report(new BenchActionProgress(action.Steps.Count, action.Steps.Count, "完成"));
            return new BenchActionExecution(true, false, false, "No physical output by this test port");
        }
        public Task<BenchConfiguration?> LoadConfigurationAsync(CancellationToken ct) =>
            Task.FromResult<BenchConfiguration?>(null);
        public Task<BenchConnectionSnapshot> GetConnectionAsync(CancellationToken ct) =>
            Task.FromResult(new BenchConnectionSnapshot(BenchConnection.Disconnected, null, null, null, null, null));
        public Task<IReadOnlyList<BenchDevice>> DiscoverCompatibleDevicesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<BenchDevice>>(Array.Empty<BenchDevice>());
        public Task<BenchConnectionSnapshot> ConnectAsync(string id, CancellationToken ct) =>
            GetConnectionAsync(ct);
        public Task<BenchConnectionSnapshot> DisconnectAsync(CancellationToken ct) =>
            GetConnectionAsync(ct);
        public Task<BenchOperationResult> ApplyStateAsync(
            string revision, string controller, string state, BenchEvidenceSource source, CancellationToken ct) =>
            Task.FromResult(new BenchOperationResult(false, null, null, null, null, null,
                null, "BLOCKED", "No hardware in test", source, null));
        public Task SaveManualMeasurementAsync(BenchManualMeasurement measurement, CancellationToken ct) =>
            Task.CompletedTask;
    }
}
