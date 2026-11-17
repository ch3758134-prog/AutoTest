using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace AutoTest.UiPreview;

/// <summary>
/// DBC-backed editor state for one CAN-send step. It creates only configuration
/// data; no frames are emitted and nothing is guessed when a DBC revision changes.
/// </summary>
public sealed class PreviewCanInspector : INotifyPropertyChanged
{
    private const string AssetKey = "editor.dbcAssetId";
    private const string RevisionKey = "editor.dbcRevision";
    private const string FingerprintKey = "editor.dbcSha256";
    private const string EncodedIdKey = "editor.dbcEncodedId";
    private DemoStep? _step;
    private Action? _onEdited;
    private UiDbcMessage? _selected;
    private string _message = "";
    private bool _binding;

    public ObservableCollection<UiDbcMessage> Messages { get; } = new();
    public ObservableCollection<PreviewCanSignalValue> Signals { get; } = new();
    public bool IsVisible => _step?.Kind == "CanSend";
    public string Message { get => _message; private set { _message = value; Raise(); } }
    public bool HasMessages => Messages.Count != 0;
    public UiDbcMessage? SelectedMessage
    {
        get => _selected;
        set
        {
            if (ReferenceEquals(_selected, value)) return;
            _selected = value;
            Raise();
            if (!_binding && _step is not null)
            {
                if (value is null)
                {
                    _step.SetParam("dbcMessage", "");
                    _step.SetParam("signals", "");
                    foreach (var key in new[] { AssetKey, RevisionKey, FingerprintKey, EncodedIdKey })
                        _step.EditorMetadata.Remove(key);
                }
                else
                {
                    _step.SetParam("dbcMessage", value.Alias);
                    _step.SetParam("signals", "{}");
                    _step.EditorMetadata[AssetKey] = value.AssetId;
                    _step.EditorMetadata[RevisionKey] = value.AssetRevision.ToString(CultureInfo.InvariantCulture);
                    _step.EditorMetadata[FingerprintKey] = value.AssetSha256;
                    _step.EditorMetadata[EncodedIdKey] = value.EncodedId.ToString(CultureInfo.InvariantCulture);
                }
                PopulateSignals(value, null);
                Message = value is null ? "请选择工程中已导入的 DBC 报文。" :
                    "已绑定 " + value.Label + "。勾选信号后输入物理值。";
                _onEdited?.Invoke();
            }
        }
    }

