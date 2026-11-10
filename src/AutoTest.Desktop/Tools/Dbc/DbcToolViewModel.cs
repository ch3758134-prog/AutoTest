using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Security.Cryptography;
using AutoTest.Application.V1;
using System.Windows.Input;
using AutoTest.Projects.Dbc;
using AutoTest.Projects.Dbc.Lab;
using AutoTest.Projects.Project;

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
    private readonly V1Workspace? _project;
    public event EventHandler? ProjectDbcImported;
    private DbcWorkbenchDocument? _document;
    private string? _expectedHash;
    private DbcMessage? _message;
    private DbcSignal? _signal;
    private string _status = "未加载 DBC / Unknown";
    private string _error = "";
    private string _decodeSummary = "未进行 Replay 解码";
    public ObservableCollection<DbcMessage> Messages { get; } = [];
    public ObservableCollection<DbcSignal> Signals { get; } = [];
    public ObservableCollection<DbcDecodedValue> Decoded { get; } = [];
    public string FilePath { get; set; } = "";
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
    public void RefreshProjectStatus()
    {
        Changed(nameof(Revision));
        Changed(nameof(ProjectStatus));
    }

    public int Revision => _project?.DbcAssetId is { } id
        ? _project.Assets.Single(x => x.Id == id).Revision : 0;
    public string ProjectStatus => _project?.DbcAssetId is { } id
        ? "当前工程主 DBC：已登记 Revision " + _project.Assets.Single(x => x.Id == id).Revision +
          "；编辑本地文件不会偷偷覆盖工程资源"
        : _project is null ? "独立 DBC 文件编辑模式" :
          "当前工程尚未登记 DBC；打开文件后点击【纳入当前工程】";
    public ICommand ImportToProjectCommand { get; }
    public ICommand PublishProjectRevisionCommand { get; }
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
    public ICommand ReplayCommand { get; }
    public ICommand ResetCommand { get; }

    public DbcToolViewModel() : this(null) { }

    public DbcToolViewModel(V1Workspace? project)
    {
        _project = project;
        ImportToProjectCommand = new DbcToolCommand(() => _ = ImportToProjectAsync());
        PublishProjectRevisionCommand = new DbcToolCommand(() => _ = PublishProjectRevisionAsync());
        OpenCommand = new DbcToolCommand(() => _ = OpenFileAsync());
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
        ReplayCommand = new DbcToolCommand(() => Safe(Replay));
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
    private void Replay()
    {
        Decoded.Clear();
        var result = DbcReplayDecoder.Decode(Document, ParseId(ReplayFrameId), DbcReplayDecoder.ParseHex(ReplayHex), DateTimeOffset.UtcNow);
        foreach (var signal in result.Signals) Decoded.Add(signal);
        DecodeSummary = $"{result.Source} / DBC {result.RevisionQuality} / Lost {result.LostCount} / {result.TimeQuality}";
        Status = "Replay 解码完成（没有采集任何真实 ECU 帧）";
    }
    public async Task ImportToProjectAsync()
    {
        try
        {
            Error = "";
            if (_project is null)
                throw new InvalidOperationException("当前界面未绑定正式工程。");
            if (_document is null || string.IsNullOrWhiteSpace(FilePath))
                throw new InvalidOperationException("先打开一份 DBC 文件，再导入工程。");
            if (_project.DbcAssetId is not null)
                throw new InvalidOperationException("当前工程已经有主 DBC；为避免覆盖已有版本，请在工程管理中创建新工程或执行明确的 Revision 变更。");
            // Import the original disk text; do not silently normalize or drop
            // unsupported vendor directives from a read-only DBC document.
            var original = await File.ReadAllTextAsync(FilePath, new UTF8Encoding(false, true));
            _project.ImportDbc(original, Path.GetFileName(FilePath));
            ProjectDbcImported?.Invoke(this, EventArgs.Empty);
            Changed(nameof(Revision));
            Changed(nameof(ProjectStatus));
            Status = "DBC 已纳入当前工程资源库；Wave 可刷新信号。请保存工程以持久化 Revision。";
        }
        catch (Exception e)
        {
            Error = e.Message;
            Status = "导入工程资源失败，原始 DBC 文件未被修改";
        }
    }

    public async Task PublishProjectRevisionAsync()
    {
        try
        {
            Error = "";
            if (_project?.DbcAssetId is not { } id || _document is null)
                throw new InvalidOperationException("先打开 DBC 文件并在工程中登记主 DBC。");
            if (!_document.CanEdit)
                throw new InvalidOperationException("DBC 含不支持安全写回的语法，不能发布为新的工程修订。");
            if (string.IsNullOrWhiteSpace(FilePath) || string.IsNullOrWhiteSpace(_expectedHash))
                throw new InvalidOperationException("请先保存当前本地 DBC 文件，再发布工程修订。");
            var expected = _project.Assets.Single(x => x.Id == id).ToReference();
            var bytes = await File.ReadAllBytesAsync(FilePath);
            var actualHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (!string.Equals(actualHash, _expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("DBC_DISK_CONFLICT: 磁盘文件已发生变化，请重新打开。");
            var original = new UTF8Encoding(false, true).GetString(bytes);
            var next = _project.ReplaceDbcSource(original, expected, Path.GetFileName(FilePath));
            ProjectDbcImported?.Invoke(this, EventArgs.Empty);
            RefreshProjectStatus();
            Status = "DBC 工程修订已更新至 r" + next.Revision +
                "；旧 TEST/参考引用不会自动重绑，请检查过期资源并保存工程。";
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            Status = "DBC 新 Revision 发布失败；工程旧版本保持不变";
        }
    }

    public async Task OpenFileAsync()
    {
        try
        {
            Error = "";
            var loaded = await DbcDocumentStore.OpenAsync(FilePath);
            _document = loaded.Document; _expectedHash = loaded.Receipt.Sha256;
            Refresh(); Status = "DBC 文件已加载（未连接硬件）";
        }
        catch (Exception e) { Error = e.Message; Status = "DBC 导入失败"; }
    }
    private async Task SaveSafeAsync()
    {
        try
        {
            Error = "";
            var receipt = await DbcDocumentStore.SaveAsync(Document, FilePath, _expectedHash);
            _expectedHash = receipt.Sha256;
            _document = DbcWorkbenchDocument.Import(Path.GetFileNameWithoutExtension(FilePath), Document.ExportText());
            Refresh(); Status = $"DBC 已保存，SHA256 {receipt.Sha256[..12]}…";
        }
        catch (Exception e) { Error = e.Message; Status = "DBC 保存失败（编辑仍在内存）"; }
    }
    private void Safe(Action run)
    {
        try { Error = ""; run(); }
        catch (Exception e) { Error = e.Message; Status = "DBC 操作失败"; }
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
