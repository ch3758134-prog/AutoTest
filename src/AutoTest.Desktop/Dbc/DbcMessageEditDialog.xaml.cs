using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using AutoTest.Projects.Dbc;

namespace AutoTest.Desktop.Dbc;

public partial class DbcMessageEditDialog : Window
{
    private readonly DbcMessage _existing;
    public DbcMessage? Result { get; private set; }

    public DbcMessageEditDialog(DbcMessage existing)
    {
        InitializeComponent();
        _existing = existing;
        IdBox.Text = "0x" + existing.Id.ToString("X");
        NameBox.Text = existing.Name;
        DlcBox.Text = existing.Dlc.ToString(CultureInfo.InvariantCulture);
        TransmitterBox.Text = existing.Transmitter;
        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        var text = IdBox.Text.Trim();
        uint parsed;
        var isValidId = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? uint.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed)
            : uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out parsed);
        if (!isValidId || parsed > 0x7FF)
        {
            ErrorText.Text = "当前 DBC 写入器仅允许标准 11 位 CAN ID（0–0x7FF）。";
            return;
        }
        if (!int.TryParse(DlcBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dlc) ||
            dlc is < 0 or > 8)
        {
            ErrorText.Text = "DLC 必须是 0–8 之间的整数。";
            return;
        }
        var name = NameBox.Text.Trim();
        var senderName = TransmitterBox.Text.Trim();
        if (!Regex.IsMatch(name, @"^\w+$") || !Regex.IsMatch(senderName, @"^\w+$"))
        {
            ErrorText.Text = "报文名称和发送节点必须是非空、无空格的 DBC 标识符。";
            return;
        }
        Result = _existing with { Id = parsed, Name = name, Dlc = dlc, Transmitter = senderName };
        DialogResult = true;
    }
}
