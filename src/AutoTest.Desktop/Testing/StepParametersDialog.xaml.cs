using System.Collections.ObjectModel;
using System.Windows;
using AutoTest.Testing.Authoring;
using Prism.Mvvm;

namespace AutoTest.Desktop.Testing;

public sealed class StepPropertyField : BindableBase
{
    private string _value;
    public StepPropertyField(string key, string value,
        IReadOnlyList<string>? options = null, string? displayName = null)
    {
        Key = key;
        _value = value;
        Options = options ?? Array.Empty<string>();
        DisplayName = displayName ?? StepUiCatalog.PropertyLabel(key);
    }
    public string Key { get; }
    public IReadOnlyList<string> Options { get; }
    public bool HasOptions => Options.Count > 0;
    public string DisplayName { get; }
    public string Value { get => _value; set => SetProperty(ref _value, value); }
}

public partial class StepParametersDialog : Window
{
    public ObservableCollection<StepPropertyField> Fields { get; } = new();
    public StepParametersResult? Result { get; private set; }

    public StepParametersDialog(StepNode node, IReadOnlyList<string>? targetNames = null)
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
                key == "target" ? targetNames : null,
                node.Type == "Write" && key == "value"
                    ? "按键：Pulse / Pulse:毫秒 / Hold / Release；IGN：ON/OFF；CAN：HEX:完整帧 或 信号=物理值;..."
                    : null));
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
