using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AutoTest.Desktop.Testing;

public partial class TestProjectWorkbenchView : UserControl
{
    public ContentControl InspectorHost { get; } = new()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch
    };

    private readonly Action? _openDependencyReview;
    public TestProjectWorkbenchView(TestProjectWorkbenchViewModel viewModel,
        Action? openDependencyReview = null)
    {
        _openDependencyReview = openDependencyReview;
        InitializeComponent();
        DataContext = viewModel;
        InspectorHost.Content = new TestProjectSelectionInspector(viewModel);
    }
    private void OpenDependencyReview_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _openDependencyReview?.Invoke();
    }

    private void PackagesGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left &&
            DataContext is TestProjectWorkbenchViewModel vm)
            vm.OpenSelectedPackage();
    }
    private void OpenPackage_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is TestProjectWorkbenchViewModel vm &&
            sender is Button { Tag: TestProjectPackageRow row })
            vm.OpenPackage(row);
    }
    private async void AdoptCurrentRevision_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is not TestProjectWorkbenchViewModel vm ||
            sender is not Button { Tag: TestProjectPackageRow row } ||
            !row.CanAdoptLatest) return;
        var asset = vm.CurrentAssetFor(row);
        if (asset is null)
        {
            MessageBox.Show(Window.GetWindow(this),
                "TEST 源资源已经不存在，禁止猜测或自动采用。",
                "资源已失效", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var (allowed, summary) = await vm.ReviewCurrentRevisionAsync(
            row, asset.Revision, asset.Sha256);
        if (!allowed)
        {
            MessageBox.Show(Window.GetWindow(this), summary,
                "新版 TEST 预检未通过", MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }
        var answer = MessageBox.Show(Window.GetWindow(this),
            summary + "\n\n是否明确采用这个新修订？",
            "检查新版 TEST 定义 · 采用修订",
            MessageBoxButton.YesNo, MessageBoxImage.Question,
            MessageBoxResult.No);
        if (answer == MessageBoxResult.Yes)
            vm.AdoptCurrentRevision(row, asset.Revision, asset.Sha256);
    }

    private void RestoreLegacyInclusion_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is not TestProjectWorkbenchViewModel vm ||
            sender is not Button { Tag: TestProjectPackageRow row } ||
            !vm.Packages.Contains(row) || row.Enabled) return;
        // This only repairs a historical persisted exclusion. Normal project
        // editing offers no second per-case production enable switch.
        var result = MessageBox.Show(Window.GetWindow(this),
            "测试项目中的每条有序用例实例都属于正式检测。\n\n" +
            "旧工程中的「" + row.Name + "」当前被标记为排除。\n" +
            "恢复后本条将参与工程完整检测；系统自动保存，运行前需重新完成资源检查。\n\n" +
            "确认恢复这条历史排除记录？",
            "恢复完整检测顺序", MessageBoxButton.YesNo,
            MessageBoxImage.Warning, MessageBoxResult.No);
        if (result == MessageBoxResult.Yes)
            row.Enabled = true;
    }

    private void MoveRowUp_Click(object sender, RoutedEventArgs e) => Move(sender, -1, e);
    private void MoveRowDown_Click(object sender, RoutedEventArgs e) => Move(sender, 1, e);
    private void Move(object sender, int direction, RoutedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is TestProjectWorkbenchViewModel vm &&
            sender is Button { Tag: TestProjectPackageRow row })
            vm.MoveRow(row, direction);
    }
    private async void RemoveRow_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is TestProjectWorkbenchViewModel vm &&
            sender is Button { Tag: TestProjectPackageRow row })
            await vm.RemoveRowAsync(row);
    }
}
