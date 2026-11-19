using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using AutoTest.Projects.Dbc;

namespace AutoTest.Desktop.Dbc;

public partial class DbcSignalEditDialog : Window
{
    private readonly DbcSignal _existing;
    public DbcSignal? Result { get; private set; }

    public DbcSignalEditDialog(DbcSignal signal)
    {
        InitializeComponent();
        _existing = signal;
        NameBox.Text = signal.Name;
        StartBitBox.Text = signal.StartBit.ToString(CultureInfo.InvariantCulture);
        BitLengthBox.Text = signal.BitLength.ToString(CultureInfo.InvariantCulture);
        FactorBox.Text = signal.Factor.ToString("R", CultureInfo.InvariantCulture);
        OffsetBox.Text = signal.Offset.ToString("R", CultureInfo.InvariantCulture);
        MinimumBox.Text = signal.Minimum.ToString("R", CultureInfo.InvariantCulture);
        MaximumBox.Text = signal.Maximum.ToString("R", CultureInfo.InvariantCulture);
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
        if (!Regex.IsMatch(name, @"^\w+$") || !Regex.IsMatch(receiver, @"^\w+$") ||
            (mux.Length > 0 && mux != "M" && !Regex.IsMatch(mux, @"^m\d+$")))
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
