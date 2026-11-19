using System.Collections.ObjectModel;
using System.Windows;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace AutoTest.UiPreview;

/// <summary>In-memory presentation data only. Never serialized and never executed.</summary>
public sealed class DemoStep : INotifyPropertyChanged
{
    private string _name = "";
    private bool _enabled = true;
    private bool _breakpoint;
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Kind { get; init; } = "wait";
    public string Name { get => _name; set { _name = value; Changed(); Changed(nameof(Summary)); } }
    public bool Enabled { get => _enabled; set { _enabled = value; Changed(); Changed(nameof(State)); } }
    public bool Breakpoint { get => _breakpoint; set { _breakpoint = value; Changed(); Changed(nameof(State)); } }
    public Dictionary<string,string> Parameters { get; } = new(StringComparer.Ordinal);
    public ObservableCollection<DemoBranch> Branches { get; } = new();
    public string Summary => Kind switch
    {
        "ign" => "设置 " + Param("state", "ON"),
        "can" => Param("message", "未选择报文") + " · " + Param("signalCount", "0") + " 信号 · " + Param("period", "0") + " ms",
        "canwait" => Param("message", "未选择报文") + " · 超时 " + Param("timeout", "1000") + " ms",
        "wait" => Param("ms", "200") + " ms",
        "read" => Param("signal", "Signal_A") + " → " + Param("variable", "$actual"),
        "expect" => Param("expression", "$actual >= 0"),
        "if" => "IF " + Param("condition", "$actual > 2"),
        "loop" => "重复 " + Param("count", "3") + " 次",
        "wave" => "信号 " + Param("signal", "Signal_A") + " · " + Param("duration", "1200") + " ms",
        "wavecheck" => "参考 " + Param("reference", "参考记录01"),
        "uds" => Param("job", "读取标识信息"),
        "input" => Param("button", "未选择按键动作"),
        "when" => "等待条件 " + Param("condition", "$ready"),
        "switch" => "SWITCH " + Param("expression", "$state"),
        "comment" => Param("text", "备注"),
        "var" => Param("variable", "$value") + " = " + Param("expression", "0"),
        "try" => "异常时：" + Param("onError", "停止并记录"),
        "sub" => "调用 " + Param("target", "未选择子程序"),
        "sol" => Param("state", "CLAMP") == "CLAMP" ? "请求压紧" : "请求打开",
        _ => Param("text", "待配置")
    };
    public string Resource => Kind switch
    {
        "ign" or "sol" => "工位能力",
        "can" or "canwait" => "DBC",
        "input" => "KEY 动作",
        "wave" or "wavecheck" => "波形资源",
        "uds" => "UDS 服务",
        _ => "—"
    };
    public string State => Enabled ? "待执行" : "禁用";
    public string Param(string key, string fallback = "") =>
        Parameters.TryGetValue(key, out var value) ? value : fallback;
    public void SetParam(string key,string value) { Parameters[key] = value; Changed(nameof(Summary)); }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    public DemoStep Copy()
    {
        var copy = new DemoStep { Kind = Kind, Name = Name + " · 副本", Enabled = Enabled, Breakpoint = Breakpoint };
        foreach (var pair in Parameters) copy.Parameters.Add(pair.Key,pair.Value);
        foreach (var branch in Branches)
        {
            var newBranch = new DemoBranch(branch.Name);
            foreach (var step in branch.Steps) newBranch.Steps.Add(step.Copy());
            copy.Branches.Add(newBranch);
        }
        return copy;
    }
}
public sealed class DemoBranch(string name)
{
    public string Name { get; } = name;
    public ObservableCollection<DemoStep> Steps { get; } = new();
}
public sealed class DemoPhase(string key,string title)
{
    public string Key { get; } = key;
    public string Title { get; } = title;
    public bool Collapsed { get; set; }
    public ObservableCollection<DemoStep> Steps { get; } = new();
}
public sealed class PreviewRow : INotifyPropertyChanged
{
    public DemoPhase Phase { get; init; } = null!;
    public DemoBranch? Branch { get; init; }
    public DemoStep? Step { get; init; }
    public DemoStep? Owner { get; init; }
    public int Index { get; init; }
    public int Depth { get; init; }
    public bool IsPhase => Step is null && Branch is null;
    public bool IsBranch => Branch is not null;
    public bool IsStep => Step is not null;
    public int RowHeight => IsPhase ? 34 : IsBranch ? 26 : 35;
    public string Name => IsPhase ? Phase.Title : IsBranch ? "└─ " + Branch!.Name : Step!.Name;
    public string Summary => Step?.Summary ?? "";
    public string Resource => Step?.Resource ?? "";
    public string Status => Step?.State ?? "";
    public string BreakpointMark => Step?.Breakpoint == true ? "●" : "";
    public string CountLabel => IsPhase ? Phase.Steps.Count + " 步" : "";
    public string ExpandMark => Phase.Collapsed ? "▸" : "▾";
    public string Number => IsStep ? (Index+1).ToString() : "";
    public bool IsMatch { get; set; }
    public Thickness Indent => new(8+Depth*18,0,0,0);
    public string Icon => Step?.Kind switch
    {
        "ign" or "input" or "sol" => "◈",
        "can" or "canwait" => "⇄",
        "read" => "↧",
        "expect" => "✓",
        "wait" or "when" => "◷",
        "if" or "switch" => "⑂",
        "loop" => "⟳",
        "wave" or "wavecheck" => "〰",
        "uds" => "◇",
        _ => "·"
    };
    public Brush PhaseBackground => Solid(Phase.Key switch
    { "prepare" => "#EAF2FC", "test" => "#EAF7F3", _ => "#FFF6E9" });
    public Brush PhaseAccent => Solid(Phase.Key switch
    { "prepare" => "#2674BD", "test" => "#238978", _ => "#C28A30" });
    public Brush CategoryBackground => Solid(Step?.Kind switch
    { "ign" or "input" or "sol" => "#FFF0D7", "can" or "canwait" or "read" => "#E5F1FF",
      "wave" or "wavecheck" => "#DBF4EE", "if" or "switch" or "loop" or "when" or "uds" => "#EFE8FA",
      _ => "#EEF2F7" });
    public Brush CategoryForeground => Solid(Step?.Kind switch
    { "ign" or "input" or "sol" => "#AF772B", "can" or "canwait" or "read" => "#286BB4",
      "wave" or "wavecheck" => "#167D73", "if" or "switch" or "loop" or "when" or "uds" => "#7752AE",
      _ => "#62768C" });
    public static Brush Solid(string hex) =>
        (Brush)new BrushConverter().ConvertFromString(hex)!;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Notify() { foreach (var key in new[]{nameof(Name),nameof(Summary),nameof(Status),nameof(BreakpointMark),nameof(Resource)}) PropertyChanged?.Invoke(this,new(key)); }
}
public sealed record PaletteKind(string Code,string Label,string Group,string Icon);
