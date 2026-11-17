using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace AutoTest.UiPreview;

/// <summary>A revision-pinned, verified DBC asset. No CAN hardware dependencies.</summary>
public sealed record UiDbcCatalog(string AssetId, string Name, int Revision, string Sha256,
    IReadOnlyList<UiDbcMessage> Messages);

public sealed record UiDbcSignal(string Name, string Unit, double Minimum, double Maximum,
    double Factor, double Offset, bool Signed, string? Multiplexer)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Unit) ? Name : Name + " (" + Unit + ")";
    public string RangeLabel => Minimum.ToString("G", CultureInfo.InvariantCulture) +
        " ～ " + Maximum.ToString("G", CultureInfo.InvariantCulture) +
        (string.IsNullOrWhiteSpace(Unit) ? "" : " " + Unit);
}

public sealed record UiDbcMessage(string AssetId, string AssetName, int AssetRevision,
    string AssetSha256, uint EncodedId, uint ArbitrationId, bool IsExtended,
    string Name, int Dlc, IReadOnlyList<UiDbcSignal> Signals)
{
    public string Alias => AssetName + "." + Name;
    public string Label => AssetName + " / " + Name + " · 0x" +
        ArbitrationId.ToString("X") + (IsExtended ? " · 扩展帧" : " · 标准帧");
    public string TreeLabel => Name + "  [0x" + ArbitrationId.ToString("X") + "]" +
        (IsExtended ? " 扩展" : "");
}

public sealed class UiExplorerTest : INotifyPropertyChanged
{
    private bool _selected = true;
    private bool _active;
    public UiExplorerTest(string id, string name) { Id = id; Name = name; }
    public string Id { get; }
    public string Name { get; }
    public bool Selected
    {
        get => _selected;
        set { if (_selected != value) { _selected = value; Notify(); } }
    }
    public bool Active
    {
        get => _active;
        set { if (_active != value) { _active = value; Notify(); } }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record UiExplorerAsset(string Id, string Name, string Kind,
    IReadOnlyList<UiDbcMessage> Messages)
{
    public string DisplayName => Name + (Messages.Count > 0 ? " (" + Messages.Count + ")" : "");
}
public sealed record UiExplorerGroup(string Name, IReadOnlyList<UiExplorerAsset> Assets, int? CountOverride = null)
{
    public int Count => CountOverride ?? Assets.Sum(a => a.Messages.Count > 0 ? a.Messages.Count : 1);
    public string DisplayName => (Name switch
    {
        "DBC 报文" => "▤  ",
        "按键配置" => "◈  ",
        "波形参考" => "〰  ",
        "UDS 服务" => "◇  ",
        _ => "▤  "
    }) + Name + "  " + Count.ToString(CultureInfo.InvariantCulture);
}
