using System.ComponentModel;
using System.IO;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AutoTest.Specialists.Uds.Workbench;

namespace AutoTest.UdsLab;

/// <summary>
/// One editable multi-service diagnostic case. It is NOT a replacement for
/// AutoTest TEST DSL/StepNode or a separate generic test executor.
/// </summary>
public sealed class DiagnosticCase : INotifyPropertyChanged
{
    private string _name = "新建诊断用例";
    private string _description = "";

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name
    {
        get => _name;
        set
        {
            if (_name == value) return;
            _name = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        }
    }
    public string Description
    {
        get => _description;
        set
        {
            if (_description == value) return;
            _description = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Description)));
        }
    }
    public int Revision { get; set; } = 1;
    public List<DiagnosticCaseStep> Steps { get; set; } = [];
    public event PropertyChangedEventHandler? PropertyChanged;

    public DiagnosticCase Clone()
    {
        return new DiagnosticCase
        {
            Name = Name + " · 副本", Description = Description,
            Steps = Steps.Select(s => s.Clone(newIdentity: true)).ToList()
        };
    }
}

/// <summary>One UDS service request and optional response extraction.</summary>
public sealed class DiagnosticCaseStep : INotifyPropertyChanged
{
    private string _name = "读取数据";
    private string _purpose = "";
    private bool _enabled = true;
    private string _lastState = "未运行";

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name
    {
        get => _name;
        set
        {
            if (_name == value) return;
            _name = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        }
    }
    public string Purpose
    {
        get => _purpose;
        set
        {
            if (_purpose == value) return;
            _purpose = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Purpose)));
        }
    }
    public bool PauseBefore { get; set; }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Enabled)));
        }
    }
    public DiagnosticAction Action { get; set; } = new();
    /// <summary>
    /// Optional full UDS PDU template. Replaces {{variable}} with bytes captured
    /// from a prior positive ECU response in the SAME current run. If blank,
    /// Action.RequestHex is used.
    /// </summary>
    public string RequestTemplate { get; set; } = "";
    public string CaptureAs { get; set; } = "";
    public int CaptureOffset { get; set; } = 3;
    public int CaptureLength { get; set; } = 0;

    [JsonIgnore]
    public string LastState
    {
        get => _lastState;
        set
        {
            if (_lastState == value) return;
            _lastState = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastState)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;

    public DiagnosticCaseStep Clone(bool newIdentity)
    {
        var cloned = new DiagnosticCaseStep
        {
            Id = newIdentity ? Guid.NewGuid() : Id,
            Name = Name, Purpose = Purpose, Enabled = Enabled, PauseBefore = PauseBefore,
            Action = new DiagnosticAction
            {
                Id = newIdentity ? Guid.NewGuid() : Action.Id,
                Name = Action.Name, Group = Action.Group,
                RequestHex = Action.RequestHex,
                ExpectedPrefixHex = Action.ExpectedPrefixHex,
                ServiceSidHex = Action.ServiceSidHex,
                PrimaryHex = Action.PrimaryHex, AuxiliaryHex = Action.AuxiliaryHex,
                RawMode = Action.RawMode, Decoder = Action.Decoder,
                DataOffset = Action.DataOffset, Scale = Action.Scale, Unit = Action.Unit
            },
            RequestTemplate = RequestTemplate,
            CaptureAs = CaptureAs, CaptureOffset = CaptureOffset,
            CaptureLength = CaptureLength
        };
        return cloned;
    }
}

/// <summary>Bounded byte-only variable substitution; no scripting/eval.</summary>
public static class DiagnosticCaseVariables
{
    private static readonly Regex VariableName =
        new(@"^[A-Za-z][A-Za-z0-9_]{0,31}$", RegexOptions.CultureInvariant);
    private static readonly Regex Reference =
        new(@"\{\{([A-Za-z][A-Za-z0-9_]{0,31})\}\}", RegexOptions.CultureInvariant);

    public static void Validate(DiagnosticCaseStep step)
    {
        if (step.Id == Guid.Empty || step.Action is null ||
            string.IsNullOrWhiteSpace(step.Name) || step.Name.Length > 150)
            throw new InvalidDataException("诊断步骤缺少名称、稳定 ID 或有效请求。");
        if (step.RequestTemplate.Length > 8190 || step.CaptureOffset is < 0 or > 4095 ||
            step.CaptureLength is < 0 or > 4095)
            throw new InvalidDataException("步骤模板或响应提取范围超出限制。");
        if (step.CaptureAs.Length > 0 &&
            (!VariableName.IsMatch(step.CaptureAs) || step.CaptureLength == 0))
            throw new InvalidDataException("保存为变量需要变量名（英文开头、字母数字下划线）及非零提取字节数。");
        if (step.CaptureAs.Length == 0 && step.CaptureLength != 0)
            throw new InvalidDataException("填写了提取字节数却没有变量名。");
        if (step.RequestTemplate.Length > 0)
        {
            string scrubbed = Reference.Replace(step.RequestTemplate, "AA");
            _ = UdsHex.Parse(scrubbed); // any other unsupported token is invalid
        }
    }

    public static byte[] Resolve(DiagnosticCaseStep step, IReadOnlyDictionary<string, byte[]> variables)
    {
        string source = string.IsNullOrWhiteSpace(step.RequestTemplate)
            ? step.Action.RequestHex : step.RequestTemplate;
        string resolved = Reference.Replace(source, m =>
        {
            string key = m.Groups[1].Value;
            if (!variables.TryGetValue(key, out var bytes) || bytes.Length == 0)
                throw new InvalidOperationException("尚无本次用例的有效变量：" + key);
            return Convert.ToHexString(bytes);
        });
        return UdsHex.Parse(resolved);
    }

    public static byte[] Extract(DiagnosticCaseStep step, byte[] response)
    {
        if (step.CaptureAs.Length == 0) return [];
        if (step.CaptureLength == 0 || step.CaptureOffset > response.Length ||
            step.CaptureLength > response.Length - step.CaptureOffset)
            throw new InvalidDataException("响应不足以提取变量 " + step.CaptureAs +
                "；已停止用例后续步骤。");
        return response.AsSpan(step.CaptureOffset, step.CaptureLength).ToArray();
    }
}
