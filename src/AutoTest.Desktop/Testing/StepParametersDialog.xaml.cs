using System.Collections.ObjectModel;
using System.Windows;
using AutoTest.Testing.Authoring;
using Prism.Mvvm;

namespace AutoTest.Desktop.Testing;

public sealed class StepPropertyField : BindableBase
{
    private string _value;
    public StepPropertyField(string key, string value)
    {
        Key = key;
        _value = value;
    }
    public string Key { get; }
    public string DisplayName => StepUiCatalog.PropertyLabel(Key);
    public string Value { get => _value; set => SetProperty(ref _value, value); }
}

public partial class StepParametersDialog : Window
{
    public ObservableCollection<StepPropertyField> Fields { get; } = new();
    public StepParametersResult? Result { get; private set; }

    public StepParametersDialog(StepNode node)
    {
        InitializeComponent();
        DataContext = this;
        TypeInfo.Text = StepUiCatalog.Label(node.Type) + " · " + node.Type;
        NameInput.Text = node.Name;
        var descriptor = StepDescriptorCatalog.Builtins().Lookup(node.Type);
        var keys = (descriptor?.RequiredProperties ?? [])
            .Concat(node.Properties.Keys)
            .Distinct(StringComparer.Ordinal);
        foreach (var key in keys)
            Fields.Add(new StepPropertyField(key, node.Properties.GetValueOrDefault(key, "")));
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
