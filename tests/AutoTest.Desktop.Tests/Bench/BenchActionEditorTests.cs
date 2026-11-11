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
        vm.NewActionCommand.Execute();
        Assert.Single(vm.Actions);
        vm.SelectedAction!.Name = "工程定义的任意真实操作";
        // New action immediately exposes its first 20-switch editing step.
        Assert.Single(vm.Steps);
        vm.SelectedStep!.Name = "准备按键组合";
        vm.Switches[0].IsSelected = true;
        vm.Switches[12].IsSelected = true;
        vm.AddHoldCommand.Execute();
        vm.SelectedStep!.Name = "稳定保持阶段";
        vm.SelectedStep.DurationMs = 380;
        vm.AddSetKeysCommand.Execute(); // explicit all-open switch state, NOT a hardcoded idle
        Assert.Equal(0u, vm.SelectedStep!.Mask);
        Assert.Equal(0, fake.Runs);
        await vm.SaveAsync();
        Assert.False(vm.HasUnsavedChanges);
        Assert.Equal(3, fake.Saved!.Single().Steps.Count);
        Assert.Equal((1u << 0) | (1u << 12), fake.Saved.Single().Steps[0].Mask);
        Assert.Equal(380, fake.Saved.Single().Steps[1].DurationMs);
        Assert.Equal("准备按键组合", fake.Saved.Single().Steps[0].Name);
        Assert.Equal("稳定保持阶段", fake.Saved.Single().Steps[1].Name);
        Assert.Equal(0u, fake.Saved.Single().Steps[2].Mask);
        await vm.RunAsync();
        Assert.Equal(1, fake.Runs);
        Assert.Equal("工程定义的任意真实操作", fake.LastAction!.Name);
    }

    [Fact]
    public async Task InvalidActionCannotRunButValidUnsavedKeyActionCanBeTried()
    {
        var fake = new ActionPort();
        var vm = new BenchKeyActionEditorViewModel(fake);
        vm.NewActionCommand.Execute();
        vm.AddHoldCommand.Execute();
        vm.MoveStepUpCommand.Execute(); // invalid: starts by holding an undefined mask
        await vm.SaveAsync();
        Assert.True(vm.HasUnsavedChanges);
        Assert.Null(fake.Saved);
        await vm.RunAsync();
        Assert.Equal(0, fake.Runs);
        vm.MoveStepDownCommand.Execute(); // restore valid order: SetKeys then Hold
        await vm.RunAsync();
        Assert.Equal(1, fake.Runs);
        Assert.True(vm.HasUnsavedChanges);
        Assert.Null(fake.Saved);
    }

    [Fact]
    public async Task InvalidDisplayedHoldTextCannotExecuteStaleNumericHold()
    {
        var port = new ActionPort();
        var vm = new BenchKeyActionEditorViewModel(port);
        vm.NewActionCommand.Execute();
        vm.AddHoldCommand.Execute();
        vm.SelectedStep!.DurationMs = 300;
        vm.SelectedStep.DurationMsText = "not-a-number";
        await vm.RunAsync();
        Assert.Equal(0, port.Runs);
        Assert.Contains("动作不合法", vm.Status);
        await vm.SaveAsync();
        Assert.Null(port.Saved);
        Assert.True(vm.HasUnsavedChanges);
        vm.SelectedStep.DurationMsText = "350";
        await vm.RunAsync();
        Assert.Equal(1, port.Runs);
        Assert.Equal(350, port.LastAction!.Steps[1].DurationMs);
    }

    [Fact]
    public async Task KeySimDraftCannotExecuteOrSaveAfterProjectSwitch()
    {
        var port = new ActionPort { ActiveProjectId = "ECU-A" };
        var vm = new BenchKeyActionEditorViewModel(port);
        await vm.LoadAsync();
        vm.NewActionCommand.Execute();
        vm.Switches[4].IsSelected = true;
        port.ActiveProjectId = "ECU-B";
        await vm.RunAsync();
        await vm.SaveAsync();
        Assert.Equal(0, port.Runs);
        Assert.Null(port.Saved);
        Assert.Contains("工程", vm.Status);
        port.ActiveProjectId = "ECU-A";
        await vm.RunAsync();
        Assert.Equal(1, port.Runs);
        port.ActiveProjectId = "ECU-B";
        await vm.DiscardDraftAndReloadAsync();
        Assert.Empty(vm.Actions);
        Assert.False(vm.HasUnsavedChanges);
    }

    [Fact]
    public async Task CleanSavedKeyActionsReloadForNewProjectWithoutStaleRows()
    {
        var port = new ActionPort { ActiveProjectId = "ECU-A" };
        var vm = new BenchKeyActionEditorViewModel(port);
        await vm.LoadAsync();
        vm.NewActionCommand.Execute();
        await vm.SaveAsync();
        Assert.Single(vm.Actions);
        port.Saved = Array.Empty<BenchKeyAction>();
        port.ActiveProjectId = "ECU-B";
        await vm.LoadAsync();
        Assert.Empty(vm.Actions);
        Assert.False(vm.HasUnsavedChanges);
    }

    [Fact]
    public async Task SelectedNamedKeyStepCanSendPhysicalMaskWithoutSavingWholeAction()
    {
        var port = new ActionPort();
        var vm = new BenchKeyActionEditorViewModel(port);
        vm.NewActionCommand.Execute();
        vm.SelectedStep!.Name = "用户自定义短按触发";
        vm.Switches[0].IsSelected = true;
        vm.Switches[12].IsSelected = true;
        Assert.Equal(0, port.DirectKeyCalls); // editing never sends
        await vm.SendSelectedStepAsync();
        Assert.Equal(1, port.DirectKeyCalls);
        Assert.Equal((1u << 0) | (1u << 12), port.LastDirectMask);
        Assert.Null(port.Saved);
        Assert.True(vm.HasUnsavedChanges);
        Assert.Contains("用户自定义短按触发", vm.SelectedStepResult);
        vm.ClearMaskCommand.Execute();
        await vm.SendSelectedStepAsync();
        Assert.Equal(1, port.ReleaseCalls); // deliberate zero mask = release
    }

    [Fact]
    public async Task PerStepNamesSurviveProjectStoreReloadAndRemainEditable()
    {
        var root = Path.Combine(Path.GetTempPath(), "autotest-named-key-steps-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "named.autotest-project.json");
        File.WriteAllText(file, "{}");
        var id = Guid.NewGuid().ToString("D");
        try
        {
            var store = new ProjectBenchActionsStore(() => file, () => id);
            var action = new BenchKeyAction(Guid.NewGuid().ToString("D"), "实验 A", new[] {
                BenchActionStep.SetKeys(0x04001, "第一次触发"),
                BenchActionStep.Hold(150, "等待调试报文"),
                BenchActionStep.SetKeys(0, "全部断开")});
            await store.SaveAsync(new[] { action }, CancellationToken.None);
            var loaded = (await store.LoadAsync(CancellationToken.None)).Single();
            Assert.Equal(action.Steps.ToArray(), loaded.Steps.ToArray());
            Assert.Equal("第一次触发", loaded.Steps[0].Name);
            Assert.Equal("等待调试报文", loaded.Steps[1].Name);
        }
        finally { Directory.Delete(root, recursive: true); }
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
        vm.NewActionCommand.Execute();
        await vm.LoadAsync();
        Assert.Equal(2, vm.Actions.Count);
        Assert.True(vm.HasUnsavedChanges);
    }

    [Fact]
    public async Task StopCancelsLongActionWithoutInventingRelease()
    {
        var fake = new ActionPort { WaitUntilCancelled = true };
        var vm = new BenchKeyActionEditorViewModel(fake);
        vm.NewActionCommand.Execute();
        await vm.SaveAsync();
        var running = vm.RunAsync();
        Assert.True(vm.IsActionRunning);
        vm.StopActionCommand.Execute();
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
            var loaded = (await store.LoadAsync(CancellationToken.None)).Single();
            Assert.Equal(action.Id, loaded.Id);
            Assert.Equal(action.Name, loaded.Name);
            Assert.Equal(action.Steps.ToArray(), loaded.Steps.ToArray());
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
        public string? ActiveProjectId { get; set; }
        public IReadOnlyList<BenchKeyAction>? Saved { get; set; }
        public BenchKeyAction? LastAction { get; private set; }
        public int Runs { get; private set; }
        public int DirectKeyCalls { get; private set; }
        public int ReleaseCalls { get; private set; }
        public uint LastDirectMask { get; private set; }
        public Task<BenchOperationResult> ApplyPhysicalKeyMaskAsync(uint mask, CancellationToken ct)
        {
            DirectKeyCalls++;
            LastDirectMask = mask;
            return Task.FromResult(new BenchOperationResult(true, "key-op", "key-corr", 1,
                "KEY_SIM", "ACK", null, null, null, BenchEvidenceSource.Physical, "physical fixture"));
        }
        public Task<BenchOperationResult> ReleaseAllPhysicalKeysAsync(CancellationToken ct)
        {
            ReleaseCalls++;
            return Task.FromResult(new BenchOperationResult(true, "key-release", "key-corr", 1,
                "KEY_SIM", "ACK", null, null, null, BenchEvidenceSource.Physical, "physical fixture"));
        }
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
