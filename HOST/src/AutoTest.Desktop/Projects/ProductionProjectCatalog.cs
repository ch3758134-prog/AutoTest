using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace AutoTest.Desktop.Projects;

public sealed record ProductionRoutingSettings(
    string Mode, string Separator, int ProductField, int SerialField, int HardwareField)
{
    public const string Single = "Single";
    public const string Fields = "Fields";
    public const string Registry = "Registry";

    public static ProductionRoutingSettings Default => new(Single, "|", 1, 2, 0);

    public void Validate()
    {
        if (Mode is not (Single or Fields or Registry) ||
            string.IsNullOrEmpty(Separator) || Separator.Length > 4 ||
            Separator.Any(char.IsControl) ||
            ProductField is < 1 or > 32 || SerialField is < 1 or > 32 ||
            HardwareField is < 0 or > 32 ||
            (Mode == Fields && (ProductField == SerialField ||
                HardwareField > 0 && (HardwareField == ProductField ||
                    HardwareField == SerialField))))
            throw new InvalidDataException("扫码字段设置无效，请检查分隔符和字段位置。");
    }
}

public sealed record ProductionCatalogRoute(
    bool Accepted, string Code, string Message, string Raw, string Serial,
    string ProductKey, string Hardware, InstalledProductionPackage? Package);

