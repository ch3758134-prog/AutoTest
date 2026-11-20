using System.Globalization;
using System.Windows;
using AutoTest.Specialists.Uds.Workbench;

namespace AutoTest.UdsLab;

/// <summary>
/// A bounded, serialized UDS-only console sequence. This is not a second
/// TEST DSL or a generic execution engine. Every service call uses the shared
/// UdsClient + ISO-TP through real DEVICE, with frame-by-frame human approval.
/// </summary>
public partial class MainWindow
{
    private async void RunStep_Click(object sender, RoutedEventArgs e)
    {
        if (_loadedStep is null) return;
        await RunCaseStepsAsync(_steps.IndexOf(_loadedStep), oneStep: true,
            restartVariables: false);
    }

    private async void RunNext_Click(object sender, RoutedEventArgs e)
    {
        int next = Math.Clamp(_nextStepIndex, 0, _steps.Count);
        if (next >= _steps.Count)
        {
            CaseRunStatus.Text = "已到达用例末尾。可选中步骤重新执行，或运行全部步骤。";
            return;
        }
        await RunCaseStepsAsync(next, oneStep: true, restartVariables: false);
    }

    private async void RunFrom_Click(object sender, RoutedEventArgs e)
    {
        int from = Math.Max(0, StepsList.SelectedIndex);
        await RunCaseStepsAsync(from, oneStep: false, restartVariables: true);
    }

    private async void RunCase_Click(object sender, RoutedEventArgs e)
    {
        await RunCaseStepsAsync(0, oneStep: false, restartVariables: true);
    }

    private async Task RunCaseStepsAsync(int start, bool oneStep, bool restartVariables)
    {
        if (_caseRunning || _pendingCancel is not null || _pendingSend is not null) return;
        if (_loadedCase is null || _hardware?.IsReady != true || _needsReconnect)
        {
            MessageBox.Show(this,
                _needsReconnect ? "诊断链路状态未知，请先断开并重连。"
                : "必须先连接真实主控板并配置 ECU 的请求/响应 CAN ID。");
            return;
        }
        // Running a draft must NEVER silently save or overwrite its
        // persisted service definition. Only "保存步骤"/Ctrl+S does that.
        SyncCaseSteps();
        if (start < 0 || start >= _steps.Count)
        {
            MessageBox.Show(this, "当前用例没有可以执行的步骤。");
            return;
        }
        var executionCase = _loadedCase;
        // Freeze all step parameters at invocation. Later editor changes never
        // mutate a running request or any earlier real ECU exchange.
        DiagnosticCaseStep[] snapshot = _steps.Select(t => t.Clone(false)).ToArray();
        if (_editorDirty && _loadedStep is not null)
        {
            try
            {
                int editedIndex = _steps.IndexOf(_loadedStep);
                if (editedIndex >= 0) snapshot[editedIndex] = CaptureCurrentStepDraft();
                CaseRunStatus.Text = "使用当前未保存的步骤草稿执行；用例文件不会被自动修改。";
            }
            catch (Exception ex)
            {
                ShowError("当前步骤参数有误，未执行用例", ex);
                return;
            }
        }
        if (restartVariables) ResetCaseVariables();
        if (restartVariables)
            foreach (var original in _steps) original.LastState = "未运行";
        _nextStepIndex = start;

        _pendingCancel = new CancellationTokenSource();
        _caseRunning = true;
        RefreshButtons();
        int planned = oneStep ? 1 : snapshot.Length - start;
        AppendAudit("CASE_RUN_START", executionCase.Id + " " + executionCase.Name +
            " from=" + start + " count=" + planned);
        _pendingSend = RunFrozenStepsAsync(executionCase, snapshot, start, oneStep,
            _pendingCancel.Token);
        try { await _pendingSend; }
        catch (Exception ex)
        {
            // This should catch unexpected UI lifecycle failures. Per-service
            // transport errors are recorded by SendCurrentAsync.
            AppendAudit("CASE_RUN_FAULT", ex.ToString());
            CaseRunStatus.Text = "用例异常终止：" + ex.Message;
        }
        finally
        {
            _pendingSend = null;
            _pendingCancel?.Dispose();
            _pendingCancel = null;
            _caseRunning = false;
            RefreshButtons();
        }
    }

