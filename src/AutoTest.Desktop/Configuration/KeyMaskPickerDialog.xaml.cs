using System.Windows;
using System.Windows.Controls;

namespace AutoTest.Desktop.Configuration;

/// <summary>20 physical KEY_SIM switches; independent from the number of ECU operations.</summary>
public partial class KeyMaskPickerDialog : Window
{
    private readonly CheckBox[] _keys = new CheckBox[20];
    public uint SelectedMask { get; private set; }

    public KeyMaskPickerDialog(uint initialMask, string heading)
    {
        if (initialMask > 0xFFFFF)
            throw new ArgumentOutOfRangeException(nameof(initialMask));
        SelectedMask = initialMask;
        InitializeComponent();
        Heading.Text = heading;
        for (var i = 0; i < _keys.Length; i++)
        {
            var toggle = new CheckBox
            {
                Content = "Key " + (i + 1).ToString("D2"),
                IsChecked = (initialMask & (1u << i)) != 0,
                Margin = new Thickness(7, 8, 7, 8),
                VerticalAlignment = VerticalAlignment.Center
            };
            toggle.Checked += (_, _) => Refresh();
            toggle.Unchecked += (_, _) => Refresh();
            _keys[i] = toggle;
            KeyGrid.Children.Add(toggle);
        }
        Refresh();
    }

    private uint CurrentMask()
    {
        uint mask = 0;
        for (var i = 0; i < _keys.Length; i++)
            if (_keys[i]?.IsChecked == true) mask |= 1u << i;
        return mask;
    }

    private void Refresh() => MaskPreview.Text = "选中 " +
        _keys.Count(k => k?.IsChecked == true) +
        " 个电阻开关    KeyMask：0x" + CurrentMask().ToString("X5");

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        foreach (var key in _keys) key.IsChecked = false;
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        SelectedMask = CurrentMask();
        DialogResult = true;
    }
}
