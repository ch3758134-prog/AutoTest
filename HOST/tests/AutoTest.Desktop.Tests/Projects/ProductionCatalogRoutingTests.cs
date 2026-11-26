using AutoTest.Desktop.Projects;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AutoTest.Desktop.Tests.Projects;

/// <summary>
/// Pure routing tests: synthetic catalog metadata has no real ECU firmware and
/// is NEVER passed to package verification or a hardware executor.
/// </summary>
public sealed class ProductionCatalogRoutingTests
{
    [Fact]
    public async Task ProductFieldSelectsExactlyTheConfiguredVersion()
    {
        var root = TempDirectory();
        try
        {
            var catalog = new ProductionProjectCatalog(root);
            await catalog.GetSettingsAsync();
            var a = new string('A', 64);
            var b = new string('B', 64);
            await AddRoutingOnlyPackageAsync(root, a, "HydEPB-A");
            await AddRoutingOnlyPackageAsync(root, b, "HydEPB-B");
            await catalog.BindAsync("EPB-A", "", a);
            await catalog.BindAsync("EPB-B", "", b);
            await catalog.SaveSettingsAsync(new(
                ProductionRoutingSettings.Fields, "|", 1, 2, 0));
            var first = await catalog.ResolveAsync("EPB-A|S123");
            var second = await catalog.ResolveAsync("EPB-B|S123");
            Assert.True(first.Accepted);
            Assert.True(second.Accepted);
            Assert.Equal("S123", first.Serial);
            Assert.Equal(a, first.Package!.PackageSha256);
            Assert.Equal(b, second.Package!.PackageSha256);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task SingleModeRequiresOneExplicitTargetWhenManyVersionsExist()
    {
        var root = TempDirectory();
        try
        {
            var catalog = new ProductionProjectCatalog(root);
            await catalog.GetSettingsAsync();
            var a = new string('A', 64);
            var b = new string('B', 64);
            await AddRoutingOnlyPackageAsync(root, a, "ECU-A");
            await AddRoutingOnlyPackageAsync(root, b, "ECU-B");
            var noChoice = await catalog.ResolveAsync("S123");
            Assert.False(noChoice.Accepted);
            await catalog.BindAsync("SINGLE", "", b);
            var selected = await catalog.ResolveAsync("S123");
            Assert.True(selected.Accepted);
            Assert.Equal(b, selected.Package!.PackageSha256);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static async Task AddRoutingOnlyPackageAsync(
        string root, string sha, string name)
    {
        await using var db = new SqliteConnection(
            "Data Source=" + Path.Combine(root, "catalog.sqlite"));
        await db.OpenAsync();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO packages
                (package_sha,project_id,project_name,revision,manifest_path,installed_at)
            VALUES ($sha,$id,$name,1,$path,$at)
            """;
        cmd.Parameters.AddWithValue("$sha", sha);
        cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$path", Path.Combine(root, sha,
            ProductionProjectPackageService.ProjectManifestName));
        cmd.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }

    private static string TempDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "autotest-routes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
