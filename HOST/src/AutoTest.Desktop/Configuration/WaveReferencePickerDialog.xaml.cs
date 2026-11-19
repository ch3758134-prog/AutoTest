using System.Windows;

namespace AutoTest.Desktop.Configuration;

public partial class WaveReferencePickerDialog : Window
{
    public IReadOnlyList<WaveReferenceWorkspaceService.CaptureChoice> Choices { get; }
    public Guid? SelectedEvidence { get; private set; }
    public string ReferenceName { get; private set; } = "";

    public WaveReferencePickerDialog(
        IReadOnlyList<WaveReferenceWorkspaceService.CaptureChoice> choices)
    {
        Choices = choices;
        InitializeComponent();
        DataContext = this;
        if (choices.Count > 0) CaptureList.SelectedIndex = 0;
    }

    private void Approve_Click(object sender, RoutedEventArgs e)
    {
        var name = ReferenceInput.Text.Trim();
        if (CaptureList.SelectedItem is not WaveReferenceWorkspaceService.CaptureChoice choice)
        {
            ErrorText.Text = "请选择一条已完成的采集证据。";
            return;
        }
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128)
        {
            ErrorText.Text = "参考名称不能为空且不得超过 128 字符。";
            return;
        }
        SelectedEvidence = choice.Evidence;
        ReferenceName = name;
        DialogResult = true;
    }
}
