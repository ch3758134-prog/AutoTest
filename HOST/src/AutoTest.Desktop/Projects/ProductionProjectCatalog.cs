using System.IO;
using System.Text.Json;
using AutoTest.Application.Production;
using AutoTest.Projects.Project;
using Microsoft.Data.Sqlite;

namespace AutoTest.Desktop.Projects;

public sealed record ProductionCatalogRoute(
    bool Accepted, string Code, string Message, string FullBarcode,
    string Marker, InstalledProductionPackage? Package, int RuleRevision);

public sealed record ProductionMarkerBinding(string Marker, string PackageSha256,
    int Revision);

/// <summary>
/// Production alone owns installed package selection and one exact leading
/// barcode marker -> one active package version. No TEST/FLASH authoring,
/// per-serial registry, station fixture selection, or barcode parsing modes.
/// </summary>
public sealed class ProductionProjectCatalog
{
    private readonly string _root;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly JsonSerializerOptions _json = new();

    public ProductionProjectCatalog(string? root = null)
    {
        _root = Path.GetFullPath(root ??
            Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
                "AutoTest", "production-library"));
    }

    public string Root => _root;
    private string DatabasePath => Path.Combine(_root, "catalog.sqlite");
    private string PackagesRoot => Path.Combine(_root, "packages");

    private async Task<SqliteConnection> ConnectAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(_root);
        var db = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = DatabasePath,
                Mode = SqliteOpenMode.ReadWriteCreate
            }.ToString());
        await db.OpenAsync(ct);
        try
        {
            // Destroy obsolete routing authority, not historical user data.
            // Before the first schema cutover, preserve an independent
            // SQLite snapshot. Failure to back up prevents any DROP.
            using (var detect = db.CreateCommand())
            {
                detect.CommandText = """
                    SELECT COUNT(*) FROM sqlite_master WHERE type='table'
                    AND name IN ('routes','serial_registry','settings')
                    """;
                var legacy = Convert.ToInt64(await detect.ExecuteScalarAsync(ct));
                if (legacy > 0)
                {
                    var backupPath = Path.Combine(_root, "catalog-before-marker-" +
                        DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss") + "-" +
                        Guid.NewGuid().ToString("N") + ".sqlite");
                    await using var backup = new SqliteConnection(
                        new SqliteConnectionStringBuilder
                        {
                            DataSource = backupPath,
                            Mode = SqliteOpenMode.ReadWriteCreate
                        }.ToString());
                    await backup.OpenAsync(ct);
                    db.BackupDatabase(backup);
                }
            }
            using var cmd = db.CreateCommand();
            cmd.CommandText = """
                PRAGMA foreign_keys = ON;
                CREATE TABLE IF NOT EXISTS packages(
                    package_sha TEXT PRIMARY KEY,
                    project_id TEXT NOT NULL,
                    project_name TEXT NOT NULL,
                    revision INTEGER NOT NULL,
                    manifest_path TEXT NOT NULL,
                    installed_at TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_packages_project
                    ON packages(project_id,revision);
                CREATE TABLE IF NOT EXISTS marker_routes(
                    marker TEXT PRIMARY KEY,
                    package_sha TEXT NOT NULL REFERENCES packages(package_sha),
                    revision INTEGER NOT NULL CHECK(revision > 0)
                );
                -- Remove discarded R1 identity schemes. Old mappings cannot
                -- silently select an ECU programming package.
                DROP TABLE IF EXISTS routes;
                DROP TABLE IF EXISTS serial_registry;
                DROP TABLE IF EXISTS settings;
                PRAGMA user_version = 3;
                """;
            await cmd.ExecuteNonQueryAsync(ct);
            return db;
        }
        catch { await db.DisposeAsync(); throw; }
    }

    public async Task<IReadOnlyList<InstalledProductionPackage>> ListAsync(
        CancellationToken ct = default)
    {
        await using var db = await ConnectAsync(ct);
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT package_sha,project_id,project_name,revision,manifest_path,installed_at
            FROM packages ORDER BY project_name,revision DESC,installed_at DESC
            """;
        var items = new List<InstalledProductionPackage>();
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct)) items.Add(ReadPackage(rd));
        return items;
    }

    public async Task<InstalledProductionPackage> ImportAsync(
        string archivePath, CancellationToken ct = default)
    {
        await _writeGate.WaitAsync(ct);
        try
        {
            // Package integrity and complete TEST/FLASH closure are validated
            // before it becomes an eligible production version.
            var installed = await ProductionProjectPackageService.InstallAsync(
                archivePath, PackagesRoot, ct);
            await using var db = await ConnectAsync(ct);
            using var cmd = db.CreateCommand();
            cmd.CommandText = """
                INSERT OR IGNORE INTO packages
                  (package_sha,project_id,project_name,revision,manifest_path,installed_at)
                VALUES ($sha,$id,$name,$revision,$manifest,$at)
                """;
            cmd.Parameters.AddWithValue("$sha", installed.PackageSha256);
            cmd.Parameters.AddWithValue("$id", installed.ProjectId.ToString("D"));
            cmd.Parameters.AddWithValue("$name", installed.ProjectName);
            cmd.Parameters.AddWithValue("$revision", installed.ProjectRevision);
            cmd.Parameters.AddWithValue("$manifest", installed.ManifestPath);
            cmd.Parameters.AddWithValue("$at", installed.InstalledAtUtc.ToString("O"));
            await cmd.ExecuteNonQueryAsync(ct);
            return installed;
        }
        finally { _writeGate.Release(); }
    }

    public async Task<IReadOnlyList<ProductionMarkerBinding>> ListBindingsAsync(
        CancellationToken ct = default)
    {
        await using var db = await ConnectAsync(ct);
        using var cmd = db.CreateCommand();
        cmd.CommandText =
            "SELECT marker,package_sha,revision FROM marker_routes ORDER BY marker";
        var items = new List<ProductionMarkerBinding>();
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
            items.Add(new(rd.GetString(0), rd.GetString(1), rd.GetInt32(2)));
        return items;
    }

    /// <summary>
    /// Administrator explicitly binds one marker to an immutable installed
    /// package version. Binding the same marker to a new version invalidates
    /// existing pending barcode observations (via caller's change event).
    /// </summary>
    public async Task BindMarkerAsync(string marker, string packageSha,
        CancellationToken ct = default)
    {
        var normalized = FixedMarkerBarcode.NormalizeMarker(marker);
        if (!ValidHash(packageSha))
            throw new InvalidDataException("请选择已经导入的检测工程版本。");
        await _writeGate.WaitAsync(ct);
        try
        {
            await using var db = await ConnectAsync(ct);
            using var cmd = db.CreateCommand();
            cmd.CommandText = """
                INSERT INTO marker_routes(marker,package_sha,revision)
                VALUES($marker,$sha,1)
                ON CONFLICT(marker) DO UPDATE SET
                    package_sha=excluded.package_sha,
                    revision=marker_routes.revision+1
                """;
            cmd.Parameters.AddWithValue("$marker", normalized);
            cmd.Parameters.AddWithValue("$sha", packageSha);
            try { await cmd.ExecuteNonQueryAsync(ct); }
            catch (SqliteException error) when (error.SqliteErrorCode == 19)
            { throw new InvalidDataException("工程版本不在已安装工程库中。", error); }
        }
        finally { _writeGate.Release(); }
    }

    public async Task RemoveMarkerAsync(string marker,
        CancellationToken ct = default)
    {
        var normalized = FixedMarkerBarcode.NormalizeMarker(marker);
        await _writeGate.WaitAsync(ct);
        try
        {
            await using var db = await ConnectAsync(ct);
            using var cmd = db.CreateCommand();
            cmd.CommandText = "DELETE FROM marker_routes WHERE marker=$marker";
            cmd.Parameters.AddWithValue("$marker", normalized);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally { _writeGate.Release(); }
    }

    public async Task<ProductionCatalogRoute> ResolveAsync(
        string raw, CancellationToken ct = default)
    {
        ProductionCatalogRoute Reject(string code, string message) =>
            new(false, code, message, raw ?? "", "", null, 0);

        if (!FixedMarkerBarcode.TryParse(raw, out var barcode, out var failure) ||
            barcode is null)
            return Reject("BARCODE_INVALID", failure);

        await using var db = await ConnectAsync(ct);
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT p.package_sha,p.project_id,p.project_name,p.revision,
                   p.manifest_path,p.installed_at,r.revision
            FROM marker_routes r
            INNER JOIN packages p ON p.package_sha=r.package_sha
            WHERE r.marker=$marker
            """;
        cmd.Parameters.AddWithValue("$marker", barcode.Marker);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (!await rd.ReadAsync(ct))
            return Reject("MARKER_UNBOUND",
                "识别标记 " + barcode.Marker + " 尚未关联检测工程。");
        var selected = ReadPackage(rd);
        var revision = rd.GetInt32(6);
        // Database unique marker constraint makes ambiguous resolution
        // impossible; do not order or guess among two active versions.
        if (await rd.ReadAsync(ct))
            return Reject("ROUTE_AMBIGUOUS", "同一识别标记关联多个检测工程，已拒绝运行。");
        return new(true, "MATCHED", "已匹配检测工程。",
            barcode.FullBarcode, barcode.Marker, selected, revision);
    }

    public async Task VerifySelectionAsync(
        InstalledProductionPackage selection, CancellationToken ct = default)
    {
        var dir = Path.GetFullPath(Path.GetDirectoryName(selection.ManifestPath)!);
        ProjectPathPolicy.ValidateInsideRoot(PackagesRoot, dir);
        if (!string.Equals(Path.GetFileName(Path.GetDirectoryName(dir)),
                selection.ProjectId.ToString("N"), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFileName(selection.ManifestPath),
                ProductionProjectPackageService.ProjectManifestName,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("工程库安装目录与工程身份不匹配。");
        var metadata = Path.Combine(dir, ProductionProjectPackageService.MetadataEntry);
        var bytes = await File.ReadAllBytesAsync(metadata, ct);
        var header = JsonSerializer.Deserialize<ProductionPackageManifest>(bytes, _json)
            ?? throw new InvalidDataException("已安装工程包信息无效。");
        if (header.ProjectId != selection.ProjectId ||
            !string.Equals(Path.GetFileName(dir), selection.PackageSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("工程库索引与实际文件不一致。");
        await ProductionProjectPackageService.ValidateInstalledAsync(dir, header, ct);
    }

    private static InstalledProductionPackage ReadPackage(
        System.Data.Common.DbDataReader rd) =>
        new(Guid.Parse(rd.GetString(1)), rd.GetString(2),
            rd.GetInt32(3), rd.GetString(0), rd.GetString(4),
            DateTimeOffset.Parse(rd.GetString(5),
                System.Globalization.CultureInfo.InvariantCulture));

    private static bool ValidHash(string? hash) =>
        hash is { Length: 64 } && hash.All(Uri.IsHexDigit);
}
