using System.Text.Json;
using AutoTest.Desktop.Shell;
using Xunit;

namespace AutoTest.Desktop.Tests.Projects;

public sealed class ProductionInspectionArchiveTests
{
    [Fact]
    public void OneInspectionCountsAsOneUnitRegardlessOfNumberOfCases()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "autotest-inspections-" + Guid.NewGuid().ToString("N"));
        var started = DateTimeOffset.Now;
        var folder = ProductionInspectionArchive.FolderFor(started, root);
        Directory.CreateDirectory(folder);
        try
        {
            static void Save(string dir, DateTimeOffset startedAt,
                string outcome, DateTimeOffset? endedAt)
            {
                var id = Guid.NewGuid();
                File.WriteAllText(Path.Combine(dir, id.ToString("N") + ".json"),
                    JsonSerializer.Serialize(new
                    {
                        Mode = "PRODUCTION", VerificationId = id,
                        StartedUtc = startedAt, EndedUtc = endedAt,
                        Outcome = outcome,
                        Cases = new[] { "case1", "case2", "case3" }
                    }));
            }
            Save(folder, started, "生产检测 PASS", started.AddSeconds(12));
            Save(folder, started, "生产检测 FAIL", started.AddSeconds(12));
            Save(folder, started, "测试已完成，正在收尾并归档正式检测",
                started.AddSeconds(12));
            Save(folder, started, "执行中", null);
            var summary = ProductionInspectionArchive.ReadDay(started.LocalDateTime.Date,
                root);
            Assert.Equal(1, summary.Passed);
            Assert.Equal(1, summary.Failed);
            Assert.Equal(1, summary.Incomplete);
            Assert.Equal(1, summary.Active);
            Assert.Equal(3, summary.Finished);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
