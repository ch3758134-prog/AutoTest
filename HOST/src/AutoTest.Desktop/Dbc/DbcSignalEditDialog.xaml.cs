using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using AutoTest.Projects.Dbc;

namespace AutoTest.Desktop.Dbc;

public partial class DbcSignalEditDialog : Window
{
    private readonly DbcSignal _existing;
    public DbcSignal? Result { get; private set; }

    // Round-trip persistence uses the ORIGINAL binary value for unchanged
    // fields; G12 is a readable editing projection, not an implicit reformat
    // of DBC factor/offset/limits on every unrelated signal edit.
    private static string Readable(double value) =>
        value.ToString("G12", CultureInfo.InvariantCulture);

    public DbcSignalEditDialog(DbcSignal signal, bool protectName = false)
    {
        InitializeComponent();
        Loaded += (_, _) => AutoTest.Desktop.Shell.DesktopWindowSizing.FitToVisibleWorkspace(this);
        _existing = signal;
        NameBox.Text = signal.Name;
        NameBox.IsReadOnly = protectName;
        NameBox.ToolTip = protectName ? "原文保护或已有值表引用；可通过中文别名修改显示名称。" : "DBC 原始信号标识符。";
        StartBitBox.Text = signal.StartBit.ToString(CultureInfo.InvariantCulture);
        BitLengthBox.Text = signal.BitLength.ToString(CultureInfo.InvariantCulture);
        FactorBox.Text = Readable(signal.Factor);
        OffsetBox.Text = Readable(signal.Offset);
        MinimumBox.Text = Readable(signal.Minimum);
        MaximumBox.Text = Readable(signal.Maximum);
        UnitBox.Text = signal.Unit;
        ReceiverBox.Text = signal.Receiver;
        MultiplexerBox.Text = signal.Multiplexer ?? "";
        IntelBox.IsChecked = signal.Intel;
        SignedBox.IsChecked = signal.Signed;
        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var receiver = ReceiverBox.Text.Trim();
        var mux = MultiplexerBox.Text.Trim();
        // DBC raw identifiers are ASCII; Chinese display names belong in the
        // dedicated signal alias, not the wire-level DBC name fields.
        const string dbcIdentifier = @"^[A-Za-z_][A-Za-z0-9_]*$";
        if (!Regex.IsMatch(name, dbcIdentifier) ||
            !Regex.IsMatch(receiver, dbcIdentifier) ||
            (mux.Length > 0 && mux != "M" && !Regex.IsMatch(mux, @"^m[0-9]+$"))
        {
            ErrorText.Text = "信号名称/接收节点须是 DBC 标识符；复用标记只能为空、M 或 m数字。";
            return;
        }
        if (UnitBox.Text.Contains('"') || UnitBox.Text.Contains('\n') || UnitBox.Text.Contains('\r'))
        {
            ErrorText.Text = "单位不允许包含双引号或换行符。";
            return;
        }

        var numbers = new[]
        {
            FactorBox.Text, OffsetBox.Text, MinimumBox.Text, MaximumBox.Text
        };
        var numeric = new double[4];
        for (var i = 0; i < numbers.Length; i++)
        {
            if (!double.TryParse(numbers[i], NumberStyles.Float, CultureInfo.InvariantCulture, out numeric[i]) ||
                !double.IsFinite(numeric[i]))
            {
                ErrorText.Text = "Factor、Offset、Minimum、Maximum 必须是有效有限数值，使用英文小数点。";
                return;
            }
        }
        var originalNumbers = new[]
        {
            _existing.Factor, _existing.Offset, _existing.Minimum, _existing.Maximum
        };
        for (var i = 0; i < numeric.Length; i++)
        {
            if (string.Equals(numbers[i].Trim(), Readable(originalNumbers[i]),
                    StringComparison.Ordinal))
                numeric[i] = originalNumbers[i];
        }

        if (!int.TryParse(StartBitBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var start) ||
            !int.TryParse(BitLengthBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bits) ||
            start is < 0 or > 63 || bits is < 1 or > 64 ||
            numeric[0] == 0 || numeric[2] > numeric[3])
        {
            ErrorText.Text = "位位置必须为 0–63，位长 1–64，Factor 不能为 0，Minimum 不能大于 Maximum。";
            return;
        }

        Result = _existing with
        {
            Name = name,
            StartBit = start,
            BitLength = bits,
            Intel = IntelBox.IsChecked == true,
            Signed = SignedBox.IsChecked == true,
            Factor = numeric[0], Offset = numeric[1],
            Minimum = numeric[2], Maximum = numeric[3],
            Unit = UnitBox.Text, Receiver = receiver,
            Multiplexer = mux.Length == 0 ? null : mux
        };
        DialogResult = true;
    }
}
