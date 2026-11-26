using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// Day-to-day station setup has one simple page. Detailed hardware,
/// model/identity rule tables and immutable-version diagnostics stay in
/// secondary tabs for installation and engineering, never the operator path.
/// </summary>
public partial class ProductionPreparationWindow
{
    private TabControl? _stationDetailTabs;

    private void OpenStationDetail(int detailIndex)
    {
        if (_stationDetailTabs is null) return;
        _stationDetailTabs.SelectedIndex = detailIndex;
        PreparationTabs.SelectedIndex = 1;
    }

    private void ConfigureProductPreparationWorkspace()
    {
        if (PreparationTabs.Items.Count != 5 ||
            PreparationTabs.Items.Cast<object>().Any(item => item is not TabItem))
            throw new InvalidOperationException(
                "工位配置编辑器布局改变，不能静默丢失硬件或版本配置。");

        var original = PreparationTabs.Items.Cast<TabItem>().ToArray();
        PreparationTabs.Items.Clear();

        var identityEditors = new TabControl
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(220, 230, 240)),
            Margin = new Thickness(7)
        };
        original[0].Header = "型号与工程";
        original[1].Header = "条码格式";
        original[2].Header = "编号登记表";
        foreach (var item in original.Take(3))
            identityEditors.Items.Add(item);
        identityEditors.SelectedIndex = 0;

        _stationDetailTabs = new TabControl
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(220, 230, 240)),
            Margin = new Thickness(7)
        };
        original[3].Header = "扫码枪与夹具硬件";
        _stationDetailTabs.Items.Add(original[3]);
        _stationDetailTabs.Items.Add(new TabItem
        {
            Header = "高级型号 / 编号规则",
            Content = identityEditors
        });
        original[4].Header = "版本与核验详情";
        _stationDetailTabs.Items.Add(original[4]);
        _stationDetailTabs.SelectedIndex = 0;

        PreparationTabs.Items.Add(new TabItem
        {
            Header = "基本设置",
            Content = CreateGuidedOverview()
        });
        PreparationTabs.Items.Add(new TabItem
        {
            Header = "高级设置",
            Content = _stationDetailTabs
        });
        PreparationTabs.SelectedIndex = 0;
    }
}
