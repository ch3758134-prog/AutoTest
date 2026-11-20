namespace AutoTest.TestEditorPreview;

/// <summary>
/// Synthetic design-only content from V02-02: 12 cases, 40 steps in case 03.
/// No CAN IDs, DBC identity, physical run facts, or real ECU credentials are asserted.
/// </summary>
public static class SampleData
{
    public static EditorDocument Create()
    {
        var names = new[]
        {
            "工位连接与身份", "供电条件检查", "外部输入与状态响应",
            "CAN 周期发送验证", "CAN 信号变化判定", "UDS 诊断会话",
            "诊断参数读取", "输入动作时序", "波形参考采集",
            "波形判定", "异常路径恢复", "清理与结束条件"
        };
        var document = new EditorDocument();
        for (var i = 0; i < names.Length; i++)
        {
            var t = new EditorCase { Name = names[i], RunSelected = i is 0 or 1 or 3 or 7 or 9 or 11 };
            foreach (var phase in new[] { "准备", "测试", "清理" })
                t.Phases.Add(StepCatalog.Phase(phase));
            document.Cases.Add(t);
        }
        var c = document.Cases[2];
        var p = c.Phases[0];
        var test = c.Phases[1];
        var cleanup = c.Phases[2];

        static EditorNode Add(EditorNode parent, string code, string kind, string summary, string? text = null)
        {
            var step = StepCatalog.New(kind, code, summary);
            if (text is not null) step.Title = text;
            parent.Children.Add(step);
            step.Parent = parent;
            return step;
        }
        static EditorNode Branch(EditorNode parent, string title) =>
            parent.Children.Single(n => n.Role == NodeRole.Branch && n.Title == title);
        static void Set(EditorNode step, string key, string value)
        {
            var field = step.Field(key);
            if (field is not null) field.Text = value;
        }

        Add(p, "P01", "日志记录", "测试开始：记下 UI 草稿编号");
        Add(p, "P02", "设置变量", "retryCount = 0");
        var p3 = Add(p, "P03", "UDS 请求", "10 03 · 目标待配置");
        Set(p3, "request", "10 03");
        Add(p, "P04", "CAN 单次发送", "Demo_NetworkConfig · 数据占位");
        Add(p, "P05", "等待", "300 ms");
        Add(p, "P06", "调用公共流程", "公共准备流程 / 固定修订待选择");

        Add(test, "T01", "CAN 周期发送", "Demo_Alive · 100 ms");
        var when = Add(test, "T02", "WHEN", "Demo_Status.Ready == 1 · 2000 ms");
        Set(when, "condition", "Demo_Status.Ready == 1");
        Add(Branch(when, "满足条件"), "T03", "设置变量", "readySeen = true");
        Add(Branch(when, "满足条件"), "T04", "日志记录", "检测到 Ready");
        Add(Branch(when, "超时分支"), "T05", "日志记录", "Ready 超时");
        Add(Branch(when, "超时分支"), "T06", "期望判定", "readySeen == true");

        var condition = Add(test, "T07", "IF / ELSE", "Demo_Status.Mode == 2");
        Set(condition, "condition", "Demo_Status.Mode == 2");
        Add(Branch(condition, "THEN"), "T08", "模拟外部输入", "Demo_InputAction · 300 ms");
        var canProblem = Add(Branch(condition, "THEN"), "T09", "CAN 信号判定", "Demo_Status.Output == 1 · 缺少 DBC 信号映射");
        Set(canProblem, "signal", "");
        Add(Branch(condition, "ELSE"), "T10", "UDS 请求", "22 F1 90");
        Add(Branch(condition, "ELSE"), "T11", "等待", "100 ms");

        var choice = Add(test, "T12", "SWITCH", "Demo_Status.ResultCode");
        Set(choice, "condition", "Demo_Status.ResultCode");
        Add(Branch(choice, "CASE 0"), "T13", "日志记录", "结果代码为 0");
        var disabled = Add(Branch(choice, "CASE 1"), "T14", "等待", "50 ms · 已禁用");
        disabled.IsEnabled = false;
        Set(disabled, "ms", "50");
        Add(Branch(choice, "DEFAULT"), "T15", "期望判定", "默认分支结果代码检查");

        var loop = Add(test, "T16", "循环 LOOP", "循环 3 次 · i");
        Add(Branch(loop, "BODY"), "T17", "CAN 单次发送", "Demo_Command");
        var invalidTime = Add(Branch(loop, "BODY"), "T18", "等待", "20x · 无效编辑缓冲");
        Set(invalidTime, "ms", "20x");
        Add(Branch(loop, "BODY"), "T19", "CAN 信号读取", "Demo_Status.Counter → counter");

        var exception = Add(test, "T20", "TRY / CATCH", "设备通信异常处理");
        Add(Branch(exception, "TRY"), "T21", "UDS 请求", "19 02 FF");
        Add(Branch(exception, "TRY"), "T22", "WHEN", "Demo_Diagnostic.Done == true");
        Add(Branch(exception, "CATCH"), "T23", "日志记录", "诊断失败原因");
        Add(Branch(exception, "FINALLY"), "T24", "停止周期发送", "Demo_Alive");

        var breakpoint = Add(test, "T25", "UDS 请求", "22 F1 90 · 断点");
        breakpoint.Breakpoint = true;
        Add(test, "T26", "波形开始", "Demo_MotorCurrent");
        Add(test, "T27", "等待", "250 ms");
        Add(test, "T28", "波形停止", "Demo_MotorCurrent");
        var missingWave = Add(test, "T29", "波形判定", "Demo_CurrentReference · 缺少参考资源");
        Set(missingWave, "reference", "");

        Add(cleanup, "C01", "停止周期发送", "Demo_Alive");
        Add(cleanup, "C02", "模拟外部输入", "Demo_NoInput · 恢复到预定义状态");
        Add(cleanup, "C03", "IGN 控制", "OFF · 仅 UI 配置");
        Add(cleanup, "C04", "等待", "100 ms");
        Add(cleanup, "C05", "日志记录", "测试结束 · 非运行结果");

        p.IsExpanded = false; // Start by showing a complete high-level structure rather than 40 lines at once.
        test.IsExpanded = true;
        when.IsExpanded = false;
        choice.IsExpanded = false;
        loop.IsExpanded = false;
        exception.IsExpanded = false;

        // Three deliberate design-only errors: T09, T18, T29.
        document.ActiveCaseId = c.Id;
        document.ActiveNodeId = canProblem.Id;
        document.RepairParents();
        return document;
    }
}
