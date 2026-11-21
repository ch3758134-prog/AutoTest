using System.Collections.ObjectModel;
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
    public string DisplayName => DisplayNames.MessageLabel(Message);
    public string NavTitle => string.IsNullOrWhiteSpace(DisplayNames.MessageAlias(Message.Id))
        ? Message.Name : DisplayNames.MessageAlias(Message.Id);
    public string Dlc => Message.Dlc.ToString();
    public string Transmitter => Message.Transmitter;
    public string Summary => DisplayNames.MessageCommentPreview(Message.Id);
    public string Caption => DisplayName + " · " + HexId;
}
public sealed record DbcSignalRow(DbcSignal Signal, string DisplayName, string Summary)
{
    public string Name => Signal.Name;
    public string NavTitle => DisplayName.Contains(" · ", StringComparison.Ordinal)
        ? DisplayName[..DisplayName.LastIndexOf(" · ", StringComparison.Ordinal)]
        : DisplayName;
    public string StartBit => Signal.StartBit.ToString();
    public string BitLength => Signal.BitLength.ToString();
    public string Order => Signal.Intel ? "Intel" : "Motorola";
    public string Signed => Signal.Signed ? "有符号" : "无符号";
    public string Factor => Signal.Factor.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    public string Offset => Signal.Offset.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    public string MinMax => Signal.Minimum.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
        + " ～ " + Signal.Maximum.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    public string Unit => Signal.Unit;
    public string Receiver => Signal.Receiver;
    public string Multiplexer => Signal.Multiplexer ?? "—";
}

/// <summary>Signal parameter form draft. Validate all fields in one transaction.</summary>
public sealed class DbcSignalEditState : BindableBase
{
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
    private readonly ProjectWorkspaceService _workspace;
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
        ProjectWorkspaceService workspace, DbcEditorDialogs dialogs, DbcEditorSession opened)
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
    public bool CanSave => _session.Document.CanSave;
    public bool HasPendingSignalDraft =>
        SelectedSignal is { } selected && SignalDraft is { } draft &&
        !draft.Matches(selected.Signal);
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
                StatusMessage = "报文中文别名已修改，请保存 DBC。";
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
                StatusMessage = "报文功能摘要已修改（与名称及原始 CM_ 定义分开），请保存 DBC。";
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
                StatusMessage = "信号功能摘要已修改，请保存 DBC。";
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
                StatusMessage = "报文详细注释已修改 · 保存后写回标准 CM_ BO_。";
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
                StatusMessage = "信号详细注释已修改 · 保存后写回标准 CM_ SG_。";
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
            _session.Document.SetMessageAlias(message.Message.Id, alias);
            RefreshMessages(message.Message.Id);
            StatusMessage = "报文中文别名已修改 · Ctrl+S 保存。";
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
            _session.Document.SetSignalAlias(message.Message.Id, signal.Name, alias);
            RefreshSignals(signal.Name);
            StatusMessage = "信号中文别名已修改 · Ctrl+S 保存。";
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
                StatusMessage = "信号中文别名已修改，请保存 DBC。";
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
            SignalDraft = SelectedSignal is { } selected
                ? DbcSignalEditState.FromSignal(selected.Signal) : null;
            if (SignalDraft is { } draft)
                draft.PropertyChanged += (_, _) =>
                {
                    RaisePropertyChanged(nameof(HasPendingSignalDraft));
                    RefreshCommands();
                };
            RaisePropertyChanged(nameof(SelectedMessageDefinition));
            RaisePropertyChanged(nameof(SelectedSignalDefinition));
        }
        finally { _settingAlias = false; }
    }
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
        var edited = _dialogs.EditMessage(chosen.Message);
        if (edited is null) return;
        try
        {
            _session.Document.UpdateMessage(chosen.Message.Id, edited);
            RefreshDocument(edited.Id, null);
            StatusMessage = "报文参数已修改（尚未保存）";
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
        var edited = _dialogs.EditSignal(chosen.Signal);
        if (edited is null) return;
        try
        {
            _session.Document.UpdateSignal(message.Message.Id, chosen.Name, edited);
            RefreshDocument(message.Message.Id, edited.Name);
            StatusMessage = "信号参数已修改（尚未保存）";
        }
        catch (Exception error) when (error is ProjectDataException or ArgumentException)
        {
            _dialogs.ShowError(error.Message);
        }
    }

    /// <summary>Apply a complete edited signal shape once, after full validation.</summary>
    public void ApplySignalParameters()
    {
        if (!CanEdit || IsBusy || SelectedMessage is not { } message ||
            SelectedSignal is not { } signal || SignalDraft is not { } draft) return;
        try
        {
            static int Integer(string input, string label) =>
                int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                    ? value : throw new FormatException(label + "须是有效整数。");
            var updated = signal.Signal with
            {
                StartBit = Integer(draft.StartBit, "起始位"),
                BitLength = Integer(draft.BitLength, "位长"),
                Intel = draft.Intel, Signed = draft.Signed,
                Factor = DbcSignalEditState.ResolveEditedNumber(
                    draft.Factor, signal.Signal.Factor, "因子"),
                Offset = DbcSignalEditState.ResolveEditedNumber(
                    draft.Offset, signal.Signal.Offset, "偏移"),
                Minimum = DbcSignalEditState.ResolveEditedNumber(
                    draft.Minimum, signal.Signal.Minimum, "最小值"),
                Maximum = DbcSignalEditState.ResolveEditedNumber(
                    draft.Maximum, signal.Signal.Maximum, "最大值"),
                Unit = draft.Unit, Receiver = draft.Receiver,
                Multiplexer = string.IsNullOrWhiteSpace(draft.Multiplexer) ?
                    null : draft.Multiplexer.Trim()
            };
            if (updated == signal.Signal)
            {
                // Different textual representations of the same value (e.g.
                // 08 vs 8 or 0.0800 vs 0.08) are not unsaved semantic changes.
                SignalDraft = DbcSignalEditState.FromSignal(signal.Signal);
                StatusMessage = "参数未发生变化。";
                RefreshCommands();
                return;
            }
            if (updated.Unit.Contains('"') || updated.Unit.Contains('\n') ||
                updated.Unit.Contains('\r'))
                throw new FormatException("单位不得包含引号或换行。");
            updated.Validate(message.Message.Dlc);
            _session.Document.UpdateSignal(message.Message.Id, signal.Name, updated);
            RefreshDocument(message.Message.Id, signal.Name);
            StatusMessage = "已应用信号技术参数，Ctrl+S 保存 DBC。";
        }
        catch (Exception error) when (error is FormatException or ArgumentException or ProjectDataException)
        {
            StatusMessage = "技术参数未应用：" + error.Message;
        }
    }

    public bool ConfirmClose() =>
        !IsBusy && (!IsDirty || _dialogs.ConfirmDiscard("DBC 尚有未保存修改。确定关闭并放弃这些修改吗？"));

    public async Task<bool> SaveForCloseAsync()
    {
        if (IsBusy) return false;
        if (!IsDirty) return true;
        var previousRevision = AssetRevision;
        await SaveAsync();
        return !IsBusy && !IsDirty && AssetRevision > previousRevision;
    }

    private async Task SaveAsync()
    {
        if (!CanSave || !IsDirty || IsBusy) return;
        // Ctrl+S must include parameters visible in the inspector, not discard
        // an un-applied form while saving only CM_ or alias edits.
        if (HasPendingSignalDraft)
        {
            ApplySignalParameters();
            if (HasPendingSignalDraft)
            {
                _dialogs.ShowError("信号技术参数尚未通过校验，请先修正参数再保存。");
                return;
            }
        }
        if (!_session.Document.IsDirty) return;
        IsBusy = true;
        try
        {
            var messageId = SelectedMessage?.Message.Id;
            var signalName = SelectedSignal?.Name;
            _session = await _workspace.SaveDbcEditorAsync(_session);
            RefreshDocument(messageId, signalName);
            StatusMessage = "DBC 已保存、重新读取并更新工程资源修订 " + AssetRevision;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                                        ProjectDataException or InvalidDataException or FormatException or
                                        ArgumentException or NotSupportedException)
        {
            StatusMessage = "保存失败：" + error.Message;
            _dialogs.ShowError("DBC 保存失败。\n" + error.Message +
                "\n如果 DBC 文件已修改而工程清单未成功保存，请检查同目录 .bak 备份。");
        }
        finally { IsBusy = false; RefreshCommands(); }
    }

    private async Task ReloadAsync()
    {
        if (IsBusy) return;
        if (IsDirty && !_dialogs.ConfirmDiscard("重新加载会丢弃未保存的 DBC 参数修改，确定继续吗？"))
            return;
        IsBusy = true;
        try
        {
            var messageId = SelectedMessage?.Message.Id;
            var signalName = SelectedSignal?.Name;
            _session = await _workspace.OpenDbcEditorAsync(_session.AssetId);
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
        if (SelectedMessage is { } message)
        {
            foreach (var signal in message.Message.Signals.OrderBy(s => s.StartBit).ThenBy(s => s.Name))
                Signals.Add(new DbcSignalRow(signal,
                    _session.Document.DisplayNames.SignalLabel(message.Message, signal),
                    _session.Document.DisplayNames.SignalCommentPreview(message.Message.Id, signal.Name)));
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
    }
}
