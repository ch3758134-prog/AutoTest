using AutoTest.Desktop.Projects;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AutoTest.Desktop.Tests.Projects;

/// <summary>Pure routing tests; synthetic package rows never reach a physical ECU.</summary>
public sealed class ProductionCatalogRoutingTests
{
    [Fact]
    public async Task ExactLeadingMarkerPicksCorrectWholeProjectAndPreservesRawCode()
    {
        var root = TempDirectory();
        try
        {
            var catalog = new ProductionProjectCatalog(root);
            await catalog.ListAsync();
            var a = new string('A', 64);
            var b = new string('B', 64);
            await AddRoutingOnlyPackageAsync(root, a, "HydEPB-A");
            await AddRoutingOnlyPackageAsync(root, b, "HydEPB-B");
            await catalog.BindMarkerAsync("EPB1", a);
            await catalog.BindMarkerAsync("EPB10", b);
            var first = await catalog.ResolveAsync("EPB1-000001");
            var second = await catalog.ResolveAsync("EPB10-000001");
            Assert.True(first.Accepted);
            Assert.True(second.Accepted);
            Assert.Equal("EPB1-000001", first.FullBarcode);
            Assert.Equal("EPB1", first.Marker);
            Assert.Equal(a, first.Package!.PackageSha256);
            Assert.Equal(b, second.Package!.PackageSha256);
            Assert.Equal("MARKER_UNBOUND",
                (await catalog.ResolveAsync("UNLISTED-000001")).Code);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RebindingMarkerChoosesOneNewVersionAndIncrementsRevision()
    {
        var root = TempDirectory();
        try
        {
            var catalog = new ProductionProjectCatalog(root);
            await catalog.ListAsync();
            var firstSha = new string('A', 64);
            var newerSha = new string('B', 64);
            await AddRoutingOnlyPackageAsync(root, firstSha, "HydEPB");
            await AddRoutingOnlyPackageAsync(root, newerSha, "HydEPB");
            await catalog.BindMarkerAsync("HYDEP", firstSha);
            var older = await catalog.ResolveAsync("HYDEP-00001");
            await catalog.BindMarkerAsync("HYDEP", newerSha);
            var newer = await catalog.ResolveAsync("HYDEP-00001");
            Assert.True(older.Accepted);
            Assert.True(newer.Accepted);
            Assert.Equal(1, older.RuleRevision);
            Assert.Equal(2, newer.RuleRevision);
            Assert.Equal(newerSha, newer.Package!.PackageSha256);
            Assert.Equal("HYDEP-00001", newer.FullBarcode);
            Assert.Single(await catalog.ListBindingsAsync());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RemovingMarkerBlocksFutureScansButKeepsInstalledPackages()
    {
        var root = TempDirectory();
        try
        {
            var catalog = new ProductionProjectCatalog(root);
            await catalog.ListAsync();
            var sha = new string('A', 64);
            await AddRoutingOnlyPackageAsync(root, sha, "HydEPB");
            await catalog.BindMarkerAsync("HYDEP", sha);
            await catalog.RemoveMarkerAsync("HYDEP");
            Assert.Equal("MARKER_UNBOUND",
                (await catalog.ResolveAsync("HYDEP-0001")).Code);
            Assert.Single(await catalog.ListAsync());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task OldScannerModesAreDestroyedWithoutDeletingInstalledPackages()
    {
        var root = TempDirectory();
        try
        {
            var path = Path.Combine(root, "catalog.sqlite");
            await using (var db = new SqliteConnection("Data Source=" + path))
            {
                await db.OpenAsync();
                using var old = db.CreateCommand();
                old.CommandText = """
                    CREATE TABLE settings(id INTEGER PRIMARY KEY,mode TEXT);
                    INSERT INTO settings VALUES(1,'Registry');
                    CREATE TABLE serial_registry(serial TEXT PRIMARY KEY);
                    CREATE TABLE routes(product_key TEXT,package_sha TEXT);
                    """;
                await old.ExecuteNonQueryAsync();
            }
            var catalog = new ProductionProjectCatalog(root);
            Assert.Empty(await catalog.ListBindingsAsync());
            await using var check = new SqliteConnection("Data Source=" + path);
            await check.OpenAsync();
            using var cmd = check.CreateCommand();
            cmd.CommandText = """
                SELECT COUNT(*) FROM sqlite_master
                WHERE type='table' AND name IN ('settings','serial_registry','routes')
                """;
            Assert.Equal(0L, (long)(await cmd.ExecuteScalarAsync())!);
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
            "autotest-marker-routing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}