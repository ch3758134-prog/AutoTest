using System.IO;
using AutoTest.Desktop.Projects;
using AutoTest.Projects.Project;
using Xunit;

namespace AutoTest.Desktop.Tests.Projects;

public sealed class TestSystemConfigurationTests
{
    [Fact]
    public async Task TcfAndFixedBench_AreIndependentOfflineDocuments()
    {
        var folder = Path.Combine(Path.GetTempPath(), "at-tcf-tbc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var workspace = new ProjectWorkspaceService();
            await workspace.CreateAsync("Sut", folder);
            var tcfAsset = await workspace.CreateConfigurationAsync(
                ProjectWorkspaceService.TestConfigurationKind, "LvTong");
            var benchAsset = await workspace.CreateConfigurationAsync(
                ProjectWorkspaceService.BenchConfigurationKind, "Station");
            var tcf = await workspace.OpenConfigurationAsync(tcfAsset.Id);
            var bench = await workspace.OpenConfigurationAsync(benchAsset.Id);
            Assert.IsType<TestSystemConfigurationDocument>(tcf.Document);
            var fixedBench = Assert.IsType<TestBenchConfigurationDocument>(bench.Document);
            Assert.Equal(0, TestBenchConfigurationDocument.KeySimCanPort);
            Assert.Equal(1, TestBenchConfigurationDocument.DutCanPort);
            Assert.Contains("WinUSB", TestBenchConfigurationDocument.StationType);
            fixedBench.Validate();

            Assert.Null(workspace.SelectedTestConfiguration);
            await workspace.SelectConfigurationAsync(tcfAsset.Id);
            await workspace.SelectConfigurationAsync(benchAsset.Id);
            Assert.Equal(tcfAsset.Id, workspace.SelectedTestConfiguration);
            Assert.Equal(benchAsset.Id, workspace.SelectedBenchConfiguration);
            Assert.NotNull(await workspace.ReadSelectedTestConfigurationAsync());
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void TcfDeclinesIncompletePinnedDbcReference()
    {
        var invalid = TestSystemConfigurationDocument.Create("ECU") with
        {
            DbcAssetId = Guid.NewGuid(),
            DbcRevision = 1
        };
        var error = Assert.Throws<ProjectDataException>(() => invalid.Validate());
        Assert.Equal("TCF_PIN", error.Code);
    }
}
