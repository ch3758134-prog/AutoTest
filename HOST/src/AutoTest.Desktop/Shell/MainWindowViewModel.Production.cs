using Prism.Commands;

namespace AutoTest.Desktop.Shell;

/// <summary>UI-01: operator-first automatic test screen; developer tools are a secondary entry.</summary>
public sealed partial class MainWindowViewModel
{
    private ProductionInspectionViewModel _productionInspection = null!;
    private bool _productionWorkspace = true;

    public ProductionInspectionView ProductionView { get; private set; } = null!;
    public bool IsProductionWorkspace => _productionWorkspace;
    public bool IsEngineerWorkspace => !_productionWorkspace;
    public string WindowTitle => IsProductionWorkspace ? "AutoTest · 自动测试" : "AutoTest · 开发与调试";
    public DelegateCommand ShowProductionWorkspaceCommand { get; private set; } = null!;
    public DelegateCommand ShowEngineerWorkspaceCommand { get; private set; } = null!;

    private void InitializeProductionWorkspace()
    {
        _productionInspection = new ProductionInspectionViewModel(
            _workspace, _scannerWorkbench, _realTest, CanRecords, OpenSerialScannerWindow);
        ProductionView = new ProductionInspectionView
        {
            DataContext = _productionInspection
        };
        ShowProductionWorkspaceCommand = new DelegateCommand(() => SetWorkspaceMode(true));
        ShowEngineerWorkspaceCommand = new DelegateCommand(() => SetWorkspaceMode(false));
    }

    private void SetWorkspaceMode(bool production)
    {
        if (_productionWorkspace == production) return;
        _productionWorkspace = production;
        RaisePropertyChanged(nameof(IsProductionWorkspace));
        RaisePropertyChanged(nameof(IsEngineerWorkspace));
        RaisePropertyChanged(nameof(WindowTitle));
        StatusMessage = production
            ? "已进入自动测试；正式扫码启动、前置烧录与报告执行链尚待接入。"
            : "已进入开发与调试；工程与编辑器保持原状态。";
    }
}
