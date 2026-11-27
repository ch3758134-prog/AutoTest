using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using AutoTest.Core;
using AutoTest.Desktop.Projects;
using AutoTest.Desktop.Testing;
using AutoTest.Desktop.Hardware;
using AutoTest.Projects.Resources;
using AutoTest.Testing.Authoring;
using AutoTest.Testing.Runtime;
using Prism.Commands;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// Real operator-initiated full-project engineering verification. It runs
/// FLASH once, then every configured test case using the existing physical
/// executor and RECORD engine. This is NOT a production release: there is no
/// physical START input and the individual REAL TEST records remain
/// Development mode. We never publish a production PASS from this path.
/// </summary>
public sealed partial class AutomaticTestHomeViewModel
{
    private RealTestWorkbenchService _operatorEngine = null!;
    private CancellationTokenSource? _operatorStop;
    private bool _operatorRunning;
    private string _operatorRunState = "尚未开始工程整组验证";
    private string _operatorCurrentCase = "—";
    private string _operatorCurrentStep = "—";
    private string _operatorRunEvidence = "";
    private string _operatorHtmlReportPath = "";
    public string OperatorHtmlReportPath
    {
        get => _operatorHtmlReportPath;
        private set => SetProperty(ref _operatorHtmlReportPath, value);
    }
    private string _operatorFlashState = "尚未开始";
    private string _operatorRecordState = "尚无工程验证记录";
    private string _operatorStepStatus = "未执行";
    private string _operatorPeriodicStatus = "当前没有已确认的项目周期 CAN 任务";
    public string OperatorPeriodicStatus
    {
        get => _operatorPeriodicStatus;
        private set => SetProperty(ref _operatorPeriodicStatus, value);
    }
    private string _operatorActual = "—";
    private string _operatorExpected = "—";
    private string _operatorFailure = "";
    private bool _engineeringVerificationArmed;
    // Only the explicit top-level "工程实测" entry allows a valid real COM scan
    // to open the existing physical-operation confirmation automatically.
    // A scan alone NEVER grants fixture/CAN/FLASH permissions.
    private bool _quickEngineeringStartOnScan;
    private int _operatorActiveCaseIndex = -1;
    private StepId? _operatorActiveStepId;
    private Guid _operatorRunId;
    private long _operatorEventTotal;
    private Task? _operatorExecutionTask;

    public DelegateCommand ArmOperatorVerificationCommand { get; private set; } = null!;
    public DelegateCommand StartOperatorVerificationCommand { get; private set; } = null!;
    public DelegateCommand StopOperatorVerificationCommand { get; private set; } = null!;
    public ObservableCollection<string> OperatorEvents { get; } = new();
    public bool IsOperatorRunning
    {
        get => _operatorRunning;
        private set
        {
            if (!SetProperty(ref _operatorRunning, value)) return;
            RaisePropertyChanged(nameof(StationReadiness));
            RaisePropertyChanged(nameof(ProductionHeadline));
            RaisePropertyChanged(nameof(ProductionGuidance));
            RaisePropertyChanged(nameof(ProductionDisplayStage));
            RefreshOperatorCommands();
            RaisePropertyChanged(nameof(EngineeringNextAction));
        }
    }
    public string OperatorRunState
    {
        get => _operatorRunState;
        private set
        {
            if (SetProperty(ref _operatorRunState, value))
                RaisePropertyChanged(nameof(ProductionDisplayStage));
        }
    }
    public string OperatorCurrentCase
    {
        get => _operatorCurrentCase;
        private set => SetProperty(ref _operatorCurrentCase, value);
    }
    public string OperatorCurrentStep
    {
        get => _operatorCurrentStep;
        private set => SetProperty(ref _operatorCurrentStep, value);
    }
    public string OperatorStepStatus { get => _operatorStepStatus; private set => SetProperty(ref _operatorStepStatus, value); }
    public string OperatorActual { get => _operatorActual; private set => SetProperty(ref _operatorActual, value); }
    public string OperatorExpected { get => _operatorExpected; private set => SetProperty(ref _operatorExpected, value); }
    public string OperatorFailure { get => _operatorFailure; private set => SetProperty(ref _operatorFailure, value); }

    public string OperatorRunEvidence
    {
        get => _operatorRunEvidence;
        private set => SetProperty(ref _operatorRunEvidence, value);
    }
    /// <summary>Physical FLASH stage state from the existing real operator run, never a simulated percentage.</summary>
    public string OperatorFlashState
    {
        get => _operatorFlashState;
        private set => SetProperty(ref _operatorFlashState, value);
    }
    public string OperatorRecordState
    {
        get => _operatorRecordState;
        private set => SetProperty(ref _operatorRecordState, value);
    }
    /// <summary>
    /// Engineer-only explicit arming of the existing full-project real
    /// verification. Production cannot expose its software START.
    /// A fresh real scan is required for every armed attempt.
    /// </summary>
    public void DisarmEngineeringVerification()
    {
        if (IsOperatorRunning) return;
        // Production must show the immutable authored TEST plan, NEVER the
        // latest engineering real-run colours/verdicts left in the shared
        // projection. Rebuild even if another callback already disarmed us.
        _engineeringVerificationArmed = false;
        _quickEngineeringStartOnScan = false;
        RebuildCases();
        InvalidateScan("工程测试已退出，重新开始时请扫码。", requiresAttention: false);
        RefreshOperatorCommands();
    }

    public void ArmEngineeringVerification(bool startWithExplicitConsentOnScan = false)
    {
        if (_isProductionWorkspace() || IsOperatorRunning) return;
        _engineeringVerificationArmed = true;
        _quickEngineeringStartOnScan = startWithExplicitConsentOnScan;
        InvalidateScan("请使用扫码枪扫描本台 ECU 编号。", requiresAttention: false);
        StartRequestStatus = startWithExplicitConsentOnScan
            ? "扫码核对成功后会显示夹具与烧录确认。"
            : "请扫码。确认编号后即可开始工程测试。";
        RefreshOperatorCommands();
    }

