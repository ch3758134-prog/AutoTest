using System.Collections.ObjectModel;
using System.IO;
using AutoTest.Desktop.Projects;
using AutoTest.Projects.Dbc;
using AutoTest.Projects.Project;
using Prism.Commands;
using Prism.Mvvm;

namespace AutoTest.Desktop.Dbc;

public sealed record DbcMessageRow(DbcMessage Message)
{
    public string HexId => "0x" + Message.ArbitrationId.ToString("X");
    public string FrameType => Message.IsExtended ? "扩展帧" : "标准帧";
    public string DecimalId => Message.Id.ToString();
    public string Name => Message.Name;
    public string Dlc => Message.Dlc.ToString();
    public string Transmitter => Message.Transmitter;
    public string Caption => HexId + " " + FrameType + " · " + Name + " (" + Message.Signals.Length + " 信号)";
}
public sealed record DbcSignalRow(DbcSignal Signal)
{
    public string Name => Signal.Name;
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
    private string _status = "就绪";

    public DbcWorkbenchViewModel(
        ProjectWorkspaceService workspace, DbcEditorDialogs dialogs, DbcEditorSession opened)
    {
        _workspace = workspace;
        _dialogs = dialogs;
        _session = opened;
        EditMessageCommand = new DelegateCommand(EditMessage, () => CanEdit && SelectedMessage is not null && !IsBusy);
        EditSignalCommand = new DelegateCommand(EditSignal, () => CanEdit && SelectedSignal is not null && !IsBusy);
        SaveCommand = new DelegateCommand(async () => await SaveAsync(), () => CanEdit && IsDirty && !IsBusy);
        ReloadCommand = new DelegateCommand(async () => await ReloadAsync(), () => !IsBusy);
        RefreshDocument(null, null);
    }

    public ObservableCollection<DbcMessageRow> Messages { get; } = new();
    public ObservableCollection<DbcSignalRow> Signals { get; } = new();

    public string AssetName => _session.AssetName;
    public string FilePath => _session.FileReceipt.Path;
    public int AssetRevision => _session.AssetRevision;
    public string MessageCount => Messages.Count.ToString();
    public string SignalCount => SelectedMessage?.Message.Signals.Length.ToString() ?? "0";
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
        ? "可编辑 · 保留原文注释/属性 · CAN ID 不可改"
        : _session.Document.CanEdit
        ? "可编辑 · 只修改已支持的 DBC 字段"
        : "只读 · 存在不支持安全回写的 DBC 语法";
    public string UnsupportedDescription => _session.Document.IsSourcePreserving
        ? "原始注释、属性、枚举值和其他记录会在保存时保留；本模式禁止改 CAN ID、信号名称及结构。"
        : _session.Document.CanEdit ? ""
        : "保留原始 DBC 内容，禁止覆盖保存。原因：" +
          string.Join("、", _session.Document.UnsupportedDirectives.Take(12));
    public bool CanEdit => _session.Document.CanEdit;
    public bool IsDirty => _session.Document.IsDirty;
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

    public DbcMessageRow? SelectedMessage
    {
        get => _selectedMessage;
        set
        {
            if (!SetProperty(ref _selectedMessage, value)) return;
            var signalName = _selectedSignal?.Name;
            RefreshSignals(signalName);
            RaisePropertyChanged(nameof(SignalCount));
            RaisePropertyChanged(nameof(SelectedValueDescriptions));
            RefreshCommands();
        }
    }
    public DbcSignalRow? SelectedSignal
    {
        get => _selectedSignal;
        set
        {
            if (SetProperty(ref _selectedSignal, value))
            {
                RaisePropertyChanged(nameof(SelectedValueDescriptions));
                RefreshCommands();
            }
        }
    }

    public DelegateCommand EditMessageCommand { get; }
    public DelegateCommand EditSignalCommand { get; }
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

    public bool ConfirmClose() =>
        !IsBusy && (!IsDirty || _dialogs.ConfirmDiscard("DBC 尚有未保存修改。确定关闭并放弃这些修改吗？"));

    private async Task SaveAsync()
    {
        if (!CanEdit || !IsDirty || IsBusy) return;
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
        Messages.Clear();
        foreach (var message in _session.Document.Model.Messages.OrderBy(m => m.Id))
            Messages.Add(new DbcMessageRow(message));
        // Keep an explicit selection when possible. Newly opened DBC selects the first message.
        SelectedMessage = Messages.FirstOrDefault(x => x.Message.Id == messageId) ?? Messages.FirstOrDefault();
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

    private void RefreshSignals(string? signalName)
    {
        Signals.Clear();
        if (SelectedMessage is { } message)
        {
            foreach (var signal in message.Message.Signals.OrderBy(s => s.StartBit).ThenBy(s => s.Name))
                Signals.Add(new DbcSignalRow(signal));
        }
        SelectedSignal = Signals.FirstOrDefault(x => x.Name == signalName) ?? Signals.FirstOrDefault();
        RaisePropertyChanged(nameof(SignalCount));
    }

    private void RefreshCommands()
    {
        RaisePropertyChanged(nameof(IsDirty));
        EditMessageCommand?.RaiseCanExecuteChanged();
        EditSignalCommand?.RaiseCanExecuteChanged();
        SaveCommand?.RaiseCanExecuteChanged();
        ReloadCommand?.RaiseCanExecuteChanged();
    }
}
