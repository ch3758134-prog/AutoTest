using System.Collections.Immutable;
using System.IO;
using AutoTest.Desktop.Testing;
using AutoTest.Projects.Project;
using AutoTest.Testing.Authoring;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// Creates a real PROJECT + TEST package for the operator to inspect and edit in
/// the existing main-window document tabs. No mock device responses or fake runs.
/// </summary>
public sealed partial class MainWindowViewModel
{
    private static StepNode AcceptanceStep(string type, string name, bool enabled = true)
    {
        var node = StepUiCatalog.NewNode(type) with { Name = name };
        return enabled ? node : node.WithProperty("enabled", "false");
    }

    private static TestDocument AcceptanceCase(TestDocument doc, int number)
    {
        var prepare = new List<StepNode>();
        var steps = new List<StepNode>();
        var cleanup = new List<StepNode>();

        if (number == 1)
        {
            prepare.Add(AcceptanceStep("Wait", "准备 · 等待工位稳定（200ms）")
                .WithProperty("milliseconds", "200"));
            prepare.Add(AcceptanceStep("SetVariable", "准备 · 初始化验收变量")
                .WithProperty("variable", "ui_acceptance")
                .WithProperty("value", "1"));

            var branch = AcceptanceStep("If", "测试 · IF/ELSE 条件分支")
                .WithProperty("variable", "ui_acceptance")
                .WithProperty("equals", "1")
                .WithSlot("then", AcceptanceStep("Wait", "THEN · 等待（100ms）")
                    .WithProperty("milliseconds", "100"))
                .WithSlot("else", AcceptanceStep("SetVariable", "ELSE · 标记备用路径")
                    .WithProperty("variable", "fallback")
                    .WithProperty("value", "1"));

            var loop = AcceptanceStep("Loop", "测试 · 循环两个步骤")
                .WithProperty("count", "2")
                .WithSlot("body",
                    AcceptanceStep("Wait", "循环体 · 等待（100ms）")
                        .WithProperty("milliseconds", "100"),
                    AcceptanceStep("SetVariable", "循环体 · 设置标志")
                        .WithProperty("variable", "loop_flag")
                        .WithProperty("value", "1"));

            steps.Add(branch);
            steps.Add(loop);
            steps.Add(AcceptanceStep("SetVariable", "测试 · 结束标志")
                .WithProperty("variable", "test_finished").WithProperty("value", "1"));
            cleanup.Add(AcceptanceStep("Wait", "清理 · 等待（100ms）")
                .WithProperty("milliseconds", "100"));
        }
        else if (number == 2)
        {
            prepare.Add(AcceptanceStep("Wait", "准备 · CAN 检查前等待")
                .WithProperty("milliseconds", "200"));
            steps.Add(AcceptanceStep("CanSend", "CAN 发送 · 需配置目标后手动启用", false));
            steps.Add(AcceptanceStep("AssertSignal", "DBC 信号判定 · 需绑定真实 DBC", false));
            steps.Add(AcceptanceStep("WaitUntil", "CAN 条件等待 · 需配置目标信号", false));
            steps.Add(AcceptanceStep("CanStopPeriodic", "停止周期 CAN · 未执行", false));
            cleanup.Add(AcceptanceStep("Wait", "清理 · 等待（100ms）")
                .WithProperty("milliseconds", "100"));
        }
        else
        {
            prepare.Add(AcceptanceStep("Wait", "准备 · 诊断前等待")
                .WithProperty("milliseconds", "200"));
            steps.Add(AcceptanceStep("UdsRequest", "UDS 请求 · 离线模板", false));
            steps.Add(AcceptanceStep("WaveStart", "波形采集 · 需配置真实信号", false));
            steps.Add(AcceptanceStep("WaveCompare", "波形判定 · 需采集与参考", false));
            steps.Add(AcceptanceStep("Flash", "ECU 烧录 · 默认禁止执行", false));
            cleanup.Add(AcceptanceStep("Wait", "清理 · 等待（100ms）")
                .WithProperty("milliseconds", "100"));
        }
        return doc with
        {
            Phases = doc.Phases
                .SetItem(TestPhase.Prepare, prepare.ToImmutableArray())
                .SetItem(TestPhase.Test, steps.ToImmutableArray())
                .SetItem(TestPhase.Cleanup, cleanup.ToImmutableArray())
        };
    }

    private async Task CreateAcceptanceSampleAsync()
    {
        if (IsBusy || _realTest.IsRunning || !await PrepareEditorsForCloseAsync()) return;

        await RunOperationAsync(async () =>
        {
            var parent = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "AutoTest", "UI验收工程");
            Directory.CreateDirectory(parent);
            var projectName = "UI_验收_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") +
                "_" + Guid.NewGuid().ToString("N")[..6];
            await _workspace.CreateAsync(projectName, parent);

            var projectAsset = await _workspace.CreateTestProjectAsync("自动测试界面验收");
            var caseSpecs = new[]
            {
                "01_准备测试清理与流程控制",
                "02_CAN发送与DBC信号编辑",
                "03_UDS波形与烧录步骤"
            };
            var entries = new List<TestProjectEntry>();
            var sampleAssets = new List<AutoTest.Core.AssetId>();
            for (var i = 0; i < caseSpecs.Length; i++)
            {
                var asset = await _workspace.CreateTestProgramAsync(caseSpecs[i]);
                var session = await _workspace.OpenTestEditorAsync(asset.Id);
                session = await _workspace.SaveTestEditorAsync(
                    session, AcceptanceCase(session.Document, i + 1));
                var updated = _workspace.Current!.Definition.Assets
                    .Single(x => x.Id == asset.Id);
                // Initially unchecked: no sample selects itself for physical execution.
                entries.Add(new TestProjectEntry(Guid.NewGuid(), updated.Id.Value,
                    updated.Revision, updated.Sha256, updated.Name, false));
                sampleAssets.Add(updated.Id);
            }

            var projectSession = await _workspace.OpenTestProjectEditorAsync(projectAsset.Id);
            var linked = new TestProjectDocument(projectSession.Document.SchemaVersion,
                projectSession.Document.Id, projectSession.Document.Name,
                entries);
            await _workspace.SaveTestProjectEditorAsync(projectSession, linked);

            var latestProject = _workspace.Current!.Definition.Assets
                .Single(x => x.Id == projectAsset.Id);
            var reloaded = await _workspace.OpenTestProjectEditorAsync(latestProject.Id);
            var projectEditor = new TestProjectWorkbenchViewModel(_workspace, _testDialogs,
                reloaded, _runSelections, _realTest, _hardware);
            BindProjectNavigation(latestProject, projectEditor);
            ActivateEditor("project|" + latestProject.Id,
                new TestProjectWorkbenchView(projectEditor),
                "测试项目 · " + latestProject.Name, projectEditor.ConfirmClose);

            var first = sampleAssets[0];
            var opened = await _workspace.OpenTestEditorAsync(first);
            var editor = new TestEditorViewModel(_workspace, _testDialogs, opened, _realTest);
            ActivateEditor("test|" + first, new TestEditorView(editor),
                "测试用例 · " + caseSpecs[0], editor.ConfirmClose);
            SelectAsset(first);

            StatusMessage = "验收工程已生成并在主窗口打开：" +
                _workspace.Current.ManifestPath +
                "。共有 3 个可编辑用例；硬件步骤默认禁用，运行选择默认未勾选。";
        });
    }
}
