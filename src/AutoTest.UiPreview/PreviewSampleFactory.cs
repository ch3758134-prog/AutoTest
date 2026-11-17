namespace AutoTest.UiPreview;

/// <summary>
/// Frozen HTML sample fixtures for visual review only.
/// Never used as a production TEST document, device simulation or runtime result.
/// </summary>
public static class PreviewSampleFactory
{
    public static IReadOnlyList<PaletteKind> MakePalette() =>
    [
        new("can","发送 CAN 报文","CAN 与数据","⇄"),
        new("canwait","等待 CAN 报文","CAN 与数据","◷"),
        new("read","读取信号","CAN 与数据","↧"),
        new("expect","检查条件","CAN 与数据","✓"),
        new("input","按键动作","工位操作","◈"),
        new("ign","IGN 控制","工位操作","◈"),
        new("sol","夹具动作","工位操作","◈"),
        new("uds","UDS 服务","UDS 服务","◇"),
        new("wave","采集信号","波形分析","〰"),
        new("wavecheck","对比波形参考","波形分析","≋"),
        new("wait","固定等待","流程与计算","◷"),
        new("var","设置变量","流程与计算","𝑥"),
        new("if","IF / ELSE","流程与计算","⑂"),
        new("switch","SWITCH / CASE","流程与计算","⑂"),
        new("when","WHEN 等待","流程与计算","◷"),
        new("loop","LOOP 循环","流程与计算","⟳"),
        new("try","TRY / CATCH","流程与计算","⚑"),
        new("sub","调用子程序","流程与计算","↳"),
        new("comment","备注","流程与计算","·"),
    ];

    public static DemoStep CreateStep(string kind,string name,params (string Key,string Value)[] args)
    {
        var result = new DemoStep { Kind = kind,Name = name };
        foreach (var (key,value) in args) result.Parameters[key] = value;
        if (kind == "if")
        {
            result.Branches.Add(new DemoBranch("THEN"));
            result.Branches.Add(new DemoBranch("ELSE"));
        }
        if (kind == "switch")
        {
            result.Branches.Add(new DemoBranch("CASE"));
            result.Branches.Add(new DemoBranch("DEFAULT"));
        }
        if (kind == "loop") result.Branches.Add(new DemoBranch("BODY"));
        if (kind == "try")
        {
            result.Branches.Add(new DemoBranch("TRY"));
            result.Branches.Add(new DemoBranch("CATCH"));
            result.Branches.Add(new DemoBranch("FINALLY"));
        }
        return result;
    }

    public static IReadOnlyList<DemoPhase> CreatePhases()
    {
        var prepare = new DemoPhase("prepare","准备");
        prepare.Steps.Add(CreateStep("ign","设置点火输入",("state","ON")));
        prepare.Steps.Add(CreateStep("can","发送运行条件",("message","未选择报文"),("signalCount","2"),("period","100")));
        prepare.Steps.Add(CreateStep("wait","等待上电稳定",("ms","200")));
        var test = new DemoPhase("test","测试");
        test.Steps.Add(CreateStep("read","读取当前状态",("signal","Signal_A"),("variable","$actual")));
        test.Steps.Add(CreateStep("expect","检查状态值",("expression","$actual >= 0")));
        var branch = CreateStep("if","根据读取值分支",("condition","$actual > 2"));
        branch.Branches[0].Steps.Add(CreateStep("wave","采集信号",("signal","Signal_A"),("duration","1200")));
        branch.Branches[0].Steps.Add(CreateStep("wavecheck","对比参考记录",("reference","参考记录01")));
        branch.Branches[1].Steps.Add(CreateStep("uds","读取诊断信息",("job","读取标识信息")));
        test.Steps.Add(branch);
        test.Steps.Add(CreateStep("canwait","等待目标帧",("message","未选择报文"),("timeout","2000")));
        var cleanup = new DemoPhase("cleanup","清理");
        cleanup.Steps.Add(CreateStep("ign","关闭点火输入",("state","OFF")));
        return [prepare,test,cleanup];
    }


}
