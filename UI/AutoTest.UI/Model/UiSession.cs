using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

namespace AutoTest.UI.Model;

/// <summary>
/// UI exploration document. The format deliberately does NOT claim to be a
/// TEST DSL, PROJECT asset, diagnostic trace, physical observation or report.
/// </summary>
public sealed class UiSession
{
    public string ProjectName { get; set; } = "未命名工作区";
    public string LastWorkspace { get; set; } = "自动测试";
    public ObservableCollection<UiResource> Resources { get; set; } = [];
    public ObservableCollection<UiTestCase> Cases { get; set; } = [];
    public ObservableCollection<UiStep> CommonFlows { get; set; } = [];
    public ObservableCollection<UiDbcMessage> Messages { get; set; } = [];
    public ObservableCollection<UiCanMessage> CanTx { get; set; } = [];
    public ObservableCollection<UiUdsRequest> UdsRequests { get; set; } = [];
    public ObservableCollection<UiInputAction> Inputs { get; set; } = [];
    public ObservableCollection<UiInputKey> Keys { get; set; } = [];
    public ObservableCollection<UiAnalogChannel> AnalogChannels { get; set; } = [];
    public ObservableCollection<UiWaveSignal> WaveSignals { get; set; } = [];
    public ObservableCollection<UiWavePoint> WavePoints { get; set; } = [];
    public string WaveSourceFile { get; set; } = "";
    public ObservableCollection<UiScanEntry> ScanEntries { get; set; } = [];
    public ObservableCollection<UiRunRecord> History { get; set; } = [];
    public string FlashFile { get; set; } = "";
    public string FlashProfile { get; set; } = "";
    public string DiagnosticAddress { get; set; } = "";
    public string FilterText { get; set; } = "";
    public string? SessionFile { get; set; }

    public static UiSession New()
    {
        var s = new UiSession();
        s.Cases.Add(new UiTestCase { Name = "未命名测试用例" });
        for (var i = 1; i <= 20; i++) s.Keys.Add(new UiInputKey { Index = i, Name = "KEY " + i.ToString("00") });
        for (var i = 1; i <= 5; i++) s.AnalogChannels.Add(new UiAnalogChannel { Index = i, Name = "模拟输出 " + i });
        return s;
    }
    public static UiSession Load(string path)
    {
        var json = File.ReadAllText(path);
        var s = JsonSerializer.Deserialize<UiSession>(json) ?? throw new InvalidDataException("空工作区文件");
        s.SessionFile = Path.GetFullPath(path);
        return s;
    }
    public void Save(string path)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(this,
                new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, full, overwrite: true);
            SessionFile = full;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public sealed class UiResource
{
    public string Name { get; set; } = "未命名资源";
    public string Kind { get; set; } = "配置";
    public string Path { get; set; } = "";
    public string Note { get; set; } = "";
}
public sealed class UiTestCase
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "测试用例";
    public string Description { get; set; } = "";
    public bool RunSelected { get; set; } = true;
    public ObservableCollection<UiStep> Steps { get; set; } = [];
}
public sealed class UiStep
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Phase { get; set; } = "测试";
    public string Type { get; set; } = "等待";
    public string Name { get; set; } = "新步骤";
    public string Target { get; set; } = "";
    public string Value { get; set; } = "";
    public string Condition { get; set; } = "";
    public string Note { get; set; } = "";
    public string Branch { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public ObservableCollection<UiStep> Children { get; set; } = [];
}
public sealed class UiDbcMessage
{
    public string Name { get; set; } = "NewMessage";
    public string CanId { get; set; } = "0x100";
    public int Dlc { get; set; } = 8;
    public string Comment { get; set; } = "";
    public ObservableCollection<UiDbcSignal> Signals { get; set; } = [];
}
public sealed class UiDbcSignal
{
    public string Name { get; set; } = "Signal";
    public string Alias { get; set; } = "";
    public int StartBit { get; set; }
    public int Length { get; set; } = 8;
    public string Endian { get; set; } = "Intel";
    public string Factor { get; set; } = "1";
    public string Offset { get; set; } = "0";
    public string Unit { get; set; } = "";
    public string Comment { get; set; } = "";
}
public sealed class UiCanMessage
{
    public bool Enabled { get; set; } = true;
    public string Name { get; set; } = "";
    public string CanId { get; set; } = "0x100";
    public string Data { get; set; } = "00 00 00 00 00 00 00 00";
    public int PeriodMs { get; set; }
    public string State { get; set; } = "未发送";
}
public sealed class UiUdsRequest
{
    public bool Enabled { get; set; } = true;
    public string Service { get; set; } = "0x22 · ReadDataByIdentifier";
    public string Request { get; set; } = "22 F1 90";
    public string Note { get; set; } = "";
    public string LastResponse { get; set; } = "未发送";
}
public sealed class UiInputKey
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public bool Pressed { get; set; }
}
public sealed class UiAnalogChannel
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public string EcuFunction { get; set; } = "";
    public string MinVoltage { get; set; } = "";
    public string MaxVoltage { get; set; } = "";
    public string Comment { get; set; } = "";
}
public sealed class UiInputAction
{
    public string Name { get; set; } = "新动作";
    public string KeyMask { get; set; } = "00000";
    public int HoldMs { get; set; } = 500;
    public string Note { get; set; } = "";
}
public sealed class UiWaveSignal
{
    public bool Visible { get; set; } = true;
    public string Name { get; set; } = "信号";
    public string Unit { get; set; } = "";
    public string Reference { get; set; } = "";
}
public sealed class UiWavePoint
{
    public double Time { get; set; }
    public double Value { get; set; }
}
public sealed class UiScanEntry
{
    public DateTime Time { get; set; } = DateTime.Now;
    public string Barcode { get; set; } = "";
    public string Status { get; set; } = "仅记录本地输入";
}
public sealed class UiRunRecord
{
    public DateTime Time { get; set; }
    public string Barcode { get; set; } = "";
    public string Case { get; set; } = "";
    public string Verdict { get; set; } = "";
    public string Evidence { get; set; } = "";
}
