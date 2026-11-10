using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using AutoTest.Core;
using AutoTest.Projects.Dbc;
using AutoTest.Projects.Project;
using AutoTest.Projects.Resources;
using AutoTest.Testing.Authoring;
using AutoTest.Testing.Preparation;
using AutoTest.Testing.Runtime;
using AutoTest.Testing.Lab;

namespace AutoTest.Application.V1;

/// <summary>
/// V1 offline authoring composition. Holds the full PROJECT definition and TEST document;
/// deliberately does not implement the lossy ProjectSnapshot/PreparedPlan DTO projections.
/// No hardware services, discovery or physical output paths are referenced here.
/// </summary>
public sealed class V1Workspace
{
    private readonly Dictionary<AssetId, byte[]> _contents = new();
    private readonly Dictionary<AssetId, string> _persistedAssetHashes = new();
    private readonly Dictionary<AssetId, string> _persistedAssetPaths = new();
    private readonly RunCoordinator _runner = new();
    private string? _path;
    private string? _persistedManifestHash;
    private AssetId? _dbcId;
    private AssetId? _documentId;
    private AssetId? _labProgramId;
    private LabDocument? _labProgram;
    private StepEditTransaction _edit;

    public V1Workspace()
    {
        Project = ProjectDefinition.Create("未命名测试工程");
        _edit = new StepEditTransaction(TestDocument.Create("新测试程序"));
    }

    public ProjectDefinition Project { get; private set; }
    public TestDocument Document => _edit.Current;
    public StepEditTransaction Editor => _edit;
    public DbcDatabase? Database { get; private set; }
    public AssetId? DbcAssetId => _dbcId;
    public AssetId? DocumentAssetId => _documentId;
    public AssetId? LabProgramAssetId => _labProgramId;
    public LabDocument? LabProgram => _labProgram?.Copy();
    public string? ProjectPath => _path;
    public bool IsRunning => _runner.IsRunning;
    public MockRunReport? LastReport { get; private set; }
    public IReadOnlyList<AssetEntry> Assets => Project.Assets.ToArray();
    public IReadOnlyList<AssetDependency> StaleDependencies => ReferenceValidator.ScanStale(Project);
    public string AssetToken(AssetId id)
    {
        var a = Project.Assets.Single(x => x.Id == id);
        return $"{a.Id.Value:D}@{a.Revision}";
    }

    public void CreateProject(string name)
    {
        EnsureIdle();
        Project = ProjectDefinition.Create(name);
        _edit = new StepEditTransaction(TestDocument.Create("新测试程序"));
        _contents.Clear(); _persistedAssetHashes.Clear(); _persistedAssetPaths.Clear();
        _dbcId = null; _documentId = null; _labProgramId = null;
        _labProgram = null; Database = null; LastReport = null;
        _path = null; _persistedManifestHash = null;
    }

    public void RenameProject(string name)
    {
        EnsureIdle();
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("工程名称不能为空", nameof(name));
        Project = (Project with { Name = name }).WithRevision().Validate();
    }

    public AssetEntry ImportDbc(string text, string filename = "network.dbc")
    {
        EnsureIdle();
        if (_dbcId is not null) throw new InvalidOperationException("当前 V1 每工程仅支持一个主 DBC；请先另建工程");
        var source = DbcSource.Load(Encoding.UTF8.GetBytes(text));
        var parsed = DbcParserAdapter.Parse(source.Text, Path.GetFileNameWithoutExtension(filename));
        var bytes = Encoding.UTF8.GetBytes(text);
        var editor = new ResourceEditService(Project);
        var entry = editor.Add("dbc", parsed.Name, "assets/" + Guid.NewGuid().ToString("N") + ".dbc", bytes);
        Project = editor.Current;
        _contents.Add(entry.Id, bytes);
        _dbcId = entry.Id;
        Database = parsed with { Revision = entry.Revision };
        return entry;
    }