    public bool CanStartOperatorVerification =>
        !_operatorRunning && !_busy &&
        !_isProductionWorkspace() && _engineeringVerificationArmed &&
        _plans.Current is { ResourceReady: true } &&
        _prepared?.AcceptedScannerObservation is { } scan &&
        !string.IsNullOrWhiteSpace(scan.RawBarcode) &&
        _scanner.IsConnected &&
        _hardware.IsConnected && _hardware.IsEcuCanBitrateApplied &&
        _engineering.Current?.ManifestPath == _plans.Current.Project.ManifestPath &&
        !_operatorEngine.IsRunning;

    private void InitializeOperatorExecution(RealTestWorkbenchService engine)
    {
        _operatorEngine = engine;
        ArmOperatorVerificationCommand = new DelegateCommand(
            () => ArmEngineeringVerification(),
            () => !_isProductionWorkspace() && !IsOperatorRunning);
        StartOperatorVerificationCommand = new DelegateCommand(
            async () =>
            {
                _operatorExecutionTask = StartOperatorVerificationAsync();
                await _operatorExecutionTask;
            },
            () => CanStartOperatorVerification);
        StopOperatorVerificationCommand = new DelegateCommand(() =>
        {
            if (_operatorStop is null) return;
            AddOperatorEvent("操作员要求停止；FLASH 中断后 ECU 状态可能未知，禁止自动重试");
            OperatorRunState = "正在停止并执行工位收尾…";
            _operatorStop.Cancel();
            _operatorEngine.RequestStop();
        }, () => _operatorRunning);
        _operatorEngine.StepBegan += OnOperatorStepBegan;
        _operatorEngine.StepReported += OnOperatorStepCompleted;
    }

    private void RefreshOperatorCommands()
    {
        ArmOperatorVerificationCommand?.RaiseCanExecuteChanged();
        StartOperatorVerificationCommand?.RaiseCanExecuteChanged();
        StopOperatorVerificationCommand?.RaiseCanExecuteChanged();
        RaisePropertyChanged(nameof(CanStartOperatorVerification));
        RaisePropertyChanged(nameof(EngineeringNextAction));
    }

    private void OnOperatorStepBegan(StepId id, string name, TestPhase phase) =>
        OnUi(() =>
        {
            if (!IsOperatorRunning) return;
            _operatorActiveStepId = id;
            OperatorCurrentStep = name;
            OperatorStepStatus = "执行中";
            OperatorActual = "—";
            OperatorExpected = "—";
            OperatorFailure = "";
            ChangeStepState(id, "执行中");
            if (OperatorEvents.Count < 1000)
                AddOperatorEvent("步骤开始 · " + phase + " · " + name);
        });

    private static string ActualStepStateLabel(RealStepStatus status) => status switch
    {
        RealStepStatus.Completed => "执行完成（非单独 PASS）",
        RealStepStatus.Skipped => "已跳过",
        RealStepStatus.ConditionFalse => "条件不成立",
        RealStepStatus.NoData => "无数据",
        RealStepStatus.Timeout => "超时",
        RealStepStatus.InvalidData => "数据无效",
        RealStepStatus.DeviceError => "设备异常",
        RealStepStatus.Cancelled => "已取消",
        _ => "状态未知"
    };

    private void OnOperatorStepCompleted(RealStepFact fact) =>
        OnUi(() =>
        {
            if (!IsOperatorRunning) return;
            var state = ActualStepStateLabel(fact.Status);
            ChangeStepState(fact.Step, state);
            if (_operatorActiveStepId == fact.Step)
            {
                OperatorStepStatus = state;
                OperatorActual = string.IsNullOrWhiteSpace(fact.Actual) ? "—" : fact.Actual;
                OperatorExpected = string.IsNullOrWhiteSpace(fact.Expected) ? "—" : fact.Expected;
                OperatorFailure = fact.Status is RealStepStatus.Completed or
                    RealStepStatus.Skipped or RealStepStatus.ConditionFalse
                    ? "" : fact.Detail;
            }
            if (fact.Status is not RealStepStatus.Completed && OperatorEvents.Count < 1000)
                AddOperatorEvent("步骤 · " + fact.Type + " · " + fact.Status +
                    " · " + fact.Detail);
        });

    private void AddOperatorEvent(string line)
    {
        _operatorEventTotal++;
        OperatorEvents.Add(DateTime.Now.ToString("HH:mm:ss.fff") + "  " + line);
        while (OperatorEvents.Count > 250) OperatorEvents.RemoveAt(0);
    }

    private void ChangeCaseState(int index, string status)
    {
        if (index < 0 || index >= Cases.Count) return;
        Cases[index] = Cases[index] with { State = status };
    }

    private static AutomaticTestStep ResetStepProjection(AutomaticTestStep step) =>
        step with
        {
            State = step.State == "步骤已禁用（不执行）" ?
                step.State : "未执行",
            Children = step.Children.Select(ResetStepProjection).ToArray()
        };

    private static AutomaticTestStep UpdateStepProjection(AutomaticTestStep step,
        StepId id, string state)
    {
        // Keep unaffected tree nodes by reference. A large TEST with many
        // real step events must not rebuild every UI row per occurrence.
        var ownChanged = step.AuthoredStepId == id && step.State != state;
        List<AutomaticTestStep>? newChildren = null;
        for (var i = 0; i < step.Children.Count; i++)
        {
            var original = step.Children[i];
            var next = UpdateStepProjection(original, id, state);
            if (newChildren is null && !ReferenceEquals(original, next))
            {
                newChildren = new List<AutomaticTestStep>(step.Children.Count);
                for (var before = 0; before < i; before++)
                    newChildren.Add(step.Children[before]);
            }
            newChildren?.Add(next);
        }
        if (!ownChanged && newChildren is null) return step;
        return step with
        {
            State = ownChanged ? state : step.State,
            Children = newChildren is null ? step.Children : newChildren.ToArray()
        };
    }

    private void ChangeStepState(StepId id, string state)
    {
        if (_operatorActiveCaseIndex < 0 || _operatorActiveCaseIndex >= Cases.Count)
            return;
        var item = Cases[_operatorActiveCaseIndex];
        var children = item.Children.Select(x =>
            UpdateStepProjection(x, id, state)).ToArray();
        if (children.Length == item.Children.Count &&
            !children.Where((child, i) =>
                !ReferenceEquals(child, item.Children[i])).Any()) return;
        Cases[_operatorActiveCaseIndex] = item with { Children = children };
    }

    private static async Task SetAndCheckIgnRequestAsync(
        HardwareConnectionService hardware, bool on, CancellationToken ct)
    {
        await hardware.SendManualIgnAsync(on,
            on ? "AUTHORIZE IGN ON" : "AUTHORIZE IGN OFF", ct);
        if (await hardware.ReadIgnLevelAsync(ct) != on)
            throw new IOException("IGN USB 写入已应答，但主控 GET_IGN GPIO 回读" +
                "与请求不一致；不能继续 FLASH/TEST。该回读仍不是 ECU 端子电压。");
    }

    private sealed record RealOperatorCaseEvidence(string Name, Guid RunId,
        string Verdict, string ReportFile);
    private sealed record RealOperatorRunReceipt(Guid VerificationId, string Mode,
        DateTimeOffset StartedUtc, DateTimeOffset? EndedUtc,
        string DutSerial, Guid ScanObservation, string ScanRuleSha256,
        string ScannerPort, string ProjectPath, string PreparationFingerprint,
        string FirmwareSha256, string UsbIdentity, long UsbEpoch,
        string FlashState, IReadOnlyList<RealOperatorCaseEvidence> Cases,
        string Outcome, string? Error, IReadOnlyList<string> ObservedEvents,
        long EventCount, long EventsEvictedFromLiveView);