    public void Bind(DemoStep? step, IEnumerable<UiDbcCatalog> catalogs, Action onEdited)
    {
        _binding = true;
        try
        {
            _step = step;
            _onEdited = onEdited;
            Messages.Clear();
            foreach (var message in catalogs.SelectMany(c => c.Messages)
                         .OrderBy(m => m.AssetName, StringComparer.CurrentCulture)
                         .ThenBy(m => m.Name, StringComparer.CurrentCulture))
                Messages.Add(message);
            _selected = null;
            Raise(nameof(SelectedMessage));
            Raise(nameof(IsVisible));
            Raise(nameof(HasMessages));
            Signals.Clear();

            if (!IsVisible) { Message = ""; return; }
            if (Messages.Count == 0) { Message = "工程中没有可读取的 DBC 报文，请先导入 DBC。"; return; }
            if (step is null) return;

            if (step.EditorMetadata.TryGetValue(AssetKey, out var assetId) &&
                step.EditorMetadata.TryGetValue(EncodedIdKey, out var rawId) &&
                uint.TryParse(rawId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                var match = Messages.FirstOrDefault(m => m.AssetId == assetId && m.EncodedId == id);
                if (match is not null &&
                    step.EditorMetadata.GetValueOrDefault(RevisionKey) ==
                        match.AssetRevision.ToString(CultureInfo.InvariantCulture) &&
                    string.Equals(step.EditorMetadata.GetValueOrDefault(FingerprintKey),
                        match.AssetSha256, StringComparison.OrdinalIgnoreCase) &&
                    step.Param("dbcMessage") == match.Alias)
                {
                    _selected = match;
                    Raise(nameof(SelectedMessage));
                    var saved = CanDbcBinding.TryReadValues(step.Param("signals"), out var parsed)
                        ? parsed : null;
                    PopulateSignals(match, saved);
                    Message = saved is null ? "信号配置无法解析，请重新选择报文后填写。" :
                        "已绑定 " + match.Label;
                    return;
                }
            }
            Message = "当前步骤缺少有效的 DBC 资源引用，或资源版本已改变。请重新选择报文。";
        }
        finally { _binding = false; }
    }

    private void PopulateSignals(UiDbcMessage? message, IReadOnlyDictionary<string, string>? saved)
    {
        Signals.Clear();
        if (message is null) return;
        foreach (var signal in message.Signals)
        {
            var physical = "";
            var isSelected = saved is not null && saved.TryGetValue(signal.Name, out physical);
            Signals.Add(new PreviewCanSignalValue(signal, isSelected, isSelected ? physical : "",
                () => SignalsChanged()));
        }
    }

    private void SignalsChanged()
    {
        if (_binding || _step is null || _selected is null) return;
        var values = Signals.Where(x => x.IsSelected)
            .ToDictionary(x => x.Signal.Name, x => x.PhysicalValue, StringComparer.Ordinal);
        _step.SetParam("signals", JsonSerializer.Serialize(values));
        Message = Signals.Any(x => x.IsSelected && x.Error.Length != 0)
            ? "有信号物理值无效，请修正后保存。" : "信号设置已更新（尚未保存）。";
        _onEdited?.Invoke();
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class PreviewCanSignalValue : INotifyPropertyChanged
{
    private readonly Action _onChanged;
    private bool _selected;
    private string _physicalValue;
    public UiDbcSignal Signal { get; }
    public bool IsSelected
    {
        get => _selected;
        set { if (value == _selected) return; _selected = value; Raise(); Raise(nameof(Error)); _onChanged(); }
    }
    public string PhysicalValue
    {
        get => _physicalValue;
        set { if (value == _physicalValue) return; _physicalValue = value; Raise(); Raise(nameof(Error)); _onChanged(); }
    }
    public string Error => !IsSelected ? "" : CanDbcBinding.CheckValue(Signal, PhysicalValue);
    public PreviewCanSignalValue(UiDbcSignal signal, bool selected, string value, Action changed)
    { Signal = signal; _selected = selected; _physicalValue = value; _onChanged = changed; }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class CanDbcBinding
{
    public static bool TryReadValues(string text, out IReadOnlyDictionary<string, string> values)
    {
        values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(text)) return false;
        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(text);
            if (parsed is null || parsed.Any(x => x.Value is null)) return false;
            values = parsed;
            return true;
        }
        catch (JsonException) { return false; }
    }

    public static string CheckValue(UiDbcSignal signal, string input)
    {
        if (!double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ||
            !double.IsFinite(v)) return "请输入有效的物理数值（使用小数点）";
        if (v < signal.Minimum || v > signal.Maximum)
            return "超出 DBC 范围：" + signal.RangeLabel;
        return "";
    }

    public static IReadOnlyList<string> Validate(DemoStep step, IReadOnlyList<UiDbcCatalog> catalogs)
    {
        if (step.Kind != "CanSend") return [];
        var error = new List<string>();
        var id = step.EditorMetadata.GetValueOrDefault("editor.dbcAssetId");
        var hash = step.EditorMetadata.GetValueOrDefault("editor.dbcSha256");
        var revision = step.EditorMetadata.GetValueOrDefault("editor.dbcRevision");
        var encodedId = step.EditorMetadata.GetValueOrDefault("editor.dbcEncodedId");
        var message = catalogs.SelectMany(x => x.Messages).FirstOrDefault(m =>
            m.AssetId == id &&
            m.EncodedId.ToString(CultureInfo.InvariantCulture) == encodedId &&
            m.AssetRevision.ToString(CultureInfo.InvariantCulture) == revision &&
            string.Equals(m.AssetSha256, hash, StringComparison.OrdinalIgnoreCase) &&
            m.Alias == step.Param("dbcMessage"));
        if (message is null)
        {
            error.Add(step.Name + "：没有有效的 DBC 报文及版本绑定。");
            return error;
        }
        if (!TryReadValues(step.Param("signals"), out var values))
        {
            error.Add(step.Name + "：信号设置不是有效的已选信号数据。");
            return error;
        }
        if (values.Count == 0)
            error.Add(step.Name + "：请至少选择一个 DBC 信号并填写物理值。");
        foreach (var pair in values)
        {
            var signal = message.Signals.FirstOrDefault(s => s.Name == pair.Key);
            if (signal is null)
                error.Add(step.Name + "：DBC 中不存在信号 " + pair.Key);
            else
            {
                var messageText = CheckValue(signal, pair.Value);
                if (messageText.Length != 0) error.Add(step.Name + " / " + pair.Key + "：" + messageText);
            }
        }
        return error;
    }
}