/// <summary>
/// Offline multi-project catalog. Importing a package does not authorize any
/// physical test. Only explicitly bound, unambiguous routes can select a
/// version; the current station and hardware remain separate concerns.
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
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = DatabasePath }.ToString());
        await connection.OpenAsync(ct);
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
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
                CREATE TABLE IF NOT EXISTS routes(
                    product_key TEXT NOT NULL,
                    hardware TEXT NOT NULL,
                    package_sha TEXT NOT NULL REFERENCES packages(package_sha),
                    PRIMARY KEY(product_key,hardware)
                );
                CREATE TABLE IF NOT EXISTS serial_registry(
                    serial TEXT PRIMARY KEY,
                    product_key TEXT NOT NULL,
                    hardware TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS settings(
                    id INTEGER PRIMARY KEY CHECK (id = 1),
                    mode TEXT NOT NULL,
                    separator TEXT NOT NULL,
                    product_field INTEGER NOT NULL,
                    serial_field INTEGER NOT NULL,
                    hardware_field INTEGER NOT NULL
                );
                """;
            await command.ExecuteNonQueryAsync(ct);
            return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    public async Task<IReadOnlyList<InstalledProductionPackage>> ListAsync(
        CancellationToken ct = default)
    {
        await using var db = await ConnectAsync(ct);
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT package_sha,project_id,project_name,revision,manifest_path,installed_at " +
                          "FROM packages ORDER BY project_name,revision DESC,installed_at DESC";
        var items = new List<InstalledProductionPackage>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) items.Add(ReadPackage(reader));
        return items;
    }

    public async Task<InstalledProductionPackage> ImportAsync(
        string archivePath, CancellationToken ct = default)
    {
        await _writeGate.WaitAsync(ct);
        try
        {
            // Complete validation and immutable install occur before catalog
            // registration. A failed import cannot select a partial package.
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

    public async Task<ProductionRoutingSettings> GetSettingsAsync(
        CancellationToken ct = default)
    {
        await using var db = await ConnectAsync(ct);
        return await ReadSettingsAsync(db, ct);
    }

    public async Task SaveSettingsAsync(ProductionRoutingSettings settings,
        CancellationToken ct = default)
    {
        settings.Validate();
        await _writeGate.WaitAsync(ct);
        try
        {
            await using var db = await ConnectAsync(ct);
            using var cmd = db.CreateCommand();
            cmd.CommandText = """
                INSERT INTO settings(id,mode,separator,product_field,serial_field,hardware_field)
                VALUES (1,$mode,$sep,$product,$serial,$hardware)
                ON CONFLICT(id) DO UPDATE SET
                mode=excluded.mode,separator=excluded.separator,
                product_field=excluded.product_field,serial_field=excluded.serial_field,
                hardware_field=excluded.hardware_field
                """;
            cmd.Parameters.AddWithValue("$mode", settings.Mode);
            cmd.Parameters.AddWithValue("$sep", settings.Separator);
            cmd.Parameters.AddWithValue("$product", settings.ProductField);
            cmd.Parameters.AddWithValue("$serial", settings.SerialField);
            cmd.Parameters.AddWithValue("$hardware", settings.HardwareField);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally { _writeGate.Release(); }
    }

    public async Task BindAsync(string productKey, string hardware,
        string packageSha, CancellationToken ct = default)
    {
        var product = NormalizeProduct(productKey);
        var hw = NormalizeHardware(hardware);
        if (!ValidHash(packageSha)) throw new InvalidDataException("请选择已导入的工程版本。");
        await _writeGate.WaitAsync(ct);
        try
        {
            await using var db = await ConnectAsync(ct);
            using var cmd = db.CreateCommand();
            cmd.CommandText = """
                INSERT INTO routes(product_key,hardware,package_sha)
                VALUES($product,$hardware,$sha)
                ON CONFLICT(product_key,hardware) DO UPDATE
                    SET package_sha=excluded.package_sha
                """;
            cmd.Parameters.AddWithValue("$product", product);
            cmd.Parameters.AddWithValue("$hardware", hw);
            cmd.Parameters.AddWithValue("$sha", packageSha);
            try { await cmd.ExecuteNonQueryAsync(ct); }
            catch (SqliteException error) when (error.SqliteErrorCode == 19)
            { throw new InvalidDataException("请选择工程库中已安装的版本。", error); }
        }
        finally { _writeGate.Release(); }
    }

    public async Task<IReadOnlyList<(string Product, string Hardware, string PackageSha)>>
        ListBindingsAsync(CancellationToken ct = default)
    {
        await using var db = await ConnectAsync(ct);
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT product_key,hardware,package_sha FROM routes ORDER BY product_key,hardware";
        var rows = new List<(string, string, string)>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        return rows;
    }

    public async Task SetRegistryIdentityAsync(
        string serial, string productKey, string hardware = "",
        CancellationToken ct = default)
    {
        ValidateSerial(serial);
        var product = NormalizeProduct(productKey);
        var hw = NormalizeHardware(hardware);
        await _writeGate.WaitAsync(ct);
        try
        {
            await using var db = await ConnectAsync(ct);
            using var cmd = db.CreateCommand();
            cmd.CommandText = """
                INSERT INTO serial_registry(serial,product_key,hardware)
                VALUES($serial,$product,$hardware)
                ON CONFLICT(serial) DO UPDATE SET
                product_key=excluded.product_key,hardware=excluded.hardware
                """;
            cmd.Parameters.AddWithValue("$serial", serial);
            cmd.Parameters.AddWithValue("$product", product);
            cmd.Parameters.AddWithValue("$hardware", hw);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally { _writeGate.Release(); }
    }

    public async Task<ProductionCatalogRoute> ResolveAsync(
        string raw, CancellationToken ct = default)
    {
        ProductionCatalogRoute Reject(string code, string message) =>
            new(false, code, message, raw, "", "", "", null);
        if (string.IsNullOrEmpty(raw) || raw.Length > 256 ||
            raw != raw.Trim() || raw.Any(char.IsControl))
            return Reject("SCAN_INVALID", "编号为空、过长或含有不可见字符。");
        await using var db = await ConnectAsync(ct);
        var settings = await ReadSettingsAsync(db, ct);
        settings.Validate();

        string serial, product, hardware;
        if (settings.Mode == ProductionRoutingSettings.Single)
        {
            serial = raw; product = ""; hardware = "";
            using var count = db.CreateCommand();
            count.CommandText = """
                SELECT p.package_sha,p.project_id,p.project_name,p.revision,
                    p.manifest_path,p.installed_at
                FROM routes r JOIN packages p ON r.package_sha=p.package_sha
                WHERE r.product_key='SINGLE' AND r.hardware=''
                """;
            var packages = new List<InstalledProductionPackage>();
            await using (var reader = await count.ExecuteReaderAsync(ct))
                while (await reader.ReadAsync(ct)) packages.Add(ReadPackage(reader));
            if (packages.Count == 0)
            {
                // When exactly one package is installed, no setup step is
                // needed. Once multiple versions exist, select explicitly.
                using var only = db.CreateCommand();
                only.CommandText = """
                    SELECT package_sha,project_id,project_name,revision,
                        manifest_path,installed_at FROM packages
                    """;
                await using var reader = await only.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct)) packages.Add(ReadPackage(reader));
            }
            if (packages.Count != 1)
                return Reject("SINGLE_NOT_UNIQUE",
                    "请在工程管理中指定唯一一份当前检测工程。");
            try { ValidateSerial(serial); }
            catch (InvalidDataException) { return Reject("SERIAL_INVALID", "ECU 编号格式无效。"); }
            return new(true, "MATCHED", "已找到对应工程。", raw,
                serial, "", "", packages[0]);
        }

        if (settings.Mode == ProductionRoutingSettings.Fields)
        {
            var fields = raw.Split([settings.Separator], StringSplitOptions.None);
            if (fields.Length < Math.Max(settings.ProductField,
                    Math.Max(settings.SerialField, settings.HardwareField)))
                return Reject("FIELDS_MISSING", "扫码内容缺少已设置的识别字段。");
            serial = fields[settings.SerialField - 1];
            product = fields[settings.ProductField - 1];
            hardware = settings.HardwareField == 0 ? "" : fields[settings.HardwareField - 1];
        }
        else
        {
            serial = raw;
            using var lookup = db.CreateCommand();
            lookup.CommandText = "SELECT product_key,hardware FROM serial_registry WHERE serial=$serial";
            lookup.Parameters.AddWithValue("$serial", serial);
            await using var rd = await lookup.ExecuteReaderAsync(ct);
            if (!await rd.ReadAsync(ct))
                return Reject("SERIAL_UNREGISTERED",
                    "此编号尚未登记产品归属，请检查编号表。");
            product = rd.GetString(0);
            hardware = rd.GetString(1);
        }
        try
        {
            ValidateSerial(serial);
            product = NormalizeProduct(product);
            hardware = NormalizeHardware(hardware);
        }
        catch (InvalidDataException)
        { return Reject("FIELDS_INVALID", "扫码编号或产品识别字段格式无效。"); }

        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT p.package_sha,p.project_id,p.project_name,p.revision,
                p.manifest_path,p.installed_at
            FROM routes r JOIN packages p ON r.package_sha=p.package_sha
            WHERE r.product_key=$product AND r.hardware=$hardware
            """;
        cmd.Parameters.AddWithValue("$product", product);
        cmd.Parameters.AddWithValue("$hardware", hardware);
        var matches = new List<InstalledProductionPackage>();
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) matches.Add(ReadPackage(reader));
        if (matches.Count != 1)
            return Reject("PROJECT_NOT_MAPPED",
                "未找到与此编号对应的检测工程，请检查工程关联。");
        return new(true, "MATCHED", "已找到对应工程。", raw,
            serial, product, hardware, matches[0]);
    }

    public async Task VerifySelectionAsync(
        InstalledProductionPackage selection, CancellationToken ct = default)
    {
        // Data in the index is not executable authority. Reload and compare
        // the version files immediately before adopting a new production scope.
        var dir = Path.GetDirectoryName(selection.ManifestPath)!;
        var metadataPath = Path.Combine(dir, ProductionProjectPackageService.MetadataEntry);
        var bytes = await File.ReadAllBytesAsync(metadataPath, ct);
        var header = JsonSerializer.Deserialize<ProductionPackageManifest>(bytes, _json)
            ?? throw new InvalidDataException("已安装工程包信息无效。");
        if (header.ProjectId != selection.ProjectId ||
            !string.Equals(Path.GetFileName(dir), selection.PackageSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("工程库索引与实际文件不一致。");
        await ProductionProjectPackageService.ValidateInstalledAsync(dir, header, ct);
    }

    private static async Task<ProductionRoutingSettings> ReadSettingsAsync(
        SqliteConnection db, CancellationToken ct)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT mode,separator,product_field,serial_field,hardware_field " +
                          "FROM settings WHERE id=1";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return ProductionRoutingSettings.Default;
        return new(reader.GetString(0), reader.GetString(1), reader.GetInt32(2),
            reader.GetInt32(3), reader.GetInt32(4));
    }

    private static InstalledProductionPackage ReadPackage(System.Data.Common.DbDataReader reader) =>
        new(Guid.Parse(reader.GetString(1)), reader.GetString(2),
            reader.GetInt32(3), reader.GetString(0), reader.GetString(4),
            DateTimeOffset.Parse(reader.GetString(5),
                System.Globalization.CultureInfo.InvariantCulture));

    private static string NormalizeProduct(string value)
    {
        var key = (value ?? "").Trim().ToUpperInvariant();
        if (!Regex.IsMatch(key, @"\A[A-Z0-9_-]{1,64}\z",
                RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            throw new InvalidDataException("产品识别码只能包含字母、数字、- 和 _。");
        return key;
    }

    private static string NormalizeHardware(string value)
    {
        var code = (value ?? "").Trim().ToUpperInvariant();
        if (code.Length != 0 && !Regex.IsMatch(code, @"\A[A-Z0-9_.-]{1,64}\z",
                RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            throw new InvalidDataException("硬件版本格式不正确。");
        return code;
    }

    private static void ValidateSerial(string value)
    {
        if (!Regex.IsMatch(value ?? "", @"\A[A-Za-z0-9][A-Za-z0-9_.-]{0,63}\z",
                RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            throw new InvalidDataException("ECU 唯一编号格式无效。");
    }
    private static bool ValidHash(string? hash) =>
        hash is { Length: 64 } && hash.All(Uri.IsHexDigit);
}