    private static void SaveOperatorReceipt(string root, RealOperatorRunReceipt receipt)
    {
        var folder = Path.Combine(root, "test-records", "operator-verifications");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, receipt.VerificationId.ToString("N") + ".json");
        var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(receipt,
                new JsonSerializerOptions { WriteIndented = true });
            using (var stream = new FileStream(temp, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 65536,
                FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            // Each revision replaces this attempt's last coherent snapshot;
            // a power loss leaves an incomplete/Running state, never PASS.
            File.Move(temp, file, overwrite: true);
            if (!System.Security.Cryptography.SHA256.HashData(
                    File.ReadAllBytes(file)).AsSpan().SequenceEqual(
                        System.Security.Cryptography.SHA256.HashData(bytes)))
                throw new IOException("本次工程实测汇总写入后 SHA256 回读不一致。");
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    // Human-readable projection of a REAL engineering verification receipt.
    // It is never a production Inspection or permission to release the DUT.
    private static string SaveOperatorHtmlReceipt(
        string root, RealOperatorRunReceipt receipt)
    {
        var folder = Path.Combine(root, "test-records", "operator-verifications");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, receipt.VerificationId.ToString("N") + ".html");
        Func<string, string> e = value =>
            System.Net.WebUtility.HtmlEncode(value) ?? "";
        var html = new System.Text.StringBuilder(4096);
        html.Append("""
<!DOCTYPE html><html lang="zh-CN"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>AutoTest 工程验证报告</title>
<style>
body{font:14px/1.65 "Microsoft YaHei","Segoe UI",sans-serif;margin:0;background:#f5f7f9;color:#22384b}
main{max-width:1050px;margin:30px auto;padding:28px 34px;background:white;border:1px solid #dfe8ef;border-radius:12px}
h1{font-size:24px;margin:0}h2{font-size:18px;margin-top:28px;border-bottom:1px solid #e2e9ef;padding-bottom:8px}
.muted,dt{color:#617588}.note{padding:12px;background:#fff5e8;border-left:4px solid #c78a39;margin:16px 0}
dl{display:grid;grid-template-columns:170px 1fr;gap:7px 15px}dd{margin:0;overflow-wrap:anywhere}
table{border-collapse:collapse;width:100%}th,td{padding:10px;border-bottom:1px solid #e4ecf2;text-align:left;vertical-align:top}
th{background:#f1f5f8}code{font:12px Consolas,monospace;overflow-wrap:anywhere}
pre{white-space:pre-wrap;overflow-wrap:anywhere;background:#f6f8fa;padding:12px;border:1px solid #e4ecf2}
</style></head><body><main>
<h1>ECU 整组工程验证报告</h1>
<div class="note"><strong>非正式生产记录。</strong> 本报告仅汇总真实工程验证事实，不构成出厂 PASS 或工位放行许可；没有实测的数据不作推断。</div>
""");
        void Pair(string label, string value) => html.Append("<dt>")
            .Append(e(label)).Append("</dt><dd>").Append(e(value)).Append("</dd>");
        html.Append("<h2>本台 ECU 与运行事实</h2><dl>");
        Pair("工程验证 ID", receipt.VerificationId.ToString("D"));
        Pair("ECU 编号", receipt.DutSerial);
        Pair("开始 UTC", receipt.StartedUtc.ToString("O"));
        Pair("结束 UTC", receipt.EndedUtc?.ToString("O") ?? "尚未结束");
        Pair("整组执行结果", receipt.Outcome);
        Pair("前置 FLASH", receipt.FlashState);
        Pair("固件 SHA256", receipt.FirmwareSha256);
        Pair("工程清单", receipt.ProjectPath);
        Pair("真实 USB 设备", receipt.UsbIdentity);
        Pair("USB 会话代次", receipt.UsbEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Pair("扫码端口", receipt.ScannerPort);
        Pair("扫码观察 ID", receipt.ScanObservation.ToString("D"));
        Pair("冻结资源指纹", receipt.PreparationFingerprint);
        html.Append("</dl>");
        if (!string.IsNullOrWhiteSpace(receipt.Error))
            html.Append("<div class=\"note\"><strong>异常/失败：</strong>")
                .Append(e(receipt.Error)).Append("</div>");
        html.Append("<h2>有序用例与独立结果</h2><p class=\"muted\">共记录 ")
            .Append(receipt.Cases.Count)
            .Append(" 个执行实例。逐步实测值和原始证据请查看每项对应的 RECORD 文件。</p>");
        html.Append("<table><thead><tr><th>序号</th><th>用例</th><th>判定</th><th>RunId</th><th>证据文件</th></tr></thead><tbody>");
        for (var i = 0; i < receipt.Cases.Count; i++)
        {
            var item = receipt.Cases[i];
            html.Append("<tr><td>").Append(i + 1).Append("</td><td>")
                .Append(e(item.Name)).Append("</td><td>").Append(e(item.Verdict))
                .Append("</td><td><code>").Append(e(item.RunId.ToString("D")))
                .Append("</code></td><td><code>")
                .Append(e(string.IsNullOrWhiteSpace(item.ReportFile)
                    ? "无已确认的证据文件路径" : item.ReportFile))
                .Append("</code></td></tr>");
        }
        html.Append("</tbody></table><h2>真实运行事件</h2><p class=\"muted\">总计 ")
            .Append(receipt.EventCount).Append(" 条，实时列表裁剪 ")
            .Append(receipt.EventsEvictedFromLiveView)
            .Append(" 条；下方只显示收据中保留的事件。</p><pre>");
        foreach (var item in receipt.ObservedEvents)
            html.Append(e(item)).Append('\n');
        html.Append("</pre><p class=\"muted\">本 HTML 与原始 JSON 位于同一目录。工程验证记录不参与生产合格品统计。</p></main></body></html>");
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(tmp, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false)))
            {
                writer.Write(html.ToString());
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            File.Move(tmp, path, overwrite: true);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
        return path;
    }

    private async Task StartOperatorVerificationAsync()
    {
        if (!CanStartOperatorVerification || _prepared?.AcceptedScannerObservation is not { } scan ||
            _plans.Current is not { } plan || _engineering.Current is not { } opened)
        {
            StartRequestStatus = "暂不能开始测试，请检查工程、设备连接和 ECU 编号。";
            return;
        }
        // Engineering verification uses the saved station device setup;
        // formal production has a separate physical START gate.
        StationFixtureExecutionPlan fixturePlan;
        try
        {
            if (string.IsNullOrEmpty(scan.EngineeringFixtureSha256))
                throw new InvalidDataException(
                    "本次扫码没有工程调试的工位设备快照，请重新扫码。");
            var draft = await StationDeviceConfigurationStore.LoadAsync();
            draft.Fixture.Validate();
            if (!string.Equals(FingerprintEngineeringFixture(draft.Fixture),
                    scan.EngineeringFixtureSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "扫码后工位夹具设置已变化，请重新扫码。");
            fixturePlan = StationFixtureExecutionPlan.From(draft.Fixture);
        }
        catch (Exception error)
        {
            StartRequestStatus = "不能开始工程测试：" + error.Message;
            return;
        }
        var fixtureDescription = fixturePlan.Press.Count == 0
            ? "本次不自动控制夹具，请确认 ECU 已正确固定。"
            : "自动压紧：" + string.Join(" → ", fixturePlan.Press
                .Select(x => "夹具 " + x.Clamp)) +
              "；测试结束后将请求释放。" +
              "压紧后等待 ECU CAN 通信就绪反馈；这不是机械位置传感器。";
        // One operation-specific confirmation. The physical interlocks,
        // project identity checks and execution authority are unchanged.
        if (MessageBox.Show(
                "ECU 编号：" + scan.RawBarcode + "\n" +
                "测试用例：" + plan.Cases.Count + " 项\n" +
                "夹具：" + fixtureDescription + "\n\n" +
                "即将擦写 ECU 固件并执行整套测试。" +
                "请确认接线正确、夹具运动范围内无人，" +
                "且已现场核对机械状态。\n\n是否开始？",
                "开始工程测试", MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        // The modal physical-operation confirmation pumps the WPF dispatcher.
        // An asynchronous scanner observation or project switch must not
        // replace the frozen DUT/fixture identity while that dialog is open.
        if (_prepared?.AcceptedScannerObservation?.ObservationId != scan.ObservationId ||
            _plans.Current?.Project.ManifestSha256 != plan.Project.ManifestSha256 ||
            !_scanner.IsConnected ||
            !string.Equals(_scanner.ConnectedPort, scan.ScannerPort,
                StringComparison.OrdinalIgnoreCase))
        {
            StartRequestStatus = "确认过程中扫码身份、工程或设备发生变化；" +
                "操作已取消，请重新扫码。";
            return;
        }

        // The modal confirmation may process other UI events. A changed
        // station device configuration invalidates the original consent.
        try
        {
            var latest = await StationDeviceConfigurationStore.LoadAsync();
            latest.Fixture.Validate();
            if (!string.Equals(FingerprintEngineeringFixture(latest.Fixture),
                    scan.EngineeringFixtureSha256, StringComparison.OrdinalIgnoreCase))
            {
                StartRequestStatus = "工位设置已改变，请重新扫码后确认。";
                return;
            }
        }
        catch (Exception error)
        {
            StartRequestStatus = "工位设置无法核对，已取消：" + error.Message;
            return;
        }

        var frozen = _prepared!;
        var sessionEpoch = _hardware.CurrentStationEpoch;
        var started = DateTimeOffset.UtcNow;
        var results = new List<RealOperatorCaseEvidence>();
        var flashState = "未执行";
        var stationOperationsMayHaveStarted = false;
        var fixtureScopeGranted = false;
        var fixtureActuationAttempted = false;
        var fixturePressComplete = false;
        var flashAttemptStarted = false;
        // Execution facts are independent of localized display messages.
        // These flags preserve the existing release and cancellation gates.
        var completedNormally = false;
        var periodicSessionClosed = false;
        var operatorCancelled = false;
        var outcome = "未完成";
        string? failure = null;
        _operatorRunId = Guid.NewGuid();
        _operatorStop = new CancellationTokenSource();
        var ct = _operatorStop.Token;
        OperatorEvents.Clear();
        _operatorEventTotal = 0;
        OperatorHtmlReportPath = "";
        OperatorRunEvidence = "记录号 " + _operatorRunId.ToString("N");
        OperatorFlashState = "尚未开始";
        OperatorPeriodicStatus = "准备整组共享 CAN 发送会话";
        OperatorRecordState = "准备记录工程验证事实";
        OperatorCurrentStep = "—";
        OperatorCurrentCase = "—";
        OperatorStepStatus = "未执行";
        OperatorActual = OperatorExpected = "—";
        OperatorFailure = "";
        _operatorActiveCaseIndex = -1;
        _operatorActiveStepId = null;
        IsOperatorRunning = true;
        for (var i = 0; i < Cases.Count; i++)
            Cases[i] = Cases[i] with
            {
                State = "未执行",
                Children = Cases[i].Children.Select(ResetStepProjection).ToArray()
            };
        int activeCaseIndex = -1;

        RealOperatorRunReceipt CurrentReceipt() => new(
                _operatorRunId, "ENGINEERING_VERIFICATION_NOT_PRODUCTION_RELEASE",
                started, outcome == "执行中" ? null : DateTimeOffset.UtcNow,
                scan.RawBarcode, scan.ObservationId, scan.RuleSha256,
                scan.ScannerPort, opened.ManifestPath, frozen.PreparationFingerprint,
                frozen.FirmwareSha256, _hardware.ConnectedDeviceIdentity ?? "UNKNOWN",
                sessionEpoch, flashState, results.ToArray(), outcome, failure,
                OperatorEvents.ToArray(), _operatorEventTotal,
                Math.Max(0, _operatorEventTotal - OperatorEvents.Count));
        void Snapshot() => SaveOperatorReceipt(opened.RootDirectory, CurrentReceipt());

        void RequireFrozenCanRate()
        {
            ct.ThrowIfCancellationRequested();
            if (!_hardware.IsConnected ||
                _hardware.CurrentStationEpoch != sessionEpoch ||
                !_hardware.IsEcuCanBitrateApplied ||
                _hardware.RequestedEcuCanBitrateKbps != frozen.EcuCanKbps ||
                _hardware.ConfirmedEcuCanBitrateKbps != frozen.EcuCanKbps)
                throw new IOException("ECU CAN 主控会话或波特率不再符合本次冻结的 " +
                    frozen.EcuCanKbps + " kbit/s；停止本次真实执行，不能使用旧授权。");
        }

        try
        {
            outcome = "执行中";
            Snapshot();
            OperatorRecordState = "已创建工程验证过程记录";
            AddOperatorEvent("软件 START 已明确确认；本次结果不会作为生产放行");
            // Engineering verification is tied only to the actual physical
            // scanner observation and the station fixture snapshot. Product
            // matching belongs exclusively to the production catalog.
            var currentDraft = await StationDeviceConfigurationStore.LoadAsync(ct);
            currentDraft.Fixture.Validate();
            if (!string.Equals(FingerprintEngineeringFixture(currentDraft.Fixture),
                    scan.EngineeringFixtureSha256, StringComparison.OrdinalIgnoreCase) ||
                scan.Source != FrozenScanSource.EngineeringRaw ||
                !string.Equals(scan.RawBarcode, _lastObservedBarcode,
                    StringComparison.Ordinal) ||
                frozen.ProjectId != plan.Project.Definition.Id.Value)
                throw new InvalidDataException(
                    "工程扫码身份或工位夹具设置已改变，不能复用原物理许可。");
            if (!string.IsNullOrEmpty(scan.ScannerPnpInstanceId))
            {
                // A real scan observation cannot be replayed after a physical
                // scanner replacement on the same COM name.
                var actualScanner = await Task.Run(() =>
                {
                    var bound = StationScannerDeviceBinding.Load();
                    return bound is null ? null : StationScannerDeviceBinding.Match(bound);
                }, ct);
                if (actualScanner is null ||
                    !string.Equals(actualScanner.PortName, scan.ScannerPort,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(actualScanner.PnpInstanceId, scan.ScannerPnpInstanceId,
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        "本次扫码来源的物理 USB 扫码枪已变化，不能沿用旧冻结身份。");
            }
            // Only this engineer-only, explicitly armed software verification
            // may deliberately use the saved draft rather than a published
            // production release. Production preflight defaults to published.
            var validated = await ProductionPreparationService.PrepareAsync(
                plan.Project, scan.RecognitionMarker, scan.RawBarcode, ct,
                requireProductionDeployment: false);
            if (!validated.ConfigurationReady ||
                validated.Preparation?.PreparationFingerprint != frozen.PreparationFingerprint)
                throw new InvalidDataException(
                    "真实运行前工程或烧录资源已变化：" + validated.Explanation);
            if (!_scanner.IsConnected ||
                _scanner.ConnectedPort != scan.ScannerPort ||
                !_hardware.IsConnected || _hardware.CurrentStationEpoch != sessionEpoch ||
                _hardware.ConfirmedEcuCanBitrateKbps != frozen.EcuCanKbps)
                throw new InvalidOperationException(
                    "扫描设备、主控连接或 ECU CAN 波特率在预检后发生变化。");

            var programs = await AutomaticTestResourceContract.ResolveAsync(plan.Project, ct);
            programs.RequireExecutable();
            if (programs.Cases.Count != frozen.OrderedCases.Count ||
                programs.Cases.Where((x, i) =>
                    x.Entry.ItemId != frozen.OrderedCases[i].EntryId ||
                    x.Asset.Id.Value != frozen.OrderedCases[i].TestAssetId ||
                    x.Asset.Revision != frozen.OrderedCases[i].Revision ||
                    !string.Equals(x.ContentSha256,
                        frozen.OrderedCases[i].Sha256,
                        StringComparison.OrdinalIgnoreCase))
                    .Any())
                throw new InvalidDataException("本次正式工程用例顺序已改变。");

            // Wave profile approval, algorithm revision and reference closure
            // must fail BEFORE ANY clamp/IGN/FLASH physical write, not when
            // the first WaveCompare step happens after ECU erasure.
            await _operatorEngine.VerifyProjectWaveProfilesBeforeHardwareAsync(
                programs.OrderedExecutions().Select(x => x.Program).ToArray(), ct);
            AddOperatorEvent("WAVE 前置核验完成：所调用用例的比较算法、已批准参数及参考波形可用");

            if (_hardware.IsManualCanBatchApproved || _hardware.IsManualKeyActionApproved)
                throw new InvalidOperationException("手动任务仍占用工位；请先停止。");

            // The resource and equipment checks above are read-only. Do not
            // send fixture cleanup commands if an inspection was rejected
            // before any physical command was even attempted.
            // Only station-owned fixture plan may actuate clamps. A product
            // TEST may contain its own IGN steps, but no project-defined
            // station preparation or release script can run here.
            stationOperationsMayHaveStarted = true;
            // Subscribe before pressing; accept only an ECU frame received after all clamp commands.
            using var fixtureCanFeedback = fixturePlan.Press.Count != 0
                ? new EcuCanFixtureFeedback(_hardware)
                : null;
            if (fixturePlan.Press.Count != 0)
            {
                RequireFrozenCanRate();
                await _hardware.AuthorizeStationFixtureRunAsync(
                    frozen.PreparationFingerprint, fixturePlan.AllowedOutputMask,
                    false, ct);
                fixtureScopeGranted = true;
                OperatorRunState = "正在压紧夹具";
                foreach (var action in fixturePlan.Press)
                {
                    ct.ThrowIfCancellationRequested();
                    fixtureActuationAttempted = true;
                    using var movement = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    movement.CancelAfter(fixturePlan.MovementTimeoutMs);
                    AddOperatorEvent("夹具" + action.Clamp + " 请求压紧 · 输出 " +
                        (fixturePlan.OutputLevelHigh(action) ? "HIGH" : "LOW") +
                        " · USB 反馈限时 " + fixturePlan.MovementTimeoutMs + "ms");
                    Snapshot(); // durable intent before energized output command
                    var bits = await _hardware.SendStationFixtureAsync(
                        action.Clamp, fixturePlan.OutputLevelHigh(action),
                        frozen.PreparationFingerprint, movement.Token,
                        logicalPress: true);
                    AddOperatorEvent("夹具" + action.Clamp + " MCU GPIO 回读 0x" +
                        bits.ToString("X2") + "；机械位置未验证（非生产到位证据）");
                    Snapshot();
                    if (fixturePlan.SettleDelayMs > 0)
                    {
                        AddOperatorEvent("装夹稳定等待 " + fixturePlan.SettleDelayMs +
                            "ms（仅已配置延时，不代表机械到位）");
                        await Task.Delay(fixturePlan.SettleDelayMs, ct);
                    }
                }
                fixturePressComplete = true;
                fixtureCanFeedback!.ArmAfterPress();
                OperatorRunState = "等待 ECU CAN 确认夹具通信就绪";
                AddOperatorEvent("夹具已请求压紧，等待压紧后新收到的 ECU CAN 报文");
                var verifiedFrame = await fixtureCanFeedback.WaitAsync(
                    TimeSpan.FromSeconds(10), ct);
                RequireFrozenCanRate();
                AddOperatorEvent("夹具功能就绪 · ECU CAN 0x" +
                    verifiedFrame.Frame.Id.ToString("X") +
                    " · " + verifiedFrame.Time.Utc.ToString("O") +
                    " · 这是通信反馈，不是机械传感器或防护联锁");
                Snapshot();
            }
            else AddOperatorEvent("工位未选择夹具输出，本次不执行自动装夹。");
            OperatorRunState = "正在准备测试环境";
            RequireFrozenCanRate();

            OperatorRunState = "正在烧录 ECU";
            // Record possible destructive activity before issuing the real job.
            flashAttemptStarted = true;
            flashState = "烧录已启动 · 后续若中断 ECU 状态可能未知";
            OperatorFlashState = flashState;
            Snapshot();
            AddOperatorEvent("FLASH 开始 · HEX/S19=" + frozen.FirmwareSha256[..12]);
            var flash = await _hardware.ProgramTestFlashAsync(frozen.Job,
                frozen.Job.Consent, ct);
            if (!flash.ResetAcknowledged || !flash.AppSessionObserved ||
                flash.TransferAcknowledged != frozen.Job.PlannedBytes ||
                flash.VerifiedBytes != frozen.Job.PlannedBytes)
                throw new InvalidDataException("FLASH 未完成 APP 复位与传输验证：" +
                    flash.Outcome);
            flashState = "烧录完成 · APP 诊断会话已确认（非固件版本验收）";
            flashAttemptStarted = false;
            OperatorFlashState = flashState;
            Snapshot();
            AddOperatorEvent(flashState);

            // All ordered invocations use one physical TEST scope and preserve
            // cross-case periodic CAN tasks; each case retains its own evidence.
            var anyFunctionalFail = false;
            await using (var projectScope = await _operatorEngine.OpenProjectSessionAsync(
                programs.OrderedExecutions().Select(x => x.Program).ToArray(),
                _operatorRunId, scan.RawBarcode, ct))
            {
            AddOperatorEvent("连续整组 TEST 会话：按工程顺序执行全部用例，周期 CAN 跨用例保持。");
            for (var i = 0; i < programs.Cases.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                RequireFrozenCanRate();
                var item = programs.Cases[i];
                activeCaseIndex = i;
                _operatorActiveCaseIndex = i;
                _operatorActiveStepId = null;
                OperatorCurrentCase = (i + 1) + "/" + programs.Cases.Count +
                    " · " + item.Entry.DisplayName;
                OperatorCurrentStep = "正在准备...";
                OperatorRunState = "正在测试 · " + OperatorCurrentCase;
                ChangeCaseState(i, "执行中");
                AddOperatorEvent("用例开始 · " + item.Entry.DisplayName);
                var prepared = _operatorEngine.Prepare(item.Program);
                if (!prepared.Diagnostics.IsEmpty)
                    throw new InvalidDataException("用例编译诊断：" +
                        string.Join("；", prepared.Diagnostics));
                // Scope was authorized once, with every permitted CAN ID and
                // action frozen BEFORE the first case began.
                var run = await projectScope.ExecuteCaseAsync(item.Program, ct);
                // Close the event/UI dispatch race using the executor's
                // immutable final facts. UI callbacks can be queued when the
                // engine finishes on a non-UI thread; never lose the last
                // real step solely because the run has already transitioned.
                foreach (var fact in run.Facts)
                    ChangeStepState(fact.Step, ActualStepStateLabel(fact.Status));
                var lastMeasured = run.Facts.LastOrDefault(fact =>
                    !string.IsNullOrWhiteSpace(fact.Actual) ||
                    !string.IsNullOrWhiteSpace(fact.Expected));
                if (lastMeasured is not null)
                {
                    OperatorStepStatus = ActualStepStateLabel(lastMeasured.Status);
                    OperatorActual = string.IsNullOrWhiteSpace(lastMeasured.Actual)
                        ? "—" : lastMeasured.Actual;
                    OperatorExpected = string.IsNullOrWhiteSpace(lastMeasured.Expected)
                        ? "—" : lastMeasured.Expected;
                    if (lastMeasured.Status is not (RealStepStatus.Completed or
                        RealStepStatus.Skipped or RealStepStatus.ConditionFalse))
                        OperatorFailure = lastMeasured.Detail;
                }
                results.Add(new RealOperatorCaseEvidence(item.Entry.DisplayName,
                    run.Run.Value, run.Verdict.ToString(), run.EvidencePath ?? ""));
                var caseState = run.Verdict switch
                {
                    RealTestVerdict.Passed => "实测通过",
                    RealTestVerdict.CompletedNoAssertion => "动作完成 · 无功能判定",
                    RealTestVerdict.Failed => "功能判定失败",
                    RealTestVerdict.Inconclusive => "数据不足 · 未得结论",
                    RealTestVerdict.Aborted => "已停止",
                    _ => "执行异常"
                };
                ChangeCaseState(i, caseState);
                _operatorActiveStepId = null;
                Snapshot();
                AddOperatorEvent("用例完成 · " + item.Entry.DisplayName +
                    " · " + run.Verdict + " · Record " + run.Run.Value.ToString("N")[..8]);
                if (run.Verdict == RealTestVerdict.Failed)
                {
                    // A failed PREPARE gate or failed scripted CLEANUP is a
                    // prerequisite/physical-state fault, not a normal ECU
                    // functional FAIL that can safely be carried forward.
                    if (run.Facts.Any(f => f.Type == "PrepareGate" ||
                        f.Phase is TestPhase.Prepare or TestPhase.Cleanup &&
                        f.Status is not (RealStepStatus.Completed or RealStepStatus.Skipped)))
                        throw new InvalidDataException("用例准备或收尾失败，不允许继续下一台架动作：" +
                            item.Entry.DisplayName + "；请检查对应 StepOccurrence。");
                    // A functional FAIL remains in its own RECORD and in the
                    // overall verdict, but does not automatically tear down a
                    // healthy VCU environment needed by subsequent diagnostics.
                    anyFunctionalFail = true;
                    AddOperatorEvent("功能 FAIL 已记录；继续执行后续已冻结用例，" +
                        "不会将本项目最终结果覆盖为 PASS。");
                }
                else if (run.Verdict is not (RealTestVerdict.Passed or
                    RealTestVerdict.CompletedNoAssertion))
                    throw new InvalidDataException("用例因数据无效、硬件故障或 STOP 未能安全继续：" +
                        item.Entry.DisplayName + " · " + run.Verdict);
                // Periodic senders are still owned by projectScope here.
                await projectScope.Actions.CheckHealthAsync(ct);
                var ongoing = projectScope.PeriodicTasks
                    .Where(x => x.StoppedUtc is null).ToArray();
                var stoppedInCase = projectScope.LastCaseStoppedPeriodic;
                OperatorPeriodicStatus = ongoing.Length == 0
                    ? "当前没有活动的周期 CAN 任务"
                    : "持续发送 " + ongoing.Length + " 条 · " +
                      string.Join("；", ongoing.Take(4).Select(x =>
                          "0x" + x.CanId.ToString("X") + " / " + x.PeriodMs +
                          " ms · USB ACK " + x.UsbAcks));
                if (stoppedInCase.Count > 0)
                {
                    var stoppedText = string.Join("；", stoppedInCase.Take(6).Select(x =>
                        "0x" + x.CanId.ToString("X") + " " + x.StopReason +
                        "（源用例 " + x.SourceCaseId + "）"));
                    OperatorPeriodicStatus += " · 本用例停止 " + stoppedInCase.Count + " 条";
                    AddOperatorEvent("周期 CAN 停止原因 · " + stoppedText +
                        "；如果它是下一个用例需要的环境报文，请检查 TEST 的" +
                        "持续时长、CanStopPeriodic 和运行入口。");
                }
                AddOperatorEvent("项目周期 CAN · " + OperatorPeriodicStatus +
                    "（ACK 不是 ECU 接收证据）");
            }
            projectScope.MarkCompleted();
            } // One project-level disposal stops senders/revokes the fence.
            completedNormally = true;
            periodicSessionClosed = true;
            OperatorPeriodicStatus = "测试已结束，周期 CAN 发送已停止";
            outcome = anyFunctionalFail
                ? "工程整组完成 · 存在功能 FAIL（不构成生产放行）"
                : "工程整组真实实测完成（不构成生产放行）";
            OperatorRunState = anyFunctionalFail
                ? "测试完成 · 部分用例失败" : "测试完成";
            AddOperatorEvent(outcome);
        }
        catch (OperationCanceledException)
        {
            operatorCancelled = true;
            if (flashAttemptStarted)
                OperatorFlashState = "烧录中断，ECU 状态待现场核查";
            outcome = "操作员停止；实体状态待核查";
            failure = "测试已取消；如曾进入烧录，须重新核实 ECU 状态。";
            OperatorRunState = "已停止，设备状态待核查";
            AddOperatorEvent(failure);
        }
        catch (Exception error)
        {
            if (flashAttemptStarted)
                OperatorFlashState = "烧录未验证完成，ECU 状态未知";
            outcome = "真实执行中断";
            failure = error.Message;
            OperatorRunState = "测试中断";
            AddOperatorEvent("执行失败 · " + error.Message);
        }
        finally
        {
            if (stationOperationsMayHaveStarted && !periodicSessionClosed)
                OperatorPeriodicStatus = "运行已中断 · 周期 CAN 及物理授权交由项目清理（详情见证据）";
            try
            {
                if (stationOperationsMayHaveStarted)
                {
                    OperatorRunState += " · 工位收尾";
                    // No project-owned station cleanup is allowed. Controlled
                    // release below uses only the pinned station fixture plan.
                    if (fixtureScopeGranted && fixturePressComplete &&
                        completedNormally && failure is null && !_hardware.IsTestRunApproved &&
                        _hardware.IsConnected &&
                        _hardware.CurrentStationEpoch == sessionEpoch)
                    {
                        // ReleasePolicy requires durable evidence BEFORE
                        // releasing an actuator. This is an engineer-approved
                        // verification, not a claim of sensor-observed safety.
                        Snapshot();
                        AddOperatorEvent("完整工程实测已归档；无活动 TEST/FLASH 授权，" +
                            "按工程师本次授权执行受控夹具释放。");
                        foreach (var action in fixturePlan.Release)
                        {
                            AddOperatorEvent("夹具" + action.Clamp +
                                " 释放请求即将发出，记录已持久化。");
                            Snapshot();
                            using var movement = new CancellationTokenSource(
                                TimeSpan.FromMilliseconds(fixturePlan.MovementTimeoutMs));
                            var bits = await _hardware.SendStationFixtureAsync(
                                action.Clamp, fixturePlan.OutputLevelHigh(action),
                                frozen.PreparationFingerprint, movement.Token,
                                logicalPress: false);
                            AddOperatorEvent("夹具" + action.Clamp + " 请求释放并读回 GPIO 0x" +
                                bits.ToString("X2") + "；实际机械释放仍未测量。");
                            Snapshot();
                            if (fixturePlan.SettleDelayMs > 0)
                                await Task.Delay(fixturePlan.SettleDelayMs); // GPIO timeout does not bound pneumatic settling
                        }
                        OperatorRunState += " · 已请求释放夹具";
                    }
                    else if (fixtureActuationAttempted)
                        AddOperatorEvent("本次装夹/FLASH/TEST/记录未满足自动释放条件，" +
                            "不擅自松夹；机械状态 UNKNOWN，须现场核查并受控处置。");
                }
                else
                    AddOperatorEvent("预检失败发生在物理动作之前；未向夹具/IGN 发送收尾指令");
            }
            catch (Exception error)
            {
                outcome = "收尾异常 · 不可作为合格结果";
                failure = (failure is null ? "" : failure + "；") +
                    "收尾动作失败：" + error.Message;
                OperatorRunState = outcome;
                AddOperatorEvent(failure);
            }
            if (fixtureScopeGranted)
            {
                try { await _hardware.RevokeStationFixtureRunAsync(frozen.PreparationFingerprint); }
                catch (Exception error)
                {
                    outcome = "夹具授权撤销异常 · 工位状态待核查";
                    failure = (failure is null ? "" : failure + "；") +
                        "工位夹具权限撤销失败：" + error.Message;
                    AddOperatorEvent(failure);
                }
            }
            AddOperatorEvent("KEY 输出状态：" + _hardware.KeyRequestStatus +
                "；测试结束/停止没有自动发送释放帧");
            if (_operatorActiveStepId is { } unfinished &&
                OperatorStepStatus == "执行中")
            {
                ChangeStepState(unfinished, "中断，步骤结果未知");
                OperatorStepStatus = "中断，步骤结果未知";
            }
            if (activeCaseIndex >= 0 && activeCaseIndex < Cases.Count &&
                Cases[activeCaseIndex].State == "执行中")
                ChangeCaseState(activeCaseIndex, operatorCancelled
                    ? "已停止" : "执行异常");
            var jsonSaved = false;
            try
            {
                Snapshot();
                jsonSaved = true;
                var htmlReport = SaveOperatorHtmlReceipt(opened.RootDirectory, CurrentReceipt());
                OperatorHtmlReportPath = htmlReport;
                OperatorRecordState = "工程测试记录与报告已保存";
                OperatorRunEvidence = "工程验证报告：" + htmlReport +
                    "\n真实原始记录：" +
                    Path.Combine(opened.RootDirectory, "test-records",
                        "operator-verifications", _operatorRunId.ToString("N") + ".json");
            }
            catch (Exception error)
            {
                if (jsonSaved)
                {
                    OperatorRecordState = "测试记录已保存，但报告生成失败：" +
                        error.Message;
                    OperatorRunEvidence = "原始 JSON：" +
                        Path.Combine(opened.RootDirectory, "test-records",
                            "operator-verifications", _operatorRunId.ToString("N") + ".json");
                    AddOperatorEvent("工程报告生成失败；原始记录仍可查看：" + error.Message);
                }
                else
                {
                    OperatorRunState = "记录保存失败；结果不可放行";
                    OperatorRecordState = "测试记录保存失败：" + error.Message;
                    AddOperatorEvent("本次真实运行汇总未能归档：" + error.Message);
                }
            }
            _operatorStop?.Dispose();
            _operatorStop = null;
            _engineeringVerificationArmed = false;
            _operatorActiveCaseIndex = -1;
            _operatorActiveStepId = null;
            IsOperatorRunning = false;
            RefreshOperatorCommands();
            StartRequestStatus = OperatorRunState;
        }
    }

    public async Task StopOperatorForShutdownAsync()
    {
        _operatorStop?.Cancel();
        _operatorEngine.RequestStop();
        if (_operatorExecutionTask is { IsCompleted: false } running)
            await running.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private void DisposeOperatorExecution()
    {
        _operatorStop?.Cancel();
        _operatorEngine.RequestStop();
        _operatorEngine.StepBegan -= OnOperatorStepBegan;
        _operatorEngine.StepReported -= OnOperatorStepCompleted;
    }
}