    private async Task RunFrozenStepsAsync(DiagnosticCase diagnosticCase,
        DiagnosticCaseStep[] snapshot, int start, bool oneStep, CancellationToken ct)
    {
        int attempted = 0, completed = 0;
        for (int index = start; index < snapshot.Length; index++)
        {
            var step = snapshot[index];
            var original = _steps.FirstOrDefault(x => x.Id == step.Id);
            // Never reuse a value from an older execution of this step if it
            // is retried, disabled, or fails in the current attempt.
            if (step.CaptureAs.Length > 0)
            {
                _caseVariables.Remove(step.CaptureAs);
                RefreshCaseVariables();
            }
            if (!step.Enabled)
            {
                if (original is not null) original.LastState = "已跳过（未勾选）";
                _nextStepIndex = index + 1;
                if (oneStep) break;
                continue;
            }
            if (ct.IsCancellationRequested) break;
            _nextStepIndex = index;
            if (!oneStep && index > start && step.PauseBefore)
            {
                if (original is not null) original.LastState = "等待人工继续";
                CaseRunStatus.Text = "已暂停在步骤 " + (index + 1) + " · " + step.Name +
                    "。检查/编辑后，可点击“执行下一步”。";
                AppendAudit("CASE_PAUSE", "case=" + diagnosticCase.Id + " step=" + step.Id);
                break;
            }
            attempted++;
            string header = "步骤 " + (index + 1).ToString(CultureInfo.InvariantCulture) +
                "/" + snapshot.Length + " · " + step.Name;
            if (original is not null) original.LastState = "正在请求 ECU…";
            CaseRunStatus.Text = header + " · 正在执行（每帧仍需授权）";
            DiagnosticAction actual = step.Action.Duplicate();
            actual.Name = step.Name; // duplicate creates a new immutable action snapshot
            byte[] pdu;
            try
            {
                DiagnosticCaseVariables.Validate(step);
                pdu = DiagnosticCaseVariables.Resolve(step, _caseVariables);
                actual.RequestHex = UdsHex.Format(pdu);
                _ = ValidateReadPdu(actual);
            }
            catch (Exception ex)
            {
                if (original is not null) original.LastState = "执行前检查失败";
                CaseRunStatus.Text = header + " · 请求未发送：" + ex.Message;
                AppendAudit("CASE_PREFLIGHT_REJECT", header + " " + ex.Message);
                break;
            }
            // A real UDS request; no synthetic response or simulated transport.
            var result = await SendCurrentAsync(actual, pdu, ct,
                diagnosticCase.Id, step.Id, diagnosticCase.Name);
            if (original is not null) original.LastState = result.Status;
            if (result.Status != "ECU 正响应")
            {
                CaseRunStatus.Text = header + " · " + result.Status +
                    "。后续步骤已停止；没有将错误响应作为可用数据。";
                AppendAudit("CASE_STOP", header + " " + result.Status);
                break;
            }
            if (!string.IsNullOrWhiteSpace(step.CaptureAs))
            {
                try
                {
                    var response = UdsHex.Parse(result.Response);
                    byte[] value = DiagnosticCaseVariables.Extract(step, response);
                    _caseVariables[step.CaptureAs] = value;
                    RefreshCaseVariables();
                    AppendAudit("CASE_CAPTURE", step.CaptureAs + "=" + UdsHex.Format(value) +
                        " 来源=" + result.CorrelationId);
                }
                catch (Exception ex)
                {
                    if (original is not null) original.LastState = "正响应已获得，但提取变量失败";
                    CaseRunStatus.Text = header + " · " + ex.Message;
                    AppendAudit("CASE_CAPTURE_FAILED", ex.ToString());
                    break;
                }
            }
            completed++;
            _nextStepIndex = index + 1;
            CaseRunStatus.Text = header + " · 已获得 ECU 正响应。已完成 " +
                completed + " 步。";
            if (oneStep) break;
        }
        if (ct.IsCancellationRequested)
            CaseRunStatus.Text = "用户已终止请求或等待；ECU 是否停止执行仍未知。";
        else if (_nextStepIndex >= snapshot.Length && attempted == completed)
            CaseRunStatus.Text = "本次诊断用例结束 · " + completed +
                " 个服务步骤获得 ECU 正响应（未自动执行未勾选步骤）。";
        AppendAudit("CASE_RUN_END", diagnosticCase.Id +
            " attempted=" + attempted + " confirmed=" + completed +
            " nextIndex=" + _nextStepIndex);
    }
}
