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
        RebuildCases();
        InvalidateScan("已返回自动测试；开发验证扫码身份已失效，请重新扫码。");
        RefreshOperatorCommands();
    }

    public void ArmEngineeringVerification()
    {
        if (_isProductionWorkspace() || IsOperatorRunning) return;
        _engineeringVerificationArmed = true;
        InvalidateScan("工程整组验证已准备：请用工位串口扫码枪扫描本台 ECU。");
        StartRequestStatus = "工程整组验证待扫码；验证资源后由工程师再次明确启动。";
        RefreshOperatorCommands();
    }

    public bool CanStartOperatorVerification =>
        !_operatorRunning && !_busy &&
        !_isProductionWorkspace() && _engineeringVerificationArmed &&
        _plans.Current is { ResourceReady: true } &&
        _prepared?.AcceptedScannerObservation is { } scan &&
        !string.IsNullOrWhiteSpace(scan.DutSerial) &&
        _scanner.IsConnected &&
        _hardware.IsConnected && _hardware.IsEcuCanBitrateApplied &&
        _engineering.Current?.ManifestPath == _plans.Current.Project.ManifestPath &&
        !_operatorEngine.IsRunning;

    private void InitializeOperatorExecution(RealTestWorkbenchService engine)
    {
        _operatorEngine = engine;
        ArmOperatorVerificationCommand = new DelegateCommand(
            ArmEngineeringVerification,
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

    private static async Task ApplyStationActionsAsync(
        IReadOnlyList<InspectionStationAction> actions,
        HardwareConnectionService hardware, CancellationToken ct,
        Action<string>? observe = null)
    {
        foreach (var action in actions)
        {
            ct.ThrowIfCancellationRequested();
            switch (action.Kind)
            {
                case "Wait": break;
                case "Clamp1Press":
                    await SetAndCheckClampRequestAsync(hardware, 1, true, ct);
                    break;
                case "Clamp1Release":
                    await SetAndCheckClampRequestAsync(hardware, 1, false, ct);
                    break;
                case "Clamp2Press":
                    await SetAndCheckClampRequestAsync(hardware, 2, true, ct);
                    break;
                case "Clamp2Release":
                    await SetAndCheckClampRequestAsync(hardware, 2, false, ct);
                    break;
                case "IgnOn":
                    await SetAndCheckIgnRequestAsync(hardware, true, ct);
                    break;
                case "IgnOff":
                    await SetAndCheckIgnRequestAsync(hardware, false, ct);
                    break;
                default:
                    throw new InvalidDataException("未识别的工位动作：" + action.Kind);
            }
            if (action.WaitAfterMilliseconds > 0)
                await Task.Delay(action.WaitAfterMilliseconds, ct);
            observe?.Invoke(action.Kind == "Wait"
                ? "工位纯等待 " + action.WaitAfterMilliseconds + "ms 已结束"
                : "工位动作 " + action.Kind + "：USB ACK 与主控 GPIO 回读一致。" +
                  "实际夹具到位或 IGN 负载电压仍未测量。");
        }
    }

    /// <summary>
    /// USB ACK alone does not establish the MCU output request state. Require
    /// a fresh GPIO readback after every configured fixture request. This is
    /// still NOT mechanical clamp position feedback; production physical
    /// fixture approval remains blocked without real sensor observations.
    /// </summary>
    private static async Task SetAndCheckClampRequestAsync(
        HardwareConnectionService hardware, byte clamp, bool press,
        CancellationToken ct)
    {
        await hardware.SendManualClampAsync(clamp, press,
            "AUTHORIZE CLAMP " + clamp + (press ? " PRESS" : " RELEASE"), ct);
        var bits = await hardware.ReadClampLevelsAsync(ct);
        if (((bits >> (clamp - 1)) & 1) != (press ? 1 : 0))
            throw new IOException("夹具" + clamp + " USB 写入已应答，但主控 GET_CLAMPS GPIO 回读" +
                "与请求不一致；不能继续 FLASH/TEST。该回读仍不是机械到位证据。");
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

    private async Task StartOperatorVerificationAsync()
    {
        if (!CanStartOperatorVerification || _prepared?.AcceptedScannerObservation is not { } scan ||
            _plans.Current is not { } plan || _engineering.Current is not { } opened)
        {
            StartRequestStatus = "未满足真实整组实测准备条件；请检查工程、主控、CAN 码率及扫码身份。";
            return;
        }
        if (MessageBox.Show(
                "本次真实整组实测将执行工程工位动作、擦写 ECU 固件，并依次运行全部 " +
                plan.Cases.Count + " 个测试用例。\n\n" +
                "ECU 编号：" + scan.DutSerial + "\n固件 SHA256：" +
                _prepared.FirmwareSha256[..Math.Min(16, _prepared.FirmwareSha256.Length)] +
                "…\n\n当前主控尚无正式实体 START 输入，本功能是软件确认的工程实测，" +
                "不会写入正式生产 PASS 或生产统计。是否明确开始真实物理操作？",
                "确认真实工位与 ECU 烧录操作", MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        var frozen = _prepared!;
        var sessionEpoch = _hardware.CurrentStationEpoch;
        var started = DateTimeOffset.UtcNow;
        var results = new List<RealOperatorCaseEvidence>();
        var flashState = "未执行";
        var stationOperationsMayHaveStarted = false;
        var outcome = "未完成";
        string? failure = null;
        _operatorRunId = Guid.NewGuid();
        _operatorStop = new CancellationTokenSource();
        var ct = _operatorStop.Token;
        OperatorEvents.Clear();
        _operatorEventTotal = 0;
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

        void Snapshot()
        {
            SaveOperatorReceipt(opened.RootDirectory, new RealOperatorRunReceipt(
                _operatorRunId, "ENGINEERING_VERIFICATION_NOT_PRODUCTION_RELEASE",
                started, outcome == "执行中" ? null : DateTimeOffset.UtcNow,
                scan.DutSerial, scan.ObservationId, scan.RuleSha256,
                scan.ScannerPort, opened.ManifestPath, frozen.PreparationFingerprint,
                frozen.FirmwareSha256, _hardware.ConnectedDeviceIdentity ?? "UNKNOWN",
                sessionEpoch, flashState, results.ToArray(), outcome, failure,
                OperatorEvents.ToArray(), _operatorEventTotal,
                Math.Max(0, _operatorEventTotal - OperatorEvents.Count)));
        }

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
            // Any published code/product change invalidates the accepted
            // identity BEFORE the first fixture, FLASH, or TEST physical write.
            var currentPublication = await StationProductionConfigurationStore.LoadPublishedAsync(ct)
                ?? throw new InvalidDataException("当前工位正式编码发布已撤销。");
            if (currentPublication.PublicationId != scan.PublicationId ||
                !string.Equals(currentPublication.ConfigurationSha256,
                    scan.PublishedConfigurationSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("扫码后生产发布配置已被修改；必须重新扫码。");
            var rechecked = ProductionIdentityResolver.Resolve(
                currentPublication, scan.RawBarcode);
            if (!rechecked.Accepted || rechecked.RuleId != scan.BarcodeRuleId ||
                rechecked.RuleRevision != scan.BarcodeRuleRevision ||
                rechecked.Model != scan.ModelCode || rechecked.Serial != scan.DutSerial ||
                rechecked.Hardware != (scan.HardwareCode ?? "") ||
                rechecked.Project!.ProjectId != frozen.ProjectId)
                throw new InvalidDataException("扫码身份的原始规则、型号或已发布工程已改变。");
            await StationProductionConfigurationStore.VerifyProductAsync(rechecked.Project, ct);
            // Only this engineer-only, explicitly armed software verification
            // may deliberately use the saved draft rather than a published
            // production release. Production preflight defaults to published.
            var validated = await ProductionPreparationService.PrepareAsync(
                plan.Project, scan.ModelCode, scan.DutSerial, ct,
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
            if (programs.Cases.Count != frozen.OrderedEnabledCases.Count ||
                programs.Cases.Where((x, i) =>
                    x.Entry.ItemId != frozen.OrderedEnabledCases[i].EntryId ||
                    x.Asset.Id.Value != frozen.OrderedEnabledCases[i].TestAssetId ||
                    x.Asset.Revision != frozen.OrderedEnabledCases[i].Revision ||
                    !string.Equals(x.ContentSha256,
                        frozen.OrderedEnabledCases[i].Sha256,
                        StringComparison.OrdinalIgnoreCase))
                    .Any())
                throw new InvalidDataException("本次正式工程用例顺序已改变。");
            if (_hardware.IsManualCanBatchApproved || _hardware.IsManualKeyActionApproved)
                throw new InvalidOperationException("手动任务仍占用工位；请先停止。");

            // The resource and equipment checks above are read-only. Do not
            // send fixture cleanup commands if an inspection was rejected
            // before any physical command was even attempted.
            stationOperationsMayHaveStarted = true;
            OperatorRunState = "准备工位 · 执行工程已保存的夹具/IGN 动作";
            await ApplyStationActionsAsync(
                frozen.Fixture?.BeforeStart ?? [], _hardware, ct,
                AddOperatorEvent);
            AddOperatorEvent("工位前置动作 USB ACK；夹具实际到位仍需真实传感器验证");
            RequireFrozenCanRate();

            OperatorRunState = "前置烧录 · 正在擦写 ECU";
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
            OperatorFlashState = flashState;
            Snapshot();
            AddOperatorEvent(flashState);

            // All ordered TEST programs share one physical CAN/KEY/IGN grant.
            // Each case still gets its own RunRecord, StepOccurrence and verdict.
            var anyFunctionalFail = false;
            await using (var projectScope = await _operatorEngine.OpenProjectSessionAsync(
                programs.Cases.Select(x => x.Program).ToArray(), _operatorRunId,
                scan.DutSerial, ct))
            {
            AddOperatorEvent("连续整组 TEST 硬件会话已建立：周期 CAN 跨用例保持；" +
                "用例 Cleanup 仅执行显式编写的步骤。");
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
                OperatorRunState = "真实测试 · " + OperatorCurrentCase;
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
                OperatorPeriodicStatus = ongoing.Length == 0
                    ? "当前没有活动的周期 CAN 任务"
                    : "持续发送 " + ongoing.Length + " 条 · " +
                      string.Join("；", ongoing.Take(4).Select(x =>
                          "0x" + x.CanId.ToString("X") + " / " + x.PeriodMs +
                          " ms · USB ACK " + x.UsbAcks));
                AddOperatorEvent("项目周期 CAN · " + OperatorPeriodicStatus +
                    "（ACK 不是 ECU 接收证据）");
            }
            projectScope.MarkCompleted();
            } // One project-level disposal stops senders/revokes the fence.
            OperatorPeriodicStatus = "整组结束：周期 CAN 发送已停止，物理授权已撤销";
            outcome = anyFunctionalFail
                ? "工程整组完成 · 存在功能 FAIL（不构成生产放行）"
                : "工程整组真实实测完成（不构成生产放行）";
            OperatorRunState = outcome;
            AddOperatorEvent(outcome);
        }
        catch (OperationCanceledException)
        {
            if (flashState.StartsWith("烧录已启动", StringComparison.Ordinal))
                OperatorFlashState = "烧录中断，ECU 状态待现场核查";
            outcome = "操作员停止；实体状态待核查";
            failure = "真实执行已取消；如曾进入烧录，须重新核实 ECU 状态。";
            OperatorRunState = outcome;
            AddOperatorEvent(failure);
        }
        catch (Exception error)
        {
            if (flashState.StartsWith("烧录已启动", StringComparison.Ordinal))
                OperatorFlashState = "烧录未验证完成，ECU 状态未知";
            outcome = "真实执行中断";
            failure = error.Message;
            OperatorRunState = outcome;
            AddOperatorEvent("执行失败 · " + error.Message);
        }
        finally
        {
            if (stationOperationsMayHaveStarted &&
                !OperatorPeriodicStatus.StartsWith("整组结束", StringComparison.Ordinal))
                OperatorPeriodicStatus = "运行已中断 · 周期 CAN 及物理授权交由项目清理（详情见证据）";
            try
            {
                if (stationOperationsMayHaveStarted)
                {
                    OperatorRunState += " · 工位收尾";
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                    await ApplyStationActionsAsync(
                        frozen.Fixture?.AfterFinish ?? [], _hardware, cleanup.Token,
                    AddOperatorEvent);
                    AddOperatorEvent("工程配置的后置工位动作已发送；现场实际状态仍需确认");
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
                ChangeCaseState(activeCaseIndex, outcome == "操作员停止；实体状态待核查"
                    ? "已停止" : "执行异常");
            try
            {
                Snapshot();
                OperatorRecordState = "工程验证过程记录已保存（非生产结案）";
                OperatorRunEvidence = "真实运行记录：" +
                    Path.Combine(opened.RootDirectory, "test-records",
                        "operator-verifications", _operatorRunId.ToString("N") + ".json");
            }
            catch (Exception error)
            {
                OperatorRunState = "记录保存失败；结果不可放行";
                OperatorRecordState = "记录保存失败：" + error.Message;
                AddOperatorEvent("本次真实运行汇总未能归档：" + error.Message);
            }
            _operatorStop?.Dispose();
            _operatorStop = null;
            _engineeringVerificationArmed = false;
            _operatorActiveCaseIndex = -1;
            _operatorActiveStepId = null;
            IsOperatorRunning = false;
            RefreshOperatorCommands();
            StartRequestStatus = OperatorRunState + "；正式生产放行仍需实体 START 与生产 RECORD 集成。";
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
