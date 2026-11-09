using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using AutoTest.Projects.Dbc;
using AutoTest.Projects.Dbc.Lab;
using AutoTest.Projects.Project;
using AutoTest.Projects.Resources;
using AutoTest.Core;
using System.Text;

namespace AutoTest.Desktop.Tools.Dbc;

public sealed class DbcToolCommand(Action action) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => action();
    public void Requery() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class DbcToolViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    private DbcWorkbenchDocument? _document;
    private DbcRecordedReplayResult? _lastArchiveReplay;
    private string? _expectedHash;
    private DbcMessage? _message;
    private DbcSignal? _signal;
    private R0ProjectAssetWorkspace? _boundWorkspace;
    private AssetId _boundAssetId;
    private int _boundRevision;
    private string _boundSha = "";
    private string _traceFilter = "";
    private readonly R0ProjectDiagnosticJournal _journal = new();
    private string _status = "未加载 DBC / Unknown";
    private string _error = "";
    private string _decodeSummary = "未进行 Replay 解码";
    public ObservableCollection<DbcMessage> Messages { get; } = [];
    public ObservableCollection<DbcSignal> Signals { get; } = [];
    public ObservableCollection<DbcDecodedValue> Decoded { get; } = [];
    public ObservableCollection<DbcByteLayoutRow> BitRows { get; } = [];
    public ObservableCollection<DbcSignalLayoutRow> LayoutSignals { get; } = [];
    public ObservableCollection<R0ProjectDiagnosticEvent> TraceEvents { get; } = [];
    public ObservableCollection<DbcSourceIssue> SourceIssues { get; } = [];
    public ObservableCollection<DbcRecordedReplayRow> ArchiveRows { get; } = [];
    private string _sourceDiagnosticSummary = "尚未执行源码诊断";
    private string _archiveSummary = "未载入离线 CAN RX 归档";
    public string SourceDiagnosticSummary { get => _sourceDiagnosticSummary; private set { _sourceDiagnosticSummary = value; Changed(); } }
    public string ArchiveSummary { get => _archiveSummary; private set { _archiveSummary = value; Changed(); } }
    public string FilePath { get; set; } = "";
    public string ProjectFilePath { get; set; } = "";
    public string BoundAssetId { get; set; } = "";
    public string TraceFilePath { get; set; } = "";
    public string ArchiveFilePath { get; set; } = "";
    public string ArchiveEvidencePath { get; set; } = "";
    public bool IncludeArchivedRawHex { get; set; }
    public bool IncludeRawTraceHex { get; set; } = true;
    public string TraceFilter { get => _traceFilter; set { _traceFilter = value ?? ""; Changed(); Changed(nameof(VisibleTraceEvents)); } }
    public IEnumerable<R0ProjectDiagnosticEvent> VisibleTraceEvents => TraceEvents.Where(e =>
        string.IsNullOrWhiteSpace(TraceFilter) ||
        (e.Stage + " " + e.ErrorCode + " " + e.Detail + " " + e.RawHex).Contains(TraceFilter, StringComparison.OrdinalIgnoreCase));
    public string NewNodeName { get; set; } = "";
    public string NewMessageId { get; set; } = "0x100";
    public string NewMessageName { get; set; } = "Frame";
    public string NewMessageDlc { get; set; } = "8";
    public string Transmitter { get; set; } = "Vector__XXX";
    public string SignalName { get; set; } = "Signal";
    public string StartBit { get; set; } = "0";
    public string BitLength { get; set; } = "8";
    public string Factor { get; set; } = "1";
    public string Offset { get; set; } = "0";
    public string Minimum { get; set; } = "0";
    public string Maximum { get; set; } = "255";
    public string Unit { get; set; } = "";
    public string Receiver { get; set; } = "Vector__XXX";
    public bool Intel { get; set; } = true;
    public bool Signed { get; set; }
    public string Mux { get; set; } = "";
    public string LabelRaw { get; set; } = "0";
    public string ValueLabel { get; set; } = "";
    public string ReplayFrameId { get; set; } = "0x100";
    public string ReplayHex { get; set; } = "00 00 00 00 00 00 00 00";
    public string Status { get => _status; private set { _status = value; Changed(); } }
    public string Error { get => _error; private set { _error = value; Changed(); } }
    public string DecodeSummary { get => _decodeSummary; private set { _decodeSummary = value; Changed(); } }
    public string SupportSummary => _document is null ? "未导入" :
        _document.CanEdit ? "受支持语法：可编辑 BO_/SG_/BU_/VAL_（保存前回读校验）" :
        "只读保护：" + string.Join(", ", _document.UnsupportedDirectives);
    public int Revision => _boundRevision;
    public string ProjectBindingSummary => _boundWorkspace is null ? "工程未绑定 / SOFTWARE_ONLY / Revision UNKNOWN" :
        $"Project {_boundWorkspace.Definition.Id.Value:D} / DBC Asset {_boundAssetId.Value:D} / Revision {_boundRevision} / SHA256 {_boundSha} / REPLAY";
    public string TraceQuality => $"Trace {TraceEvents.Count} / Dropped {_journal.DroppedCount} / SOFTWARE_ONLY";
    public DbcMessage? SelectedMessage
    {
        get => _message;
        set { _message = value; Changed(); SelectMessage(); }
    }
    public DbcSignal? SelectedSignal
    {
        get => _signal;
        set { _signal = value; Changed(); SelectSignal(); }
    }
    public ICommand OpenCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand AddNodeCommand { get; }
    public ICommand UpdateMessageCommand { get; }
    public ICommand AddMessageCommand { get; }
    public ICommand DeleteMessageCommand { get; }
    public ICommand AddSignalCommand { get; }
    public ICommand UpdateSignalCommand { get; }
    public ICommand DeleteSignalCommand { get; }
    public ICommand LabelCommand { get; }
    public ICommand BindProjectCommand { get; }
    public ICommand ExportTraceCommand { get; }
    public ICommand DiagnoseSourceCommand { get; }
    public ICommand ReplayArchiveCommand { get; }
    public ICommand ExportArchiveEvidenceCommand { get; }
    public ICommand ReplayCommand { get; }
    public ICommand ResetCommand { get; }

    public DbcToolViewModel()
    {
        OpenCommand = new DbcToolCommand(() => _ = LoadSafeAsync());
        SaveCommand = new DbcToolCommand(() => _ = SaveSafeAsync());
        AddNodeCommand = new DbcToolCommand(() => Safe(() => { Document.AddNode(NewNodeName); Refresh(); }));
        UpdateMessageCommand = new DbcToolCommand(() => Safe(() =>
        {
            var id = SelectedMessageId();
            Document.UpdateMessage(id, new DbcMessage(ParseId(NewMessageId), NewMessageName,
                int.Parse(NewMessageDlc), Transmitter, _message!.Signals));
            Refresh();
        }));
        AddMessageCommand = new DbcToolCommand(() => Safe(AddMessage));
        DeleteMessageCommand = new DbcToolCommand(() => Safe(() => { Document.DeleteMessage(SelectedMessageId()); Refresh(); }));
        AddSignalCommand = new DbcToolCommand(() => Safe(() => { Document.AddSignal(SelectedMessageId(), DraftSignal()); Refresh(); }));
        UpdateSignalCommand = new DbcToolCommand(() => Safe(() => {
            var old = _signal ?? throw new InvalidOperationException("请选择信号。");
            Document.UpdateSignal(SelectedMessageId(), old.Name, DraftSignal()); Refresh(); }));
        DeleteSignalCommand = new DbcToolCommand(() => Safe(() => {
            Document.DeleteSignal(SelectedMessageId(), _signal?.Name ?? throw new InvalidOperationException("请选择信号。")); Refresh(); }));
        LabelCommand = new DbcToolCommand(() => Safe(() => {
            Document.SetValueDescription(SelectedMessageId(), _signal?.Name ?? throw new InvalidOperationException("请选择信号。"),
                long.Parse(LabelRaw, CultureInfo.InvariantCulture), ValueLabel);
            Refresh(); }));
        BindProjectCommand = new DbcToolCommand(() => _ = BindProjectSafeAsync());
        ExportTraceCommand = new DbcToolCommand(() => _ = ExportTraceSafeAsync());
        DiagnoseSourceCommand = new DbcToolCommand(() => _ = DiagnoseSourceSafeAsync());
        ReplayArchiveCommand = new DbcToolCommand(() => _ = ReplayArchiveSafeAsync());
        ExportArchiveEvidenceCommand = new DbcToolCommand(() => _ = ExportArchiveEvidenceSafeAsync());
        ReplayCommand = new DbcToolCommand(() => _ = ReplaySafeAsync());
        ResetCommand = new DbcToolCommand(() => Safe(() => { Document.Reset(); Refresh(); }));
    }
    private DbcWorkbenchDocument Document => _document ?? throw new InvalidOperationException("先打开 DBC 文件。");
    private uint SelectedMessageId() => _message?.Id ?? throw new InvalidOperationException("请选择报文。");
    private static uint ParseId(string text) => text.Trim().StartsWith("0x", StringComparison.OrdinalIgnoreCase) ?
        uint.Parse(text.Trim()[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture) :
        uint.Parse(text.Trim(), CultureInfo.InvariantCulture);
    private static double Number(string text) => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
    private DbcSignal DraftSignal() => new(SignalName, int.Parse(StartBit), int.Parse(BitLength), Intel, Signed,
        Number(Factor), Number(Offset), Number(Minimum), Number(Maximum), Unit, Receiver,
        string.IsNullOrWhiteSpace(Mux) ? null : Mux);
    private void AddMessage()
    {
        var frame = new DbcMessage(ParseId(NewMessageId), NewMessageName, int.Parse(NewMessageDlc), Transmitter,
            System.Collections.Immutable.ImmutableArray<DbcSignal>.Empty);
        Document.AddMessage(frame); Refresh();
    }
    private void PreviewLayout()
    {
        BitRows.Clear();
        LayoutSignals.Clear();
        if (_message is null) return;
        var layout = DbcBitLayoutProjection.Preview(_message);
        foreach (var row in layout.Bytes) BitRows.Add(row);
        foreach (var signal in layout.Signals) LayoutSignals.Add(signal);
    }

    private void Log(string stage, string outcome, string errorCode = "", string rawHex = "",
        string expected = "", string actual = "", string detail = "")
    {
        var item = _journal.Record("PROJECT/DBC", stage, outcome, errorCode,
            project: ProjectFilePath, assetId: _boundWorkspace is null ? "" : _boundAssetId.Value.ToString("D"),
            revision: _boundRevision, sha256: _boundSha, rawHex: rawHex,
            expected: expected, actual: actual, detail: detail, origin: "REPLAY");
        TraceEvents.Add(item);
        if (TraceEvents.Count > 4096) TraceEvents.RemoveAt(0);
        Changed(nameof(VisibleTraceEvents));
        Changed(nameof(TraceQuality));
    }


    private async Task DiagnoseSourceSafeAsync()
    {
        SourceIssues.Clear();
        try
        {
            if (string.IsNullOrWhiteSpace(FilePath))
                throw new ProjectDataException("DBC_SOURCE_PATH", "请先选择 DBC 文件路径。");
            var info = new FileInfo(FilePath);
            if (!info.Exists || info.Length is <= 0 or > 4 * 1024 * 1024)
                throw new ProjectDataException("DBC_SOURCE_SIZE", "DBC 不存在、为空或超过大小限制。");
            var bytes = await File.ReadAllBytesAsync(FilePath);
            var source = DbcSource.Load(bytes);
            foreach (var issue in DbcSourceDiagnosticScanner.Inspect(source.Text,
                Path.GetFileNameWithoutExtension(FilePath))) SourceIssues.Add(issue);
            var errors = SourceIssues.Count(x => x.Severity == "ERROR");
            var warnings = SourceIssues.Count(x => x.Severity == "WARNING");
            SourceDiagnosticSummary = $"文件源码诊断：错误 {errors} / 只读或扩展警告 {warnings}；行号为 1 起始，0 表示不能可靠定位";
            Log("SOURCE_DIAGNOSIS", errors == 0 ? "COMPLETED" : "REJECTED",
                errors == 0 ? "" : "DBC_SOURCE_DIAGNOSTIC",
                expected: "parseable DBC source", actual: $"{errors} errors / {warnings} warnings",
                detail: "仅诊断源文件；未知指令保留，不自动写回。");
        }
        catch (Exception e)
        {
            SourceDiagnosticSummary = "无法读取源文件以定位错误：" + e.Message;
            Log("SOURCE_DIAGNOSIS", "REJECTED",
                e is ProjectDataException p ? p.Code : e.GetType().Name, detail: e.Message);
        }
    }

    private async Task ReplayArchiveSafeAsync()
    {
        _lastArchiveReplay = null;
        ArchiveRows.Clear();
        ArchiveSummary = "归档校验中；未读取物理 ECU 数据";
        try
        {
            Error = "";
            if (_boundWorkspace is null)
                throw new ProjectDataException("DBC_ARCHIVE_BINDING",
                    "归档 Replay 必须先绑定工程 DBC AssetId/Revision。");
            await _boundWorkspace.VerifyPinnedAssetAsync(_boundAssetId, _boundRevision, _boundSha);
            var binding = new DbcRevisionBinding(_boundAssetId.Value, _boundRevision, _boundSha);
            // Explicit bounded review: excessive archives are rejected, never silently truncated.
            var result = await DbcRecordedCanArchiveReplay.ReadAsync(
                ArchiveFilePath, Document, binding, maxFrames: 10000);
            // Recheck after the potentially long read: changes during Replay must not become trusted UI evidence.
            await _boundWorkspace.VerifyPinnedAssetAsync(_boundAssetId, _boundRevision, _boundSha);
            _lastArchiveReplay = result;
            foreach (var frame in result.Frames)
            {
                ArchiveRows.Add(frame);
                Log("ARCHIVE_FRAME", frame.Status, frame.ErrorCode, frame.PayloadHex,
                    expected: "pinned DBC + valid source CAN RX",
                    actual: frame.DecodedSummary,
                    detail: $"frame={frame.FrameIndex} utc={frame.ReconstructedUtc:O} offset_us={frame.OffsetMicroseconds} " +
                        $"role={frame.BusRole} can=0x{frame.FrameId:X} trace={frame.TraceEvidence} lost={frame.LostFrames}");
            }
            ArchiveSummary = $"Replay archive SHA256 {result.ArchiveSha256} / DBC Revision {result.DbcRevision} / " +
                $"总帧 {result.TotalFrames} 已解码 {result.DecodedFrames} 未映射 {result.UnmappedFrames} " +
                $"拒绝 {result.RejectedFrames} 按键通道跳过 {result.SkippedKeypadFrames} / " +
                $"ReportedLostMax {result.MaximumReportedLostFrames} / {result.ClockQuality}";
            Status = result.RejectedFrames > 0 || result.UnmappedFrames > 0 ?
                "离线 Replay 完成但存在未解码帧；请查看逐帧错误" : "离线 Replay 完成（非实时 CAN 采集）";
            Log("ARCHIVE_REPLAY", result.RejectedFrames == 0 && result.UnmappedFrames == 0 ?
                "COMPLETED" : "COMPLETED_WITH_ERRORS", rawHex: "",
                expected: "recorded CAN RX and pinned DBC",
                actual: $"{result.DecodedFrames}/{result.TotalFrames} decoded",
                detail: ArchiveSummary);
        }
        catch (Exception e)
        {
            ArchiveRows.Clear();
            ArchiveSummary = "归档 Replay 被拒绝；无可信新解码值";
            Error = e is ProjectDataException p ? $"{p.Code}: {p.Message}" : e.Message;
            Status = "离线 CAN 归档未通过验证";
            Log("ARCHIVE_REPLAY", "REJECTED",
                e is ProjectDataException x ? x.Code : e.GetType().Name,
                expected: "verified archive + pinned DBC",
                actual: "rejected", detail: e.Message);
        }
    }

    private async Task ExportArchiveEvidenceSafeAsync()
    {
        try
        {
            Error = "";
            if (_lastArchiveReplay is null)
                throw new ProjectDataException("DBC_ARCHIVE_EXPORT_EMPTY",
                    "请先成功校验并加载离线 CAN 归档。");
            var receipt = await DbcRecordedReplayEvidenceWriter.ExportAsync(
                ArchiveEvidencePath, _lastArchiveReplay, IncludeArchivedRawHex);
            Status = $"离线 Replay 完整证据已导出：{receipt.FrameCount} 帧，SHA256 {receipt.Sha256[..12]}…";
            Log("ARCHIVE_EVIDENCE_EXPORT", "COMPLETED",
                expected: "all archive rows", actual: $"{receipt.FrameCount} frames",
                detail: $"file={Path.GetFileName(receipt.Path)} sha256={receipt.Sha256}");
        }
        catch (Exception e)
        {
            Error = e is ProjectDataException p ? $"{p.Code}: {p.Message}" : e.Message;
            Status = "归档证据导出失败（不会覆盖已有文件）";
            Log("ARCHIVE_EVIDENCE_EXPORT", "REJECTED",
                e is ProjectDataException x ? x.Code : e.GetType().Name, detail: e.Message);
        }
    }

    private async Task BindProjectSafeAsync()
    {
        try
        {
            Error = "";
            var workspace = await R0ProjectAssetWorkspace.OpenAsync(ProjectFilePath);
            if (!AssetId.TryParse(BoundAssetId, out var id))
                throw new ProjectDataException("ASSET_ID", "请输入工程资源清单中的 DBC AssetId。");
            var check = workspace.Find(id);
            if (!check.Asset.Kind.Equals("DBC", StringComparison.OrdinalIgnoreCase) || !check.IsValid)
                throw new ProjectDataException("ASSET_STALE", "DBC 资产类型或内容哈希校验失败。");
            var loaded = await DbcDocumentStore.OpenAsync(check.FullPath);
            if (!string.Equals(loaded.Receipt.Sha256, check.Asset.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new ProjectDataException("ASSET_STALE", "DBC 文件哈希与工程 manifest 不一致。");
            _boundWorkspace = workspace;
            _boundAssetId = id;
            _boundRevision = check.Asset.Revision;
            _boundSha = check.Asset.Sha256;
            _document = loaded.Document;
            _expectedHash = loaded.Receipt.Sha256;
            FilePath = check.FullPath;
            Changed(nameof(FilePath));
            Changed(nameof(ProjectBindingSummary));
            Refresh();
            await DiagnoseSourceSafeAsync();
            Log("BIND", "ACCEPTED", expected: check.Asset.Sha256, actual: loaded.Receipt.Sha256);
            Status = "已绑定工程 DBC；后续 Replay 前会再次校验磁盘版本。";
        }
        catch (Exception e)
        {
            Error = e is ProjectDataException p ? $"{p.Code}: {p.Message}" : e.Message;
            Status = "绑定失败，原工程版本不视为已核验";
            Log("BIND", "REJECTED", e is ProjectDataException x ? x.Code : e.GetType().Name, detail: e.Message);
        }
    }

    private async Task ReplaySafeAsync()
    {
        Decoded.Clear();
        BitRows.Clear();
        LayoutSignals.Clear();
        try
        {
            Error = "";
            var frameId = ParseId(ReplayFrameId);
            var raw = DbcReplayDecoder.ParseHex(ReplayHex);
            Log("REPLAY_REQUEST", "REQUESTED", rawHex: Convert.ToHexString(raw), expected: "DBC-valid DLC and pinned revision");
            if (_boundWorkspace is not null)
                await _boundWorkspace.VerifyPinnedAssetAsync(_boundAssetId, _boundRevision, _boundSha);
            var result = DbcReplayDecoder.Decode(Document, frameId, raw, DateTimeOffset.UtcNow,
                revision: _boundWorkspace is null ? null : new DbcRevisionBinding(_boundAssetId.Value, _boundRevision, _boundSha));
            foreach (var signal in result.Signals) Decoded.Add(signal);
            var layout = DbcBitLayoutProjection.FromReplay(Document.Model.FindMessage(frameId), raw);
            foreach (var row in layout.Bytes) BitRows.Add(row);
            foreach (var signal in layout.Signals) LayoutSignals.Add(signal);
            DecodeSummary = $"{result.Source} / {result.RevisionQuality} / Lost {result.LostCount} / {result.TimeQuality}";
            Status = "Replay 解码完成（原始帧来自用户输入，不是 ECU 实时采集）";
            Log("REPLAY", "ACCEPTED", rawHex: Convert.ToHexString(raw), expected: "decoded signal values",
                actual: string.Join("; ", result.Signals.Select(x => $"{x.SignalName}={x.PhysicalValue} {x.Unit}")),
                detail: DecodeSummary);
        }
        catch (Exception e)
        {
            Decoded.Clear();
            BitRows.Clear();
            LayoutSignals.Clear();
            Error = e is ProjectDataException p ? $"{p.Code}: {p.Message}" : e.Message;
            Status = "Replay 已拒绝；已清除上一帧解码结果";
            DecodeSummary = "无可信解码值";
            Log("REPLAY", "REJECTED", e is ProjectDataException x ? x.Code : e.GetType().Name,
                rawHex: ReplayHex, expected: "valid bound DBC and raw frame", actual: "rejected", detail: e.Message);
        }
    }

    private async Task ExportTraceSafeAsync()
    {
        try
        {
            Error = "";
            await _journal.ExportJsonLinesAsync(TraceFilePath, true, IncludeRawTraceHex);
            Status = "已导出 PROJECT/DBC 原始证据（本地 JSONL）";
        }
        catch (Exception e)
        {
            Error = e is ProjectDataException p ? $"{p.Code}: {p.Message}" : e.Message;
            Status = "Trace 导出失败";
            Log("TRACE_EXPORT", "REJECTED", e is ProjectDataException x ? x.Code : e.GetType().Name, detail: e.Message);
        }
    }

    private async Task LoadSafeAsync()
    {
        try
        {
            Error = "";
            var loaded = await DbcDocumentStore.OpenAsync(FilePath);
            _boundWorkspace = null; _boundRevision = 0; _boundSha = ""; _boundAssetId = default;
            Changed(nameof(ProjectBindingSummary));
            _document = loaded.Document; _expectedHash = loaded.Receipt.Sha256;
            Refresh();
            await DiagnoseSourceSafeAsync();
            Status = "DBC 文件已加载（未连接硬件）";
            Log("OPEN", "ACCEPTED", expected: "valid DBC file", actual: loaded.Receipt.Sha256);
        }
        catch (Exception e)
        {
            Error = e is ProjectDataException p ? $"{p.Code}: {p.Message}" : e.Message;
            Status = "DBC 导入失败";
            Log("OPEN", "REJECTED", e is ProjectDataException x ? x.Code : e.GetType().Name, detail: e.Message);
            await DiagnoseSourceSafeAsync();
        }
    }
    private async Task SaveSafeAsync()
    {
        try
        {
            Error = "";
            DbcFileReceipt receipt;
            if (_boundWorkspace is null)
            {
                receipt = await DbcDocumentStore.SaveAsync(Document, FilePath, _expectedHash);
                _expectedHash = receipt.Sha256;
            }
            else
            {
                await _boundWorkspace.VerifyPinnedAssetAsync(_boundAssetId, _boundRevision, _boundSha);
                var published = await _boundWorkspace.PublishRevisionAsync(_boundAssetId,
                    new UTF8Encoding(false).GetBytes(Document.ExportText()));
                _boundRevision = published.Asset.Revision;
                _boundSha = published.Asset.Sha256;
                FilePath = published.FullPath;
                _expectedHash = published.Asset.Sha256;
                receipt = new DbcFileReceipt(FilePath, _expectedHash, _boundRevision);
                Changed(nameof(FilePath)); Changed(nameof(ProjectBindingSummary));
            }
            Log("SAVE", "ACCEPTED", expected: "valid source + new revision", actual: receipt.Sha256);

            _document = DbcWorkbenchDocument.Import(Path.GetFileNameWithoutExtension(FilePath), Document.ExportText());
            Refresh();
            await DiagnoseSourceSafeAsync();
            Status = $"DBC 已保存，SHA256 {receipt.Sha256[..12]}…";
        }
        catch (Exception e)
        {
            Error = e is ProjectDataException p ? $"{p.Code}: {p.Message}" : e.Message;
            Status = "DBC 保存失败（编辑仍在内存）";
            Log("SAVE", "REJECTED", e is ProjectDataException x ? x.Code : e.GetType().Name,
                expected: "atomic DBC write / pinned revision", actual: "not saved", detail: e.Message);
        }
    }
    private void Safe(Action run)
    {
        try { Error = ""; run(); }
        catch (Exception e) { Error = e is ProjectDataException p ? $"{p.Code}: {p.Message}" : e.Message; Status = "DBC 操作失败"; Log("EDIT", "REJECTED", e is ProjectDataException x ? x.Code : e.GetType().Name, detail: e.Message); }
    }
    private void Refresh()
    {
        var id = _message?.Id; var signal = _signal?.Name;
        Messages.Clear();
        foreach (var message in Document.Model.Messages.OrderBy(m => m.Id)) Messages.Add(message);
        SelectedMessage = Messages.FirstOrDefault(m => m.Id == id) ?? Messages.FirstOrDefault();
        SelectedSignal = Signals.FirstOrDefault(s => s.Name == signal) ?? Signals.FirstOrDefault();
        Changed(nameof(SupportSummary)); Changed(nameof(Revision));
        Status = Document.CanEdit ? "已加载，待确认保存" : "只读：包含当前不支持写回的 DBC 语法";
    }
    private void SelectMessage()
    {
        Signals.Clear();
        foreach (var signal in _message?.Signals ?? []) Signals.Add(signal);
        PreviewLayout();
        SelectedSignal = Signals.FirstOrDefault();
        if (_message is not null)
        {
            ReplayFrameId = $"0x{_message.Id:X}"; NewMessageId = ReplayFrameId;
            NewMessageName = _message.Name; NewMessageDlc = _message.Dlc.ToString();
            Transmitter = _message.Transmitter;
            Changed(nameof(ReplayFrameId)); Changed(nameof(NewMessageId));
            Changed(nameof(NewMessageName)); Changed(nameof(NewMessageDlc)); Changed(nameof(Transmitter));
        }
    }
    private void SelectSignal()
    {
        if (_signal is null) return;
        SignalName = _signal.Name; StartBit = _signal.StartBit.ToString(); BitLength = _signal.BitLength.ToString();
        Factor = _signal.Factor.ToString("R", CultureInfo.InvariantCulture);
        Offset = _signal.Offset.ToString("R", CultureInfo.InvariantCulture);
        Minimum = _signal.Minimum.ToString("R", CultureInfo.InvariantCulture);
        Maximum = _signal.Maximum.ToString("R", CultureInfo.InvariantCulture);
        Intel = _signal.Intel; Signed = _signal.Signed;
        Unit = _signal.Unit; Receiver = _signal.Receiver; Mux = _signal.Multiplexer ?? "";
        foreach (var property in new[] { nameof(SignalName), nameof(StartBit), nameof(BitLength),
            nameof(Factor), nameof(Offset), nameof(Minimum), nameof(Maximum), nameof(Intel),
            nameof(Signed), nameof(Unit), nameof(Receiver), nameof(Mux) }) Changed(property);
    }
}
