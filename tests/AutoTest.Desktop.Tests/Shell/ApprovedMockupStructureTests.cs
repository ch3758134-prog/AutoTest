using System.IO;
using Xunit;

namespace AutoTest.Desktop.Tests.Shell;

/// <summary>
/// Source-level regression guard for the approved light-blue nine-screen IDE.
/// Functional behavior is still covered by the product's normal WPF and model tests.
/// </summary>
public sealed class ApprovedMockupStructureTests
{
    [Fact]
    public void MainShellHasSingleDocumentHostAndDirectHardwareConnection()
    {
        var xaml = Load("src/AutoTest.Desktop/Shell/MainWindow.xaml");
        Assert.Contains("ExplorerDocumentHost", xaml);
        Assert.Contains("DocumentTabs", xaml);
        Assert.Contains("StationConnectButton", xaml);
        Assert.DoesNotContain("ExplorerTabs", xaml);
        Assert.DoesNotContain("WorkbenchTabs", xaml);
        Assert.Contains("专业工具", xaml);
    }

    [Fact]
    public void EditorHasRealProgramTabsSequenceInspectorAndVisibleResultsDock()
    {
        var xaml = Load("src/AutoTest.Desktop/Tools/Testing/LabTestWorkspaceView.xaml");
        foreach (var contract in new[]
        {
            "程序编辑", "运行配置", "程序属性", "ProgramTree",
            "步骤设置", "ParameterRows", "执行结果", "追踪记录",
            "ExpandAllCommand", "CollapseAllCommand"
        })
            Assert.Contains(contract, xaml);
        Assert.DoesNotContain("Grid.Row=\"3\" Header=\"", xaml);
    }

    [Fact]
    public void RunnerUsesRealCasePlanProgressAndCurrentStepContext()
    {
        var xaml = Load("src/AutoTest.Desktop/Tools/Testing/R1ProjectRunView.xaml");
        foreach (var contract in new[]
        {
            "TestCases", "RunTimelineRows", "RunProgressPercent", "RunStepRows",
            "SelectedRunStep", "Facts", "诊断追踪"
        })
            Assert.Contains(contract, xaml);
    }

    [Fact]
    public void IndependentToolsKeepTheSharedChromeButNeverMoveIntoEditorTab()
    {
        var code = Load("src/AutoTest.Desktop/Shell/MainWindow.xaml.cs");
        Assert.Contains("BuildProfessionalChrome", code);
        Assert.Contains("OpenProfessionalToolWindow", code);
        Assert.Contains("R1ToolWindowPolicy.IsProfessionalTool", code);
        Assert.Contains("Owner = this", code);
        Assert.Contains("viewHost.Child = null", code);
    }

    private static string Load(string relative)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName,
                relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        throw new FileNotFoundException("Repository UI contract file missing", relative);
    }
}
