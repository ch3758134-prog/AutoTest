using Prism.Commands;

namespace AutoTest.Desktop.Shell;

/// <summary>Separate production read-only screen; engineer tabs remain intact.</summary>
public sealed partial class MainWindowViewModel
{
    private ProductionInspectionViewModel _productionInspection = null!;
    private bool _productionWorkspace;

    public ProductionInspectionView ProductionView { get; private set; } = null!;
    public bool IsProductionWorkspace => _productionWorkspace;
    public bool IsEngineerWorkspace => !_productionWorkspace;
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
        StatusMessage = production
            ? "生产监视工作区已打开。正式生产物理执行链尚未接通；当前只展示真实工程、扫码预检及研发 TEST 观察。"
            : "已返回工程师工作区；编辑器与调试状态保留。";
    }
}