    /// <summary>
    /// Replace the project's DBC content as a new explicit revision. The caller
    /// must provide the exact old asset identity/hash; stale concurrent edits
    /// fail closed. Existing TEST references are never silently rebound.
    /// </summary>
    public AssetEntry ReplaceDbcSource(string utf8Text, AssetRef expected, string filename = "network.dbc")
    {
        EnsureIdle();
        if (_dbcId is null || Database is null)
            throw new InvalidOperationException("DBC_REQUIRED: 当前工程没有主 DBC。");
        var index = -1;
        for (var i = 0; i < Project.Assets.Length; i++)
            if (Project.Assets[i].Id == _dbcId.Value) { index = i; break; }
        if (index < 0) throw new InvalidOperationException("DBC_ASSET_MISSING");
        var old = Project.Assets[index];
        if (old.Id != expected.Id || old.Revision != expected.Revision.Value ||
            !string.Equals(old.Sha256, expected.Fingerprint.Sha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("DBC_REVISION_CONFLICT: 工程资源版本已经变化，请重新加载。");
        var data = Encoding.UTF8.GetBytes(utf8Text ??
            throw new ArgumentNullException(nameof(utf8Text)));
        var source = DbcSource.Load(data);
        var parsed = DbcParserAdapter.Parse(source.Text,
            Path.GetFileNameWithoutExtension(filename));
        var sha = Sha(data);
        if (string.Equals(sha, old.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("DBC_UNCHANGED: 内容未发生变化，不创建伪修订。");
        var revision = checked(old.Revision + 1);
        var next = old with
        {
            Revision = revision, Sha256 = sha,
            RelativePath = RevisionFile(old.Id, revision, old.Kind)
        };
        var project = (Project with
        {
            Assets = Project.Assets.SetItem(index, next)
        }).WithRevision().Validate();
        // Only mutate after every syntax, ID, version and resource validation passed.
        Project = project;
        Database = parsed with { Revision = revision };
        _contents[_dbcId.Value] = data;
        return next;
    }

    /// <summary>Explicit edit; never rewrites a TEST document's pinned asset reference.</summary>
    public AssetEntry EditDbcSignal(uint messageId, string signalName, double factor)
    {
        EnsureIdle();
        if (_dbcId is null || Database is null) throw new InvalidOperationException("请先导入 DBC");
        var session = new DbcEditSession(Database);
        var signal = Database.FindSignal(messageId, signalName);
        if (signal.Factor == factor) throw new ArgumentException("Factor 没有变化，不产生伪修订", nameof(factor));
        session.EditSignal(messageId, signalName, signal with { Factor = factor });
        var updated = session.Commit();
        var bytes = Encoding.UTF8.GetBytes(DbcParserAdapter.Export(updated));
        var index = -1;
        for (var i = 0; i < Project.Assets.Length; i++)
            if (Project.Assets[i].Id == _dbcId.Value) { index = i; break; }
        var old = Project.Assets[index];
        var revision = checked(old.Revision + 1);
        var next = old with { Revision = revision, Sha256 = Sha(bytes),
            RelativePath = RevisionFile(old.Id, revision, old.Kind) };
        // Stale dependencies remain pinned and visible. They must be explicitly rebound.
        Project = (Project with { Assets = Project.Assets.SetItem(index, next) }).WithRevision().Validate();
        Database = updated with { Revision = revision };
        _contents[_dbcId.Value] = bytes;
        return next;
    }

    /// <summary>
    /// R2's full LAB test-program schema is a versioned project asset, not a
    /// free-floating standalone JSON. Importing it never launches physical
    /// execution or mutates the older TEST document used by P11 Mock runs.
    /// </summary>
    public AssetEntry ImportLabProgram(string json, string filename = "test-program.lab.json")
    {
        EnsureIdle();
        if (_labProgramId is not null)
            throw new InvalidOperationException("LAB_PROGRAM_ALREADY_REGISTERED: use explicit revision replacement.");
        var document = LabDocumentCodec.Open(json);
        // Structural validation precedes changing any project state. Controller
        // bindings may remain unresolved until station/project configuration.
        var bytes = new UTF8Encoding(false).GetBytes(LabDocumentCodec.Save(document));
        var editor = new ResourceEditService(Project);
        var entry = editor.Add("test-program", document.Name,
            "assets/" + document.Id.ToString("N") + ".lab.json", bytes);
        Project = editor.Current;
        _contents.Add(entry.Id, bytes);
        _labProgramId = entry.Id;
        _labProgram = document;
        return entry;
    }

    public AssetEntry ReplaceLabProgram(string json, AssetRef expected)
    {
        EnsureIdle();
        if (_labProgramId is null || _labProgram is null)
            throw new InvalidOperationException("LAB_PROGRAM_REQUIRED");
        var index = -1;
        for (var i = 0; i < Project.Assets.Length; i++)
            if (Project.Assets[i].Id == _labProgramId.Value) { index = i; break; }
        if (index < 0) throw new InvalidOperationException("LAB_PROGRAM_ASSET_MISSING");
        var original = Project.Assets[index];
        if (original.Id != expected.Id ||
            original.Revision != expected.Revision.Value ||
            !string.Equals(original.Sha256, expected.Fingerprint.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("LAB_PROGRAM_REVISION_CONFLICT");
        var nextDocument = LabDocumentCodec.Open(json);
        if (nextDocument.Id != _labProgram.Id)
            throw new InvalidOperationException("LAB_PROGRAM_IDENTITY_CHANGED");
        var bytes = new UTF8Encoding(false).GetBytes(LabDocumentCodec.Save(nextDocument));
        var sha = Sha(bytes);
        if (string.Equals(sha, original.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("LAB_PROGRAM_UNCHANGED");
        var revision = checked(original.Revision + 1);
        var updated = original with
        {
            Revision = revision,
            Sha256 = sha,
            Name = nextDocument.Name,
            RelativePath = RevisionFile(original.Id, revision, original.Kind)
        };
        var project = (Project with
        {
            Assets = Project.Assets.SetItem(index, updated)
        }).WithRevision().Validate();
        Project = project;
        _contents[_labProgramId.Value] = bytes;
        _labProgram = nextDocument;
        return updated;
    }

    public void CreateTestDocument(string name)
    {
        EnsureIdle();
        if (_documentId is not null)
            throw new InvalidOperationException("请另建工程，避免覆盖已关联的测试资源");
        _edit = new StepEditTransaction(TestDocument.Create(name));
        LastReport = null;
    }

    /// <summary>
    /// A real, editable TEST authoring example, not the V0 gateway scenario.
    /// Every node is persisted with TestDocumentCodec and executed by RunCoordinator.
    /// </summary>
    public void AddEditableMockExample()
    {
        EnsureIdle();
        if (Document.Walk().Any()) throw new InvalidOperationException("测试程序非空");
        var prepare = StepNode.Create("SetVariable", "准备模式")
            .WithProperty("variable", "mode").WithProperty("value", "ready");
        var observe = StepNode.Create("MockObserve", "生成模拟观测")
            .WithProperty("variable", "pressure").WithProperty("value", "8")
            .WithProperty("quality", "Observed");
        var assert = StepNode.Create("Assert", "断言模拟压力")
            .WithProperty("variable", "pressure").WithProperty("equals", "8");
        var loop = StepNode.Create("Loop", "重复验证")
            .WithProperty("count", "2").WithSlot("body", observe, assert);
        var condition = StepNode.Create("If", "条件路径")
            .WithProperty("variable", "mode").WithProperty("equals", "ready")
            .WithSlot("then", loop).WithSlot("else");
        var cleanup = StepNode.Create("SetVariable", "模拟清理")
            .WithProperty("variable", "mode").WithProperty("value", "clean");
        _edit.Insert(TestPhase.Prepare, null, null, 0, prepare);
        _edit.Insert(TestPhase.Test, null, null, 0, condition);
        _edit.Insert(TestPhase.Cleanup, null, null, 0, cleanup);
    }

    public void PinStepToDbc(StepId id)
    {
        EnsureIdle();
        if (_dbcId is null) throw new InvalidOperationException("尚未导入 DBC");
        _edit.SetProperty(id, "asset", AssetToken(_dbcId.Value));
    }

    /// <summary>Explicitly acknowledge new asset revision in both document and project dependency graph.</summary>
    public void RebindDbcReference(StepId id)
    {
        EnsureIdle();
        if (_dbcId is null) throw new InvalidOperationException("尚未导入 DBC");
        var previous = Document.Walk().First(x => x.Id == id);
        if (!previous.Properties.ContainsKey("asset")) throw new InvalidOperationException("此步骤没有 DBC 引用");
        PinStepToDbc(id);
        if (_documentId is { } documentAsset)
            Project = (Project with
            {
                Dependencies = Project.Dependencies.Select(d => d.From == documentAsset && d.To == _dbcId.Value
                    ? d with { RequiredRevision = Project.Assets.Single(a => a.Id == _dbcId.Value).Revision }
                    : d).ToImmutableArray()
            }).WithRevision().Validate();
    }

    public IReadOnlyList<string> Diagnostics()
    {
        var messages = new List<string>();
        messages.AddRange(StaleDependencies.Select(d =>
            $"REFERENCE_STALE: {d.From.Value:D} -> {d.To.Value:D} 需要 revision {d.RequiredRevision}"));
        var allowed = AllowedAssetTokens();

        foreach (var node in Document.Walk())
        {
            if (!node.Properties.TryGetValue("asset", out var token) ||
                allowed.Contains(token))
                continue;

            var knownIdentity = Project.Assets.Any(asset =>
                token.StartsWith(
                    asset.Id.Value.ToString("D") + "@",
                    StringComparison.Ordinal));

            messages.Add(
                $"{(knownIdentity ? "REFERENCE_STALE" : "REFERENCE_MISSING")}: " +
                $"step {node.Id.Value:D} pinned {token}");
        }

        messages.AddRange(
            new PlanCompiler().Prepare(Document, allowed).Diagnostics);

        return messages;
    }

    public string StepAssetStatus(StepNode node)
    {
        if (!node.Properties.TryGetValue("asset", out var token)) return "未引用 DBC";
        if (_dbcId is null) return "REFERENCE_MISSING（DBC 不存在）";
        return token == AssetToken(_dbcId.Value)
            ? "引用有效：" + token
            : "REFERENCE_STALE：已固定旧版本 " + token;
    }

    private HashSet<string> AllowedAssetTokens() =>
        Project.Assets.Where(a => _contents.ContainsKey(a.Id))
            .Select(a => AssetToken(a.Id)).ToHashSet(StringComparer.Ordinal);

    public async Task<MockRunReport> RunMockAsync(CancellationToken ct = default, RunId? runId = null)
    {
        var problems = Diagnostics();
        if (problems.Count != 0) throw new InvalidOperationException("PREPARE_BLOCKED: " + string.Join("; ", problems));
        var plan = new PlanCompiler().Prepare(Document, AllowedAssetTokens());
        if (!plan.Diagnostics.IsEmpty) throw new InvalidOperationException("PREPARE_BLOCKED");
        // Unique station per run: no previous run's observation can be reused.
        var report = await _runner.StartAsync(plan, new AutoTest.Testing.Runtime.MockStation(), ct, runId);
        LastReport = report;
        return report;
    }

    public void Stop() => _runner.RequestStop();
    public void Pause() => _runner.PauseAtBoundary();
    public void Resume() => _runner.Resume();
    public void Step() => _runner.Step();

    public async Task SaveProjectAsync(string path, CancellationToken ct = default)
    {
        EnsureIdle();
        var full = Path.GetFullPath(path);
        if (!full.EndsWith(".autotest-project.json", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("工程文件必须以 .autotest-project.json 结尾", nameof(path));
        if (_path is not null && !string.Equals(full, _path, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("V1 不支持复制工程到另一根目录；请先重新打开原工程");
        var dir = Path.GetDirectoryName(full)!;
        var clone = _contents.ToDictionary(x => x.Key, x => x.Value.ToArray());
        var project = Project;
        AssetId? documentAssetId = _documentId;
        var documentBytes = Encoding.UTF8.GetBytes(TestDocumentCodec.Serialize(Document));
        if (_documentId is null)
        {
            var entry = AssetEntry.Create("test", Document.Name,
                "assets/" + Document.Id.Value.ToString("N") + ".r1.test.json", documentBytes);
            project = (project with { Assets = project.Assets.Add(entry) }).WithRevision().Validate();
            clone.Add(entry.Id, documentBytes);
            documentAssetId = entry.Id;
        }
        else
        {
            var index = -1;
            for (var i = 0; i < project.Assets.Length; i++)
                if (project.Assets[i].Id == _documentId.Value) { index = i; break; }
            var original = project.Assets[index];
            if (!original.VerifyHash(documentBytes))
                project = (project with { Assets = project.Assets.SetItem(index,
                    original with { Revision = checked(original.Revision + 1), Sha256 = Sha(documentBytes),
                        RelativePath = RevisionFile(original.Id, checked(original.Revision + 1), "test"),
                        Name = Document.Name }) })
                    .WithRevision().Validate();
            clone[_documentId.Value] = documentBytes;
        }
        // The document asset pins DBC revision at authoring time. Do not rewrite it on DBC edits.
        if (_dbcId is { } dbc && Document.Walk().Any(n => n.Properties.ContainsKey("asset")) &&
            !project.Dependencies.Any(d => d.From == documentAssetId!.Value && d.To == dbc))
        {
            var pin = Document.Walk().Select(n => n.Properties.GetValueOrDefault("asset"))
                .FirstOrDefault(x => x is not null && x.StartsWith(dbc.Value.ToString("D") + "@", StringComparison.Ordinal));
            if (pin is not null && int.TryParse(pin[(pin.LastIndexOf('@') + 1)..], out var revision))
                project = (project with { Dependencies = project.Dependencies.Add(
                    new AssetDependency(documentAssetId!.Value, dbc, revision, "test-signal-reference")) })
                    .WithRevision().Validate();
        }
        project.Validate();
        if ((_persistedManifestHash is not null && !File.Exists(full)) ||
            (File.Exists(full) && (_persistedManifestHash is null ||
            Sha(await File.ReadAllBytesAsync(full, ct)) != _persistedManifestHash)))
            throw new InvalidOperationException("PROJECT_CONFLICT: 目标工程缺失、已有外部内容或已改变");
        // Compare the last committed files, including revision paths no longer referenced by draft.
        foreach (var (id, previousPath) in _persistedAssetPaths)
        {
            ct.ThrowIfCancellationRequested();
            var previousFull = ProjectPathPolicy.Normalize(dir, previousPath);
            if (!File.Exists(previousFull) ||
                Sha(await File.ReadAllBytesAsync(previousFull, ct)) != _persistedAssetHashes[id])
                throw new InvalidOperationException("ASSET_CONFLICT: 已提交的资源在外部被修改 " + previousPath);
        }
        foreach (var asset in project.Assets)
        {
            ct.ThrowIfCancellationRequested();
            var file = ProjectPathPolicy.Normalize(dir, asset.RelativePath);
            if (File.Exists(file))
            {
                var currentHash = Sha(await File.ReadAllBytesAsync(file, ct));
                if (!_persistedAssetHashes.TryGetValue(asset.Id, out var old) ||
                    currentHash != old || currentHash != asset.Sha256)
                    throw new InvalidOperationException("ASSET_CONFLICT: " + asset.Name);
            }
            if (!clone.TryGetValue(asset.Id, out var bytes) || !asset.VerifyHash(bytes))
                throw new InvalidOperationException("ASSET_HASH_MISMATCH: " + asset.Name);
        }
        foreach (var asset in project.Assets)
        {
            var file = ProjectPathPolicy.Normalize(dir, asset.RelativePath);
            // Content for previous revisions is immutable. Commit the manifest last.
            if (!File.Exists(file)) await WriteAtomicAsync(file, clone[asset.Id], ct);
        }
        await ProjectRepository.SaveDefinitionAsync(project, full, ct);
        Project = project; _path = full; _documentId = documentAssetId;
        _contents.Clear(); foreach (var pair in clone) _contents.Add(pair.Key, pair.Value);
        _persistedAssetHashes.Clear(); _persistedAssetPaths.Clear();
        foreach (var asset in project.Assets)
        {
            _persistedAssetHashes.Add(asset.Id, asset.Sha256);
            _persistedAssetPaths.Add(asset.Id, asset.RelativePath);
        }
        _persistedManifestHash = Sha(await File.ReadAllBytesAsync(full, ct));
    }

    public async Task OpenProjectAsync(string path, CancellationToken ct = default)
    {
        EnsureIdle();
        var full = Path.GetFullPath(path);
        var project = await ProjectRepository.OpenDefinitionAsync(full, ct);
        var dir = Path.GetDirectoryName(full)!;
        var loaded = new Dictionary<AssetId, byte[]>();
        foreach (var asset in project.Assets)
        {
            var bytes = await File.ReadAllBytesAsync(ProjectPathPolicy.Normalize(dir, asset.RelativePath), ct);
            if (!asset.VerifyHash(bytes)) throw new InvalidOperationException("ASSET_HASH_MISMATCH: " + asset.Name);
            loaded.Add(asset.Id, bytes);
        }
        var documents = project.Assets.Where(a => a.Kind == "test").ToArray();
        var dbcs = project.Assets.Where(a => a.Kind == "dbc").ToArray();
        var labPrograms = project.Assets.Where(a => a.Kind == "test-program").ToArray();
        if (documents.Length != 1 || dbcs.Length > 1 || labPrograms.Length > 1)
            throw new NotSupportedException("V1 单工程须有一个测试文档，且最多一个 DBC");
        var document = TestDocumentCodec.Deserialize(Encoding.UTF8.GetString(loaded[documents[0].Id]));
        LabDocument? labProgram = labPrograms.Length == 1
            ? LabDocumentCodec.Open(Encoding.UTF8.GetString(loaded[labPrograms[0].Id])) : null;
        DbcDatabase? dbc = null;
        if (dbcs.Length == 1)
        {
            var src = DbcSource.Load(loaded[dbcs[0].Id]);
            dbc = DbcParserAdapter.Parse(src.Text, dbcs[0].Name) with { Revision = dbcs[0].Revision };
        }
        // Only commit fully validated project/asset/document; broken files leave old workspace intact.
        Project = project; _edit = new StepEditTransaction(document);
        _dbcId = dbcs.SingleOrDefault()?.Id; _documentId = documents[0].Id; Database = dbc;
        _labProgramId = labPrograms.SingleOrDefault()?.Id; _labProgram = labProgram;
        _contents.Clear(); foreach (var (id, bytes) in loaded) _contents.Add(id, bytes);
        _path = full; _persistedManifestHash = Sha(await File.ReadAllBytesAsync(full, ct));
        _persistedAssetHashes.Clear(); _persistedAssetPaths.Clear();
        foreach (var asset in project.Assets)
        {
            _persistedAssetHashes[asset.Id] = asset.Sha256;
            _persistedAssetPaths[asset.Id] = asset.RelativePath;
        }
        LastReport = null;
    }

    private void EnsureIdle()
    {
        if (IsRunning) throw new InvalidOperationException("执行期间禁止修改工程和步骤");
    }
    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string RevisionFile(AssetId id, int revision, string kind) =>
        $"assets/{id.Value:N}.r{revision}" + (kind switch
        {
            "test" => ".test.json",
            "test-program" => ".lab.json",
            "dbc" => ".dbc",
            _ => throw new NotSupportedException("Unsupported asset revision kind: " + kind)
        });
    private static async Task WriteAtomicAsync(string file, byte[] bytes, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temp, bytes, ct);
            ct.ThrowIfCancellationRequested();
            File.Move(temp, file);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
