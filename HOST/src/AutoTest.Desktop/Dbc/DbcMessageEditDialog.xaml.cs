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
        IdBox.Text = "0x" + existing.ArbitrationId.ToString("X") +
            (existing.IsExtended ? "（29 位扩展帧）" : "（11 位标准帧）");
        IdBox.IsReadOnly = true;
        NameBox.Text = existing.Name;
        DlcBox.Text = existing.Dlc.ToString(CultureInfo.InvariantCulture);
        TransmitterBox.Text = existing.Transmitter;
        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
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
        Result = _existing with { Name = name, Dlc = dlc, Transmitter = senderName };
        DialogResult = true;
    }
}
