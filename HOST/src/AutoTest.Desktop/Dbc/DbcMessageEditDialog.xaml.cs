using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using AutoTest.Projects.Dbc;

namespace AutoTest.Desktop.Dbc;

public partial class DbcMessageEditDialog : Window
{
    private readonly DbcMessage _existing;
    public DbcMessage? Result { get; private set; }

    public DbcMessageEditDialog(DbcMessage existing, bool allowIdChange = false)
    {
        InitializeComponent();
        Loaded += (_, _) => AutoTest.Desktop.Shell.DesktopWindowSizing.FitToVisibleWorkspace(this);
        _existing = existing;
        IdBox.Text = "0x" + existing.ArbitrationId.ToString("X");
        IdBox.IsReadOnly = !allowIdChange;
        ExtendedBox.IsChecked = existing.IsExtended;
        ExtendedBox.IsEnabled = allowIdChange;
        IdBox.ToolTip = allowIdChange ? "输入实际仲裁 ID；扩展帧标记由 DBC 编码处理。"
            : "当前文件或值表引用保护 CAN ID。";
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
        // DBC serializer/parser uses ASCII source tokens. A localized display
        // name must be edited as an alias, not a nonstandard DBC identifier.
        const string dbcIdentifier = @"^[A-Za-z_][A-Za-z0-9_]*$";
        if (!Regex.IsMatch(name, dbcIdentifier) ||
            !Regex.IsMatch(senderName, dbcIdentifier))
        {
            ErrorText.Text = "报文名称和发送节点必须是非空、无空格的 DBC 标识符。";
            return;
        }
        var idText = IdBox.Text.Trim();
        if (idText.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) idText = idText[2..];
        var extended = ExtendedBox.IsChecked == true;
        if (!uint.TryParse(idText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id) ||
            id > (extended ? 0x1FFFFFFFu : 0x7FFu))
        { ErrorText.Text = "CAN ID 超出标准帧或扩展帧范围。"; return; }
        var encoded = id == _existing.ArbitrationId && extended == _existing.IsExtended
            ? _existing.Id : extended ? id | 0x80000000u : id;
        Result = _existing with { Id = encoded, Name = name, Dlc = dlc, Transmitter = senderName };
        DialogResult = true;
    }
}
