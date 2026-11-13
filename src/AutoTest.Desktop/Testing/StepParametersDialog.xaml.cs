using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using AutoTest.Testing.Authoring;
using Prism.Mvvm;

namespace AutoTest.Desktop.Testing;

public sealed class StepPropertyField : BindableBase
{
    private string _value;
    private IReadOnlyList<string> _options;
    private string _displayName;
    private bool _isVisible = true;
    public StepPropertyField(string key, string value,
        IReadOnlyList<string>? options = null, string? displayName = null)
    {
        Key = key;
        _value = value;
        _options = options ?? Array.Empty<string>();
        _displayName = displayName ?? StepUiCatalog.PropertyLabel(key);
    }
    public string Key { get; }
    public IReadOnlyList<string> Options
    {
        get => _options;
        set
        {
            if (SetProperty(ref _options, value ?? Array.Empty<string>()))
                RaisePropertyChanged(nameof(HasOptions));
        }
    }
    public bool HasOptions => Options.Count > 0;
    public string DisplayName { get => _displayName; set => SetProperty(ref _displayName, value); }
    public bool IsVisible { get => _isVisible; set => SetProperty(ref _isVisible, value); }
    public string Value { get => _value; set => SetProperty(ref _value, value ?? ""); }
}

public partial class StepParametersDialog : Window
{
    public ObservableCollection<StepPropertyField> Fields { get; } = new();
    public StepParametersResult? Result { get; private set; }

    public StepParametersDialog(StepNode node, IReadOnlyList<string>? targetNames = null,
        IReadOnlyList<string>? waveProfileNames = null,
        IReadOnlyList<string>? referenceNames = null,
        IReadOnlyDictionary<string, TestTargetKind>? targetKinds = null)
    {
        InitializeComponent();
        DataContext = this;
        TypeInfo.Text = StepUiCatalog.Label(node.Type) + " · " + node.Type;
        NameInput.Text = node.Name;
        var descriptor = StepDescriptorCatalog.Builtins().Lookup(node.Type);
        var required = descriptor is null ? Enumerable.Empty<string>() : descriptor.RequiredProperties.AsEnumerable();
        var keys = required.Concat(node.Properties.Keys)
            .Distinct(StringComparer.Ordinal);
        foreach (var key in keys)
            Fields.Add(new StepPropertyField(key, node.Properties.GetValueOrDefault(key, ""),
                key switch
                {
                    "target" => targetNames,
                    "profile" when waveProfileNames?.Count > 0 =>
                        new[] { "" }.Concat(waveProfileNames).ToArray(),
                    "reference" when referenceNames?.Count > 0 =>
                        new[] { "" }.Concat(referenceNames).ToArray(),
                    "mode" when node.Type == "Read" => ["Latest", "Next"],
                    "operator" when node.Type == "Calculate" => ["+", "-", "*", "/"],
                    "operator" when node.Type is "Expect" or "WaitUntil" =>
                        ["==", "!=", ">", ">=", "<", "<="],
                    _ => null
                }));
        if (node.Type == "WaveAnalyze" &&
            Fields.FirstOrDefault(x => x.Key == "profile") is { } profileField)
        {
            void UpdateAnalysisFields()
            {
                var usingProfile = !string.IsNullOrWhiteSpace(profileField.Value);
                foreach (var field in Fields.Where(f => f.Key is "tolerance" or "coverage"))
                    field.IsVisible = !usingProfile;
            }
            profileField.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(StepPropertyField.Value))
                    UpdateAnalysisFields();
            };
            UpdateAnalysisFields();
        }
        if (node.Type == "Write" && targetKinds is not null &&
            Fields.FirstOrDefault(x => x.Key == "target") is { } targetField &&
            Fields.FirstOrDefault(x => x.Key == "value") is { } valueField)
        {
            void UpdateWriteOptions()
            {
                if (!targetKinds.TryGetValue(targetField.Value, out var kind))
                    return;
                switch (kind)
                {
                    case TestTargetKind.Button:
                        valueField.DisplayName = "模拟按键动作（Pulse / Hold / Release，可填写 Pulse:保持毫秒数）";
                        valueField.Options = ["Pulse", "Hold", "Release"];
                        if (valueField.Value is "1" or "ON" or "OFF") valueField.Value = "Pulse";
                        break;
                    case TestTargetKind.Ignition:
                        valueField.DisplayName = "IGN 状态";
                        valueField.Options = ["ON", "OFF"];
                        if (!new[] { "ON", "OFF", "true", "false", "1", "0" }
                            .Contains(valueField.Value, StringComparer.OrdinalIgnoreCase))
                            valueField.Value = "ON";
                        break;
                    case TestTargetKind.CanMessage:
                        valueField.DisplayName =
                            "DBC 报文内容（填写全部信号=物理值;...，或 HEX:完整报文）";
                        valueField.Options = [];
                        if (valueField.Value is "Pulse" or "Hold" or "Release" or "ON" or "OFF")
                            valueField.Value = "";
                        break;
                }
            }
            targetField.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(StepPropertyField.Value))
                    UpdateWriteOptions();
            };
            UpdateWriteOptions();
        }
        Loaded += (_, _) => { NameInput.Focus(); NameInput.SelectAll(); };
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        var name = NameInput.Text.Trim();
        if (name.Length == 0 || name.Length > 120)
        {
            ErrorText.Text = "步骤名称不能为空，长度不能超过 120 个字符。";
            return;
        }
        Result = new StepParametersResult(name,
            Fields.ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal));
        DialogResult = true;
    }
}
