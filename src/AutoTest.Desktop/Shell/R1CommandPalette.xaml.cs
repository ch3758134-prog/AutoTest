using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AutoTest.Desktop.Shell;

public sealed record R1PaletteCommand(string Key, string Icon, string Title, string Hint);

/// <summary>R1 HTML Ctrl+K command palette, backed by the same product navigation.</summary>
public partial class R1CommandPalette : Window
{
    private readonly Action<string> _execute;
    private readonly List<R1PaletteCommand> _all =
    [
        new("project","▦","工程中心","Project / Explorer"),
        new("station","⎔","设备连接","USB / Station"),
        new("can","≋","CAN Monitor","CAN / RX Trace"),
        new("dbc","▤","DBC Editor","Signals / DBC"),
        new("controller","⊞","控制器设计","Controller / Mapping"),
        new("bench","⌘","Manual Bench","Station / Manual"),
        new("wave","∿","Wave Studio","Capture / Compare"),
        new("uds","⇄","UDS Console","Diagnostics / ISO-TP"),
        new("flash","⇧","CAN Flash","ECU Programming"),
        new("program","⑂","测试程序编辑","Test / Authoring"),
        new("run","▷","运行与调试","Execution / Debug"),
        new("production","▣","扫码出厂","Production / EOL"),
        new("records","▥","记录与报告","Records / Reports"),
        new("operations","⌗","操作审计","Operations / Audit")
    ];
    public R1CommandPalette(Action<string> execute)
    {
        _execute=execute ?? throw new ArgumentNullException(nameof(execute));
        InitializeComponent();
        ApplyFilter("");
    }
    private void Palette_Loaded(object sender,RoutedEventArgs e) => Query.Focus();
    private void Query_TextChanged(object sender,TextChangedEventArgs e) =>
        ApplyFilter(Query.Text);
    private void ApplyFilter(string query)
    {
        if (Commands is null) return;
        string filter=query.Trim();
        Commands.ItemsSource=_all.Where(c=>string.IsNullOrEmpty(filter)||
            (c.Title+" "+c.Hint+" "+c.Key).Contains(filter,StringComparison.OrdinalIgnoreCase)).ToArray();
        if (Commands.Items.Count>0) Commands.SelectedIndex=0;
    }
    private void ExecuteSelected()
    {
        if (Commands.SelectedItem is not R1PaletteCommand cmd) return;
        Close();
        _execute(cmd.Key);
    }
    private void Palette_PreviewKeyDown(object sender,KeyEventArgs e)
    {
        if (e.Key==Key.Escape) {Close();e.Handled=true;return;}
        if (e.Key==Key.Return) {ExecuteSelected();e.Handled=true;return;}
        if (e.Key==Key.Down && Query.IsKeyboardFocused)
        {
            Commands.Focus();
            Commands.SelectedIndex=Math.Min(Commands.SelectedIndex+1,Commands.Items.Count-1);
            e.Handled=true;
        }
    }
    private void Commands_MouseDoubleClick(object sender,MouseButtonEventArgs e) => ExecuteSelected();
    private void Close_Click(object sender,RoutedEventArgs e) => Close();
}
