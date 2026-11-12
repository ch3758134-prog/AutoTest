using System.Windows;
using System.Windows.Controls;
using AutoTest.Testing.Authoring;

namespace AutoTest.Desktop.Testing;

public partial class TargetMappingDialog : Window
{
    public IReadOnlyList<MappingChoice> Choices { get; }
    public TestTargetMapping? Result { get; private set; }

    public TargetMappingDialog(IReadOnlyList<MappingChoice> choices)
    {
        Choices = choices;
        InitializeComponent();
        DataContext = this;
        if (choices.Count > 0) TargetChoice.SelectedIndex = 0;
    }

    private void TargetChoice_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (TargetChoice.SelectedItem is MappingChoice choice)
            AliasInput.Text = choice.Mapping.Name;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (TargetChoice.SelectedItem is not MappingChoice choice)
        {
            ErrorText.Text = "请选择测试对象。";
            return;
        }
        try
        {
            Result = choice.Mapping with { Name = AliasInput.Text.Trim() };
            Result.Validate();
            DialogResult = true;
        }
        catch (ArgumentException ex) { ErrorText.Text = ex.Message; }
    }
}