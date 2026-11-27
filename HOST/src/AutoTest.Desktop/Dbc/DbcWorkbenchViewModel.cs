using System.Collections.ObjectModel;
using System.Collections.Immutable;
using System.ComponentModel;
using AutoTest.Projects.Dbc.Editing;
using System.IO;
using System.Globalization;
using AutoTest.Desktop.Projects;
using AutoTest.Projects.Dbc;
using AutoTest.Projects.Project;
using Prism.Commands;
using Prism.Mvvm;

namespace AutoTest.Desktop.Dbc;

public sealed record DbcMessageRow(DbcMessage Message, DbcDisplayNames DisplayNames)
{
    public string HexId => "0x" + Message.ArbitrationId.ToString("X");
    public string FrameType => Message.IsExtended ? "扩展帧" : "标准帧";
    public string DecimalId => Message.Id.ToString();
    public string Name => Message.Name;
    public string Alias => DisplayNames.MessageAlias(Message.Id);
    public string DisplayName => DisplayNames.MessageLabel(Message);
    public string NavTitle => string.IsNullOrWhiteSpace(DisplayNames.MessageAlias(Message.Id))
        ? Message.Name : DisplayNames.MessageAlias(Message.Id);
    public string Dlc => Message.Dlc.ToString();
    public string Transmitter => Message.Transmitter;
    public string Summary => DisplayNames.MessageCommentPreview(Message.Id);
    public string Caption => DisplayName + " · " + HexId;
}
public sealed class DbcSignalRow : BindableBase, IEditableObject
{
    public DbcSignal Signal { get; private set; }
    public string DisplayName { get; }
    public string Summary { get; }
    private string _alias;
    private string _acceptedAlias;
    public string Alias { get => _alias; set => SetProperty(ref _alias, value ?? ""); }
    public bool HasAliasDraft => !string.Equals(_alias, _acceptedAlias, StringComparison.Ordinal);
    public void AcceptAlias()
    {
        _acceptedAlias = _alias;
        RaisePropertyChanged(nameof(Alias));
    }
    public void ResetAlias() => Alias = _acceptedAlias;
    public int EnumCount { get; }
    public DbcSignalEditState Draft { get; private set; }
    public bool IsEditing { get; private set; }
    public DbcSignalRow(DbcSignal signal, string displayName, string summary,
        string alias = "", int enumCount = 0)
    {
        Signal = signal; DisplayName = displayName; Summary = summary;
        _alias = _acceptedAlias = alias; EnumCount = enumCount;
        Draft = DbcSignalEditState.FromSignal(signal);
    }
    public string Name => Signal.Name;
    public string NavTitle => string.IsNullOrEmpty(Alias) ? Name : Alias;
    public string StartBit => Signal.StartBit.ToString(CultureInfo.InvariantCulture);
    public string BitLength => Signal.BitLength.ToString(CultureInfo.InvariantCulture);
    public string Order => Signal.Intel ? "Intel" : "Motorola";
    public string ByteOrder => Order;
    public string SignType => Signed;
    public string Signed => Signal.Signed ? "有符号" : "无符号";
    public string Factor => DbcSignalEditState.DisplayDouble(Signal.Factor);
    public string Offset => DbcSignalEditState.DisplayDouble(Signal.Offset);
    public string MinMax => Minimum + " ～ " + Maximum;
    public string Minimum => DbcSignalEditState.DisplayDouble(Signal.Minimum);
    public string Maximum => DbcSignalEditState.DisplayDouble(Signal.Maximum);
    public string Unit => Signal.Unit;
    public string Receiver => Signal.Receiver;
    public string Multiplexer => Signal.Multiplexer ?? "";
    public IReadOnlyList<string> ByteOrders { get; } = new[] { "Intel", "Motorola" };
    public IReadOnlyList<string> SignTypes { get; } = new[] { "无符号", "有符号" };
    public void Accept(DbcSignal signal)
    {
        Signal = signal; Draft = DbcSignalEditState.FromSignal(signal);
        RaisePropertyChanged(string.Empty);
    }
    public void BeginEdit() => IsEditing = true;
    public void EndEdit() => IsEditing = false;
    public void CancelEdit()
    {
        IsEditing = false;
        ResetAlias();
        if (!Draft.Matches(Signal)) Accept(Signal);
    }
}

/// <summary>Signal parameter form draft. Validate all fields in one transaction.</summary>
public sealed class DbcSignalEditState : BindableBase
{
    private string _name = "";
    public string Name { get => _name; set => SetProperty(ref _name, value ?? ""); }
    private string _startBit = "";
    public string StartBit { get => _startBit; set => SetProperty(ref _startBit, value); }
    private string _bitLength = "";
    public string BitLength { get => _bitLength; set => SetProperty(ref _bitLength, value); }
    private string _factor = "";
    public string Factor { get => _factor; set => SetProperty(ref _factor, value); }
    private string _offset = "";
    public string Offset { get => _offset; set => SetProperty(ref _offset, value); }
    private string _minimum = "";
    public string Minimum { get => _minimum; set => SetProperty(ref _minimum, value); }
    private string _maximum = "";
    public string Maximum { get => _maximum; set => SetProperty(ref _maximum, value); }
    private string _unit = "";
    public string Unit { get => _unit; set => SetProperty(ref _unit, value); }
    private string _receiver = "";
    public string Receiver { get => _receiver; set => SetProperty(ref _receiver, value); }
    private string _multiplexer = "";
    public string Multiplexer { get => _multiplexer; set => SetProperty(ref _multiplexer, value); }
    private bool _intel = false;
    public bool Intel { get => _intel; set => SetProperty(ref _intel, value); }
    private bool _signed = false;
    public bool Signed { get => _signed; set => SetProperty(ref _signed, value); }
    public string ByteOrder { get => Intel ? "Intel" : "Motorola"; set { if (value is "Intel" or "Motorola") { Intel = value == "Intel"; RaisePropertyChanged(); } } }
    public string SignType { get => Signed ? "有符号" : "无符号"; set { if (value is "有符号" or "无符号") { Signed = value == "有符号"; RaisePropertyChanged(); } } }
    /// <summary>Human-readable decimal; the unchanged original double is kept
    /// verbatim when a different DBC field is edited.</summary>
    public static string DisplayDouble(double value) =>
        value.ToString("G12", CultureInfo.InvariantCulture);

    public static double ResolveEditedNumber(string text, double original, string label)
    {
        if (text == DisplayDouble(original)) return original;
        if (!double.TryParse(text, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var parsed) || !double.IsFinite(parsed))
            throw new FormatException(label + "须是有效有限数值（英文小数点）。");
        return parsed;
    }

    public static DbcSignalEditState FromSignal(DbcSignal signal) => new()
    {
        Name = signal.Name,
        StartBit = signal.StartBit.ToString(CultureInfo.InvariantCulture),
        BitLength = signal.BitLength.ToString(CultureInfo.InvariantCulture),
        Factor = DisplayDouble(signal.Factor),
        Offset = DisplayDouble(signal.Offset),
        Minimum = DisplayDouble(signal.Minimum),
        Maximum = DisplayDouble(signal.Maximum),
        Unit = signal.Unit, Receiver = signal.Receiver,
        Multiplexer = signal.Multiplexer ?? "", Intel = signal.Intel, Signed = signal.Signed
    };

    public bool Matches(DbcSignal signal) =>
        Name == signal.Name &&
        StartBit == signal.StartBit.ToString(CultureInfo.InvariantCulture) &&
        BitLength == signal.BitLength.ToString(CultureInfo.InvariantCulture) &&
        Factor == DisplayDouble(signal.Factor) &&
        Offset == DisplayDouble(signal.Offset) &&
        Minimum == DisplayDouble(signal.Minimum) &&
        Maximum == DisplayDouble(signal.Maximum) &&
        Unit == signal.Unit && Receiver == signal.Receiver &&
        Multiplexer == (signal.Multiplexer ?? "") &&
        Intel == signal.Intel && Signed == signal.Signed;
}

/// <summary>
/// WPF state only: parsing, semantic validation and serialization remain in AutoTest.Projects.
/// Numeric forms are applied explicitly, not on every keystroke.
/// </summary>
public sealed class DbcWorkbenchViewModel : BindableBase
{
    private readonly Stack<DbcWorkbenchDocument.EditCheckpoint> _undo = new();
    private readonly Stack<DbcWorkbenchDocument.EditCheckpoint> _redo = new();
    public bool CanChangeStructure => CanEdit && !_session.Document.IsSourcePreserving;
    public bool CanUndo => _undo.Count > 0 && !IsBusy;
    public bool CanRedo => _redo.Count > 0 && !IsBusy;
    public bool HasOpenEditor { get; set; }
    public bool HasActiveGridEdit => Signals.Any(r => r.IsEditing);
    public DbcMessage? CurrentMessageModel => SelectedMessage is { } row
        ? _session.Document.Model.Messages.FirstOrDefault(m => m.Id == row.Message.Id) : null;
    public IReadOnlyDictionary<long, string> SelectedEnums =>
        SelectedMessage is { } m && SelectedSignal is { } sg
        ? _session.Document.ValueTables.FirstOrDefault(t => t.MessageId == m.Message.Id &&
            t.SignalName == sg.Name)?.Labels ?? ImmutableDictionary<long, string>.Empty
        : ImmutableDictionary<long, string>.Empty;

    private void Remember(DbcWorkbenchDocument.EditCheckpoint before)
    {
        _undo.Push(before); _redo.Clear();
        if (_undo.Count > 80)
        {
            var latest = _undo.Take(80).Reverse().ToArray();
            _undo.Clear();
            foreach (var checkpoint in latest) _undo.Push(checkpoint);
        }
        RefreshCommands();
    }

    public void UndoEdit(bool redo = false)
    {
        if (IsBusy || HasOpenEditor || HasActiveGridEdit || !TryCommitPendingSignal()) return;
        var from = redo ? _redo : _undo;
        var to = redo ? _undo : _redo;
        if (from.Count == 0) return;
        var id = SelectedMessage?.Message.Id; var signal = SelectedSignal?.Name;
        to.Push(_session.Document.CaptureCheckpoint());
        _session = _session with { Document = DbcWorkbenchDocument.RecreateHistoricalDraft(from.Pop()) };
        RefreshDocument(id, signal);
        StatusMessage = redo ? "已重做修改。" : "已撤销修改。";
    }

    public void CheckDocument()
    {
        if (!TryCommitPendingSignal()) return;
        try
        {
            _session.Document.Model.Validate();
            _ = _session.Document.ExportText();
            StatusMessage = "DBC 结构校验通过；自动保存继续核对文件与工程资源修订。";
        }
        catch (Exception error) when (error is ProjectDataException or ArgumentException or FormatException)
        { StatusMessage = "DBC 检查失败：" + error.Message; }
    }

    public bool TrySaveNotes(bool message, string alias, string summary, string comment)
    {
        if (IsBusy || SelectedMessage is not { } m || (!message && SelectedSignal is null)) return false;
        if (alias == (message ? MessageAliasText : SignalAliasText) &&
            summary == (message ? MessageSummaryText : SignalSummaryText) &&
            comment == (message ? MessageCommentText : SignalCommentText)) return true;
        var before = _session.Document.CaptureCheckpoint();
        try
        {
            if (message)
            {
                if (alias != MessageAliasText) _session.Document.SetMessageAlias(m.Message.Id, alias);
                if (summary != MessageSummaryText) _session.Document.SetMessageSummary(m.Message.Id, summary);
                if (comment != MessageCommentText) _session.Document.SetMessageComment(m.Message.Id, comment);
            }
            else
            {
                var name = SelectedSignal!.Name;
                if (alias != SignalAliasText) _session.Document.SetSignalAlias(m.Message.Id, name, alias);
                if (summary != SignalSummaryText) _session.Document.SetSignalSummary(m.Message.Id, name, summary);
                if (comment != SignalCommentText) _session.Document.SetSignalComment(m.Message.Id, name, comment);
            }
            var keep = SelectedSignal?.Name;
            RefreshDocument(m.Message.Id, keep);
            Remember(before);
            StatusMessage = "名称与注释已修改。";
            return true;
        }
        catch (Exception error) when (error is ProjectDataException or ArgumentException)
        {
            _session.Document.RestoreCheckpoint(before);
            StatusMessage = "说明未应用：" + error.Message;
            return false;
        }
    }

    public bool TrySaveEnums(IReadOnlyDictionary<long, string> values)
    {
        if (!CanChangeStructure || IsBusy || SelectedMessage is not { } m || SelectedSignal is not { } sg) return false;
        if (SelectedEnums.Count == values.Count && SelectedEnums.All(p => values.TryGetValue(p.Key, out var v) && v == p.Value)) return true;
        var before = _session.Document.CaptureCheckpoint();
        try
        {
            _session.Document.ReplaceValueDescriptions(m.Message.Id, sg.Name, values);
            RefreshDocument(m.Message.Id, sg.Name);
            Remember(before);
            StatusMessage = "枚举值已修改。";
            return true;
        }
        catch (Exception error) when (error is ProjectDataException or ArgumentException)
        { StatusMessage = "枚举未应用：" + error.Message; return false; }
    }

    public void AddMessage()
    {
        if (!CanChangeStructure || IsBusy || !TryCommitPendingSignal()) return;
        var model = _session.Document.Model;
        var id = Enumerable.Range(0, 2048).Select(x => (uint)x)
            .FirstOrDefault(x => !model.Messages.Any(m => m.Id == x), uint.MaxValue);
        if (id == uint.MaxValue) { StatusMessage = "没有空闲标准帧 ID。"; return; }
        var name = "Message_" + id.ToString("X");
        var edited = _dialogs.EditMessage(new DbcMessage(id, name, 8,
            model.Nodes.FirstOrDefault() ?? "Vector__XXX", []), allowIdChange: true);
        if (edited is null) return;
        var before = _session.Document.CaptureCheckpoint();
        try { _session.Document.AddMessage(edited); RefreshDocument(edited.Id, null); Remember(before); }
        catch (Exception error) when (error is ProjectDataException or ArgumentException)
        { StatusMessage = error.Message; }
    }

    public void AddSignal()
    {
        if (!CanChangeStructure || IsBusy || !TryCommitPendingSignal() || CurrentMessageModel is not { } m) return;
        if (m.Dlc == 0) { StatusMessage = "DLC 为 0，无法添加信号。"; return; }
        var occupied = m.Signals.SelectMany(DbcSignalCodec.BitPositions).ToHashSet();
        var bit = Enumerable.Range(0, m.Dlc * 8).FirstOrDefault(x => !occupied.Contains(x), -1);
        if (bit < 0) { StatusMessage = "报文没有未占用位，请先调整已有信号。"; return; }
        var i = 1; while (m.Signals.Any(s => s.Name == "Signal_" + i)) i++;
        var edited = _dialogs.EditSignal(new DbcSignal("Signal_" + i, bit, 1, true, false,
            1, 0, 0, 1, "", _session.Document.Model.Nodes.FirstOrDefault() ?? "Vector__XXX"));
        if (edited is null) return;
        var before = _session.Document.CaptureCheckpoint();
        try { _session.Document.AddSignal(m.Id, edited); RefreshDocument(m.Id, edited.Name); Remember(before); }
        catch (Exception error) when (error is ProjectDataException or ArgumentException)
        { StatusMessage = error.Message; }
    }

    public void DeleteSelected(bool message)
    {
        if (!CanChangeStructure || IsBusy || !TryCommitPendingSignal() || SelectedMessage is not { } m) return;
        if (!message && SelectedSignal is null) return;
        var name = message ? m.Name : SelectedSignal!.Name;
        if (!_dialogs.ConfirmDiscard("删除 " + name + "？已有 TEST/波形引用可能需要更新。")) return;
        var before = _session.Document.CaptureCheckpoint();
        try
        {
            if (message) _session.Document.DeleteMessage(m.Message.Id);
            else _session.Document.DeleteSignal(m.Message.Id, name);
            RefreshDocument(message ? null : m.Message.Id, null);
            Remember(before);
        }
        catch (Exception error) when (error is ProjectDataException or ArgumentException)
        { StatusMessage = error.Message; }
    }

    private readonly ProjectWorkspaceService? _workspace;
    private readonly DbcEditorDialogs _dialogs;
    private DbcEditorSession _session;
    private DbcMessageRow? _selectedMessage;
    private DbcSignalRow? _selectedSignal;
    private bool _isBusy;
    private bool _settingAlias;
    private string _messageAliasText = "";
    private string _signalAliasText = "";
    private string _messageSummaryText = "";
    private string _signalSummaryText = "";
    private string _messageCommentText = "";
    private string _signalCommentText = "";
    private DbcSignalEditState? _signalDraft;
    private bool _committingPendingSignal;
    private string _status = "就绪";
    private string _messageSearchText = "";

    public DbcWorkbenchViewModel(
        ProjectWorkspaceService? workspace, DbcEditorDialogs dialogs, DbcEditorSession opened)
    {
        _workspace = workspace;
        _dialogs = dialogs;
        _session = opened;
        EditMessageCommand = new DelegateCommand(EditMessage, () => CanEdit && SelectedMessage is not null && !IsBusy);
        EditSignalCommand = new DelegateCommand(EditSignal, () => CanEdit && SelectedSignal is not null && !IsBusy);
        ApplySignalParametersCommand = new DelegateCommand(ApplySignalParameters,
            () => CanEdit && SelectedSignal is not null && !IsBusy);
        SaveCommand = new DelegateCommand(async () => await SaveAsync(), () => CanSave && IsDirty && !IsBusy);
        ReloadCommand = new DelegateCommand(async () => await ReloadAsync(), () => !IsBusy);
        RefreshDocument(null, null);
    }

    /// <summary>
    /// The standalone file adapter reuses the exact DBC document/editor and
    /// conflict-checked disk store, without creating or altering an ECU project.
    /// Synthetic session IDs are ephemeral editor bookkeeping only.
    /// </summary>
    public static DbcWorkbenchViewModel ForIndependentFile(
        DbcEditorDialogs dialogs, DbcWorkbenchDocument document, DbcFileReceipt receipt) =>
        new(null, dialogs, new DbcEditorSession(
            AutoTest.Core.ProjectId.New(), "", "", AutoTest.Core.AssetId.New(),
            receipt.Revision, Path.GetFileNameWithoutExtension(receipt.Path),
            document, receipt));

    public bool IsIndependentFile => _workspace is null;
    public string SourceDescription => IsIndependentFile
        ? "独立 DBC 文件 · 修改自动保存回原文件，不加入 ECU 工程"
        : "当前 ECU 工程 DBC · 有效修改自动保存并更新资源修订";

    public ObservableCollection<DbcMessageRow> Messages { get; } = new();
    public ObservableCollection<DbcSignalRow> Signals { get; } = new();

    public string AssetName => _session.AssetName;
    public string FilePath => _session.FileReceipt.Path;
    public int AssetRevision => _session.AssetRevision;
    public string MessageCount => Messages.Count + "/" +
        _session.Document.Model.Messages.Length; 
    public string MessageSearchText
    {
        get => _messageSearchText;
        set
        {
            if (!TryCommitPendingSignal()) return;
            if (!SetProperty(ref _messageSearchText, value ?? "")) return;
            var keepMessage = SelectedMessage?.Message.Id;
            var keepSignal = SelectedSignal?.Name;
            RefreshMessages(keepMessage);
            RefreshSignals(keepSignal);
        }
    }
    public string SignalCount => SelectedMessage?.Message.Signals.Length.ToString() ?? "0";
    public bool IsSignalSelected => SelectedMessage is not null && SelectedSignal is not null;
    public bool IsMessageSelected => SelectedMessage is not null && SelectedSignal is null;
    public DbcSignalEditState? SignalDraft
    {
        get => _signalDraft;
        private set => SetProperty(ref _signalDraft, value);
    }
    public string SelectedValueDescriptions
    {
        get
        {
            if (SelectedMessage is null || SelectedSignal is null) return "未选择信号";
            var table = _session.Document.ValueTables.FirstOrDefault(
                t => t.MessageId == SelectedMessage.Message.Id && t.SignalName == SelectedSignal.Name);
            return table is null ? "（无枚举值说明）"
                : string.Join("、", table.Labels.OrderBy(x => x.Key)
                    .Select(x => x.Key + " = " + x.Value));
        }
    }
    public string StateDescription => _session.Document.IsSourcePreserving
        ? "原文保护模式"
        : _session.Document.CanEdit ? "结构可编辑" : "原始结构只读";
    public string UnsupportedDescription => _session.Document.IsSourcePreserving
        ? "保留其他属性、枚举值和原文；允许编辑原始 CM_ 注释，CAN ID 和原始信号名称受保护。"
        : _session.Document.CanEdit ? ""
        : "原始 DBC 存在不支持的结构；只允许安全修改中文别名和可识别 CM_ 注释。原因：" +
          string.Join("、", _session.Document.UnsupportedDirectives.Take(12));
    public bool CanEdit => _session.Document.CanEdit;
    public bool CanSave => !IsBusy && _session.Document.CanSave;
    public bool HasPendingSignalDraft => Signals.Any(row =>
        !row.Draft.Matches(row.Signal) || row.HasAliasDraft);
    public bool IsDirty => _session.Document.IsDirty || HasPendingSignalDraft;
    public string MessageAliasText
    {
        get => _messageAliasText;
        set
        {
            if (!SetProperty(ref _messageAliasText, value ?? "") || _settingAlias ||
                SelectedMessage is not { } message) return;
            try
            {
                _session.Document.SetMessageAlias(message.Message.Id, value);
                StatusMessage = "报文中文别名已修改，等待自动保存。";
                RefreshCommands();
            }
            catch (ProjectDataException error) { StatusMessage = error.Message; }
        }
    }
    public string MessageSummaryText
    {
        get => _messageSummaryText;
        set
        {
            if (!SetProperty(ref _messageSummaryText, value ?? "") || _settingAlias ||
                SelectedMessage is not { } message) return;
            try
            {
                _session.Document.SetMessageSummary(message.Message.Id, value);
                StatusMessage = "报文功能摘要已修改，等待自动保存。";
                RefreshCommands();
            }
            catch (ProjectDataException error) { StatusMessage = error.Message; }
        }
    }
    public string SignalSummaryText
    {
        get => _signalSummaryText;
        set
        {
            if (!SetProperty(ref _signalSummaryText, value ?? "") || _settingAlias ||
                SelectedMessage is not { } message || SelectedSignal is not { } signal) return;
            try
            {
                _session.Document.SetSignalSummary(message.Message.Id, signal.Name, value);
                StatusMessage = "信号功能摘要已修改，等待自动保存。";
                RefreshCommands();
            }
            catch (ProjectDataException error) { StatusMessage = error.Message; }
        }
    }
    public string MessageCommentText
    {
        get => _messageCommentText;
        set
        {
            if (!SetProperty(ref _messageCommentText, value ?? "") || _settingAlias ||
                SelectedMessage is not { } message) return;
            try
            {
                _session.Document.SetMessageComment(message.Message.Id, value);
                StatusMessage = "报文详细注释已修改，将自动写入 CM_ BO_。";
                RefreshCommands();
            }
            catch (ProjectDataException error) { StatusMessage = error.Message; }
        }
    }

    public string SignalCommentText
    {
        get => _signalCommentText;
        set
        {
            if (!SetProperty(ref _signalCommentText, value ?? "") || _settingAlias ||
                SelectedMessage is not { } message || SelectedSignal is not { } signal) return;
            try
            {
                _session.Document.SetSignalComment(message.Message.Id, signal.Name, value);
                StatusMessage = "信号详细注释已修改，将自动写入 CM_ SG_。";
                RefreshCommands();
            }
            catch (ProjectDataException error) { StatusMessage = error.Message; }
        }
    }

    // F2 / double-click edits the display alias, never the DBC BO_/SG_ identifier.
    public bool RenameSelectedMessage(string alias)
    {
        if (IsBusy || SelectedMessage is not { } message) return false;
        try
        {
            var before = _session.Document.CaptureCheckpoint();
            _session.Document.SetMessageAlias(message.Message.Id, alias);
            RefreshMessages(message.Message.Id);
            Remember(before);
            StatusMessage = "报文中文别名已修改，等待自动保存。";
            RefreshCommands();
            return true;
        }
        catch (Exception e) when (e is ProjectDataException or ArgumentException)
        {
            StatusMessage = "重命名失败：" + e.Message;
            return false;
        }
    }

    public bool RenameSelectedSignal(string alias)
    {
        if (IsBusy || SelectedMessage is not { } message || SelectedSignal is not { } signal)
            return false;
        try
        {
            var before = _session.Document.CaptureCheckpoint();
            _session.Document.SetSignalAlias(message.Message.Id, signal.Name, alias);
            RefreshSignals(signal.Name);
            Remember(before);
            StatusMessage = "信号中文别名已修改，等待自动保存。";
            RefreshCommands();
            return true;
        }
        catch (Exception e) when (e is ProjectDataException or ArgumentException)
        {
            StatusMessage = "重命名失败：" + e.Message;
            return false;
        }
    }

    public string SelectedMessageDefinition => SelectedMessage is { } m
        ? _session.Document.DisplayNames.MessageDescription(m.Message.Id) : "";
    public string SelectedSignalDefinition => SelectedMessage is { } m && SelectedSignal is { } sg
        ? _session.Document.DisplayNames.SignalDescription(m.Message.Id, sg.Name) : "";
    public string SignalAliasText
    {
        get => _signalAliasText;
        set
        {
            if (!SetProperty(ref _signalAliasText, value ?? "") || _settingAlias ||
                SelectedMessage is not { } message || SelectedSignal is not { } signal) return;
            try
            {
                _session.Document.SetSignalAlias(message.Message.Id, signal.Name, value);
                StatusMessage = "信号中文别名已修改，等待自动保存。";
                RefreshCommands();
            }
            catch (ProjectDataException error) { StatusMessage = error.Message; }
        }
    }
    private void RefreshAliasInputs()
    {
        _settingAlias = true;
        try
        {
            MessageAliasText = SelectedMessage is { } message
                ? _session.Document.DisplayNames.MessageAlias(message.Message.Id) : "";
            SignalAliasText = SelectedMessage is { } m && SelectedSignal is { } sig
                ? _session.Document.DisplayNames.SignalAlias(m.Message.Id, sig.Name) : "";
            MessageSummaryText = SelectedMessage is { } msg
                ? _session.Document.DisplayNames.ExplicitMessageSummary(msg.Message.Id) : "";
            SignalSummaryText = SelectedMessage is { } m2 && SelectedSignal is { } sg2
                ? _session.Document.DisplayNames.ExplicitSignalSummary(m2.Message.Id, sg2.Name) : "";
            MessageCommentText = SelectedMessage is { } msg3
                ? _session.Document.DisplayNames.MessageDescription(msg3.Message.Id) : "";
            SignalCommentText = SelectedMessage is { } m3 && SelectedSignal is { } sg3
                ? _session.Document.DisplayNames.SignalDescription(m3.Message.Id, sg3.Name) : "";
            if (SignalDraft is { } previousDraft) previousDraft.PropertyChanged -= SignalDraftChanged;
            SignalDraft = SelectedSignal?.Draft;
            if (SignalDraft is { } draft) draft.PropertyChanged += SignalDraftChanged;
            RaisePropertyChanged(nameof(SelectedMessageDefinition));
            RaisePropertyChanged(nameof(SelectedSignalDefinition));
            RaisePropertyChanged(nameof(CurrentMessageModel));
        }
        finally { _settingAlias = false; }
    }
    private void SignalDraftChanged(object? sender, PropertyChangedEventArgs e) => RefreshCommands();

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value)) RefreshCommands();
        }
    }
    public string StatusMessage
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    /// <summary>
    /// Avoid silently dropping a partially edited signal when the operator
    /// selects a different signal, message or filter. Valid changes are
    /// applied to the existing DBC document; invalid values keep selection.
    /// </summary>
    private bool TryCommitPendingSignal()
    {
        if (_committingPendingSignal || !HasPendingSignalDraft) return true;
        if (!CanEdit)
        {
            StatusMessage = "当前 DBC 结构只读；请撤销尚未应用的参数修改。";
            return false;
        }
        _committingPendingSignal = true;
        try
        {
            ApplySignalParameters();
            if (HasPendingSignalDraft)
            {
                StatusMessage = "当前信号参数无效：请修正后再切换对象，不能丢弃草稿。";
                return false;
            }
            return true;
        }
        finally { _committingPendingSignal = false; }
    }

    public DbcMessageRow? SelectedMessage
    {
        get => _selectedMessage;
        set
        {
            var previousId = _selectedMessage?.Message.Id;
            var requestedId = value?.Message.Id;
            if (previousId != requestedId && !TryCommitPendingSignal())
            {
                RaisePropertyChanged(nameof(SelectedMessage));
                return;
            }
            // Applying a draft may replace row instances; use the current row.
            if (value is not null)
                value = Messages.FirstOrDefault(row => row.Message.Id == requestedId) ?? value;
            if (!SetProperty(ref _selectedMessage, value)) return;
            var previousSignal = previousId == requestedId ? _selectedSignal?.Name : null;
            RefreshSignals(previousSignal);
            RaisePropertyChanged(nameof(SignalCount));
            RaisePropertyChanged(nameof(SelectedValueDescriptions));
            RefreshAliasInputs();
            RaisePropertyChanged(nameof(IsSignalSelected));
            RaisePropertyChanged(nameof(IsMessageSelected));
            RefreshCommands();
        }
    }

    public DbcSignalRow? SelectedSignal
    {
        get => _selectedSignal;
        set
        {
            var requested = value?.Name;
            if (_selectedSignal?.Name != requested && !TryCommitPendingSignal())
            {
                RaisePropertyChanged(nameof(SelectedSignal));
                return;
            }
            if (value is not null)
                value = Signals.FirstOrDefault(row => row.Name == requested) ?? value;
            if (!SetProperty(ref _selectedSignal, value)) return;
            RaisePropertyChanged(nameof(SelectedValueDescriptions));
            RaisePropertyChanged(nameof(IsSignalSelected));
            RaisePropertyChanged(nameof(IsMessageSelected));
            RefreshAliasInputs();
            RefreshCommands();
        }
    }

    public DelegateCommand EditMessageCommand { get; }
    public DelegateCommand EditSignalCommand { get; }
    public DelegateCommand ApplySignalParametersCommand { get; }
    public DelegateCommand SaveCommand { get; }
    public DelegateCommand ReloadCommand { get; }

    public void EditMessage()
    {
        if (!CanEdit || IsBusy || SelectedMessage is not { } chosen) return;
        if (!TryCommitPendingSignal()) return;
        var edited = _dialogs.EditMessage(CurrentMessageModel ?? chosen.Message,
            CanChangeStructure && !_session.Document.ValueTables.Any(t => t.MessageId == chosen.Message.Id));
        if (edited is null || edited == (CurrentMessageModel ?? chosen.Message)) return;
        try
        {
            var before = _session.Document.CaptureCheckpoint();
            _session.Document.UpdateMessage(chosen.Message.Id, edited);
            RefreshDocument(edited.Id, null);
            Remember(before);
            StatusMessage = "报文参数已修改，等待自动保存。";
        }
        catch (Exception error) when (error is ProjectDataException or ArgumentException)
        {
            _dialogs.ShowError(error.Message);
        }
    }

    public void EditSignal()
    {
        if (!CanEdit || IsBusy || SelectedMessage is not { } message || SelectedSignal is not { } chosen)
            return;
        if (!TryCommitPendingSignal()) return;
        var edited = _dialogs.EditSignal(chosen.Signal, !CanChangeStructure || SelectedEnums.Count > 0);
        if (edited is null || edited == chosen.Signal) return;
        try
        {
            var before = _session.Document.CaptureCheckpoint();
            _session.Document.UpdateSignal(message.Message.Id, chosen.Name, edited);
            RefreshDocument(message.Message.Id, edited.Name);
            Remember(before);
            StatusMessage = "信号参数已修改，等待自动保存。";
        }
        catch (Exception error) when (error is ProjectDataException or ArgumentException)
        {
            _dialogs.ShowError(error.Message);
        }
    }

    /// <summary>Apply a complete edited signal shape once, after full validation.</summary>
    public void ApplySignalParameters()
    {
        foreach (var row in Signals.Where(r =>
            !r.Draft.Matches(r.Signal) || r.HasAliasDraft).ToArray())
            if (!TryCommitSignalRow(row)) return;
    }

    public void ResetSignalRow(DbcSignalRow row)
    {
        row.CancelEdit();
        if (ReferenceEquals(row, SelectedSignal)) RefreshAliasInputs();
        RefreshCommands();
    }

    public bool TryCommitSignalRow(DbcSignalRow row)
    {
        if (row.Draft.Matches(row.Signal) && !row.HasAliasDraft) return true;
        if (!CanEdit || IsBusy || SelectedMessage is not { } message || !Signals.Contains(row))
            return false;
        try
        {
            var draft = row.Draft;
            static int Integer(string input, string label) =>
                int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                    ? value : throw new FormatException(label + "须是有效整数。");
            var original = row.Signal;
            var editedName = draft.Name.Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(editedName,
                    @"^[A-Za-z_][A-Za-z0-9_]*$",
                    System.Text.RegularExpressions.RegexOptions.CultureInvariant,
                    TimeSpan.FromMilliseconds(100)))
                throw new FormatException("信号技术名称只能使用英文字母、数字和下划线，不能以数字开头。");
            var updated = original with
            {
                Name = editedName,
                StartBit = Integer(draft.StartBit, "起始位"), BitLength = Integer(draft.BitLength, "位长"),
                Intel = draft.Intel, Signed = draft.Signed,
                Factor = DbcSignalEditState.ResolveEditedNumber(draft.Factor, original.Factor, "因子"),
                Offset = DbcSignalEditState.ResolveEditedNumber(draft.Offset, original.Offset, "偏移"),
                Minimum = DbcSignalEditState.ResolveEditedNumber(draft.Minimum, original.Minimum, "最小值"),
                Maximum = DbcSignalEditState.ResolveEditedNumber(draft.Maximum, original.Maximum, "最大值"),
                Unit = draft.Unit, Receiver = draft.Receiver.Trim(),
                Multiplexer = string.IsNullOrWhiteSpace(draft.Multiplexer) ? null : draft.Multiplexer.Trim()
            };
            if (updated.Unit.Contains('"') || updated.Unit.Any(char.IsControl))
                throw new FormatException("单位不得包含引号或控制字符。");
            if (!System.Text.RegularExpressions.Regex.IsMatch(updated.Receiver, @"^\w+$") ||
                (updated.Multiplexer is { } mux && mux != "M" &&
                    !System.Text.RegularExpressions.Regex.IsMatch(mux, @"^m\d+$")))
                throw new FormatException("接收节点或复用标记不符合 DBC 格式。");
            if (updated != original || row.HasAliasDraft)
            {
                var before = _session.Document.CaptureCheckpoint();
                try
                {
                    if (updated != original)
                    {
                        updated.Validate(message.Message.Dlc);
                        _session.Document.UpdateSignal(message.Message.Id, original.Name, updated);
                    }
                    if (row.HasAliasDraft)
                        _session.Document.SetSignalAlias(
                            message.Message.Id, updated.Name, row.Alias);
                }
                catch
                {
                    _session.Document.RestoreCheckpoint(before);
                    throw;
                }
                row.Accept(updated);
                row.AcceptAlias();
                Remember(before);
            }
            else row.Accept(original);
            if (ReferenceEquals(row, SelectedSignal)) RefreshAliasInputs();
            RaisePropertyChanged(nameof(CurrentMessageModel));
            StatusMessage = "信号参数已更新，自动保存后仍可撤销。";
            RefreshCommands();
            return true;
        }
        catch (Exception error) when (error is FormatException or ArgumentException or ProjectDataException)
        {
            StatusMessage = row.Name + "：参数未应用 — " + error.Message;
            return false;
        }
    }

    public bool ConfirmClose() =>
        !IsBusy && (!IsDirty || _dialogs.ConfirmDiscard("DBC 尚有未保存修改。确定关闭并放弃这些修改吗？"));

    public async Task<bool> SaveForCloseAsync()
    {
        if (IsBusy || HasOpenEditor || HasActiveGridEdit) return false;
        if (!IsDirty) return true;
        // This contract is also used by the shell's idle autosave. Failures
        // stay visible in StatusMessage without interrupting batch editing with
        // a system dialog; explicit Check DBC is available to diagnose syntax.
        await SaveAsync(silent: true);
        return !IsBusy && !IsDirty;
    }

    public async Task AutoSaveAsync()
    {
        if (IsBusy || HasOpenEditor || HasActiveGridEdit || HasPendingSignalDraft || !IsDirty) return;
        await SaveAsync(silent: true);
    }

    private async Task SaveAsync(bool silent = false)
    {
        if (!CanSave || !IsDirty || IsBusy || HasOpenEditor) return;
        // Ctrl+S must include parameters visible in the inspector, not discard
        // an un-applied form while saving only CM_ or alias edits.
        if (HasPendingSignalDraft)
        {
            ApplySignalParameters();
            if (HasPendingSignalDraft)
            {
                if (!silent) _dialogs.ShowError("信号技术参数尚未通过校验，请先修正参数再保存。");
                return;
            }
        }
        if (!_session.Document.IsDirty) return;
        IsBusy = true;
        try
        {
            var messageId = SelectedMessage?.Message.Id;
            var signalName = SelectedSignal?.Name;
            if (_workspace is null)
            {
                var receipt = await DbcDocumentStore.SaveAsync(
                    _session.Document, _session.FileReceipt.Path, _session.FileReceipt.Sha256);
                var verified = await DbcDocumentStore.OpenAsync(receipt.Path);
                if (!string.Equals(verified.Receipt.Sha256, receipt.Sha256,
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("独立 DBC 保存后的回读校验失败。");
                _session = _session with
                {
                    Document = verified.Document, FileReceipt = verified.Receipt,
                    AssetRevision = verified.Receipt.Revision
                };
                StatusMessage = "已保存独立 DBC 文件；未修改当前工程资源。";
            }
            else
            {
                _session = await _workspace.SaveDbcEditorAsync(_session);
                StatusMessage = "DBC 已保存、重新读取并更新工程资源修订 " + AssetRevision;
            }
            if (silent)
            {
                // The canonical DBC document has been persisted and re-read,
                // but rebuilding observable message/signal rows during an idle
                // save would discard scroll position, selection and keyboard focus.
                // Edits themselves already refresh those rows synchronously.
                RaisePropertyChanged(nameof(AssetRevision));
                RaisePropertyChanged(nameof(FilePath));
                RaisePropertyChanged(nameof(SourceDescription));
                RaisePropertyChanged(nameof(CanEdit));
                RaisePropertyChanged(nameof(StateDescription));
                RaisePropertyChanged(nameof(UnsupportedDescription));
            }
            else
                RefreshDocument(messageId, signalName);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                                        ProjectDataException or InvalidDataException or FormatException or
                                        ArgumentException or NotSupportedException)
        {
            StatusMessage = "保存失败：" + error.Message;
            if (!silent) _dialogs.ShowError("DBC 保存失败。\n" + error.Message +
                "\n如果 DBC 文件已修改而工程清单未成功保存，请检查同目录 .bak 备份。");
        }
        finally { IsBusy = false; RefreshCommands(); }
    }

    private async Task ReloadAsync()
    {
        if (IsBusy || HasOpenEditor) return;
        if (IsDirty && !_dialogs.ConfirmDiscard("重新加载会丢弃未保存的 DBC 参数修改，确定继续吗？"))
            return;
        IsBusy = true;
        try
        {
            var messageId = SelectedMessage?.Message.Id;
            var signalName = SelectedSignal?.Name;
            if (_workspace is null)
            {
                var loaded = await DbcDocumentStore.OpenAsync(_session.FileReceipt.Path);
                _session = _session with
                {
                    Document = loaded.Document, FileReceipt = loaded.Receipt,
                    AssetRevision = loaded.Receipt.Revision
                };
            }
            else
                _session = await _workspace.OpenDbcEditorAsync(_session.AssetId);
            _undo.Clear(); _redo.Clear();
            RefreshDocument(messageId, signalName);
            StatusMessage = "已从磁盘重新加载 DBC";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                                        ProjectDataException or InvalidDataException or FormatException or
                                        ArgumentException or NotSupportedException)
        {
            StatusMessage = "重新加载失败：" + error.Message;
            _dialogs.ShowError(error.Message);
        }
        finally { IsBusy = false; RefreshCommands(); }
    }

    private void RefreshDocument(uint? messageId, string? signalName)
    {
        RefreshMessages(messageId);
        RefreshSignals(signalName);
        RaisePropertyChanged(nameof(MessageCount));
        RaisePropertyChanged(nameof(SignalCount));
        RaisePropertyChanged(nameof(AssetName));
        RaisePropertyChanged(nameof(FilePath));
        RaisePropertyChanged(nameof(AssetRevision));
        RaisePropertyChanged(nameof(SourceDescription));
        RaisePropertyChanged(nameof(SelectedValueDescriptions));
        RaisePropertyChanged(nameof(CanEdit));
        RaisePropertyChanged(nameof(StateDescription));
        RaisePropertyChanged(nameof(UnsupportedDescription));
        RefreshCommands();
    }

    private void RefreshMessages(uint? preferredId)
    {
        var query = MessageSearchText.Trim();
        Messages.Clear();
        foreach (var message in _session.Document.Model.Messages.OrderBy(m => m.Id))
        {
            var names = _session.Document.DisplayNames;
            if (query.Length > 0 && !new[]
                {
                    message.Name, names.MessageLabel(message), names.MessageSummary(message.Id),
                    names.MessageDescription(message.Id),
                    "0x" + message.ArbitrationId.ToString("X"),
                    message.ArbitrationId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    message.Transmitter
                }.Any(value => value.Contains(query, StringComparison.CurrentCultureIgnoreCase)) &&
                !message.Signals.Any(signal => signal.Name.Contains(query,
                    StringComparison.CurrentCultureIgnoreCase) ||
                    names.SignalLabel(message, signal).Contains(query,
                        StringComparison.CurrentCultureIgnoreCase) ||
                    names.SignalCommentPreview(message.Id, signal.Name).Contains(query,
                        StringComparison.CurrentCultureIgnoreCase)))
                continue;
            Messages.Add(new DbcMessageRow(message, names));
        }
        SelectedMessage = Messages.FirstOrDefault(x => x.Message.Id == preferredId)
            ?? Messages.FirstOrDefault();
        RaisePropertyChanged(nameof(MessageCount));
    }

    private void RefreshSignals(string? signalName)
    {
        Signals.Clear();
        if (SelectedMessage is { } message && CurrentMessageModel is { } current)
        {
            foreach (var signal in current.Signals.OrderBy(s => s.StartBit).ThenBy(s => s.Name))
                Signals.Add(new DbcSignalRow(signal,
                    _session.Document.DisplayNames.SignalLabel(message.Message, signal),
                    _session.Document.DisplayNames.SignalCommentPreview(message.Message.Id, signal.Name),
                    _session.Document.DisplayNames.SignalAlias(message.Message.Id, signal.Name),
                    _session.Document.ValueTables.FirstOrDefault(t => t.MessageId == message.Message.Id &&
                        t.SignalName == signal.Name)?.Labels.Count ?? 0));
        }
        SelectedSignal = signalName is null ? null :
            Signals.FirstOrDefault(x => x.Name == signalName);
        RaisePropertyChanged(nameof(SignalCount));
    }

    private void RefreshCommands()
    {
        RaisePropertyChanged(nameof(IsDirty));
        RaisePropertyChanged(nameof(HasPendingSignalDraft));
        EditMessageCommand?.RaiseCanExecuteChanged();
        EditSignalCommand?.RaiseCanExecuteChanged();
        ApplySignalParametersCommand?.RaiseCanExecuteChanged();
        SaveCommand?.RaiseCanExecuteChanged();
        ReloadCommand?.RaiseCanExecuteChanged();
        RaisePropertyChanged(nameof(CanSave));
        RaisePropertyChanged(nameof(CanUndo));
        RaisePropertyChanged(nameof(CanRedo));
        RaisePropertyChanged(nameof(CanChangeStructure));
    }
}
