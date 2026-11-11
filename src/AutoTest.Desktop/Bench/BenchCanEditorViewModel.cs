using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Prism.Commands;
using AutoTest.Projects.Dbc;
using System.Globalization;
using Prism.Mvvm;

namespace AutoTest.Desktop.Bench;

/// <summary>An editable DUT CAN1 message, never an internal KEY_SIM control frame.</summary>
/// <summary>One editable physical DBC value, kept separate from CAN bytes until validated.</summary>
public sealed class BenchDbcSignalInput : BindableBase
{
    private string _value;
    public DbcSignal Definition { get; }
    public string Name => Definition.Name;
    public string Unit => Definition.Unit;
    public string ValueText { get => _value; set => SetProperty(ref _value, value); }
    public BenchDbcSignalInput(DbcSignal definition, double value)
    {
        Definition = definition;
        _value = value.ToString("G17", CultureInfo.InvariantCulture);
    }
}

public sealed class BenchCanMessageItem : BindableBase
{
    private string _name;
    private bool _checked;
    public bool IsChecked { get => _checked; set => SetProperty(ref _checked, value); }
    private string _canIdHex;
    private bool _extended;
    private string _dataHex;
    private string _periodMsText;
    private string? _dbcRevision;
    private string? _dbcMessageName;
    public string? DbcRevision { get => _dbcRevision; set => SetProperty(ref _dbcRevision, value); }
    public string? DbcMessageName { get => _dbcMessageName; set => SetProperty(ref _dbcMessageName, value); }
    public string SourceLabel => DbcRevision is null ? "原始报文" : "工程 DBC";
    public string Id { get; }
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string CanIdHex { get => _canIdHex; set => SetProperty(ref _canIdHex, value); }
    public bool Extended { get => _extended; set => SetProperty(ref _extended, value); }
    public string DataHex { get => _dataHex; set => SetProperty(ref _dataHex, value); }
    public string PeriodMsText { get => _periodMsText; set => SetProperty(ref _periodMsText, value); }

    public BenchCanMessageItem(BenchCanMessage value)
    {
        Id = value.Id;
        _name = value.Name;
        _canIdHex = $"0x{value.ArbitrationId:X}";
        _extended = value.Extended;
        _dataHex = value.DataHex;
        _periodMsText = value.PeriodMs.ToString(CultureInfo.InvariantCulture);
        _dbcRevision = value.DbcRevision;
        _dbcMessageName = value.DbcMessageName;
    }

    public BenchCanMessage Snapshot()
    {
        var raw = CanIdHex.Trim();
        if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) raw = raw[2..];
        if (!uint.TryParse(raw, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id))
            throw new ArgumentException("CAN 报文 ID 必须是十六进制数，如 0x123。");
        if (!int.TryParse(PeriodMsText, NumberStyles.None, CultureInfo.InvariantCulture, out var period))
            throw new ArgumentException("周期必须是整数毫秒。");
        var normalized = new BenchCanMessage(Id, Name.Trim(), id, Extended,
            new string(DataHex.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray()).ToUpperInvariant(),
            period, DbcRevision, DbcMessageName);
        normalized.Validate();
        return normalized;
    }
}

public sealed class BenchCanEditorViewModel : BindableBase
{
    private readonly IBenchWorkbenchPort _port;
    private readonly Action<string> _audit;
    private BenchCanMessageItem? _selected;
    private bool _dirty;
    private bool _working;
    private bool _periodicRunning;
    private CancellationTokenSource? _periodic;
    private string _status = "CAN 报文未加载";
    private string _periodicStatus = "未启动周期发送";
    private long _frameCount;
    private bool _editingSignal;
    private bool _signalInvalid;
    private string _dbcStatus = "尚未关联工程 DBC";
    private string _signalStatus = "选择 DBC 报文后可编辑信号物理值";
    private DbcMessage? _selectedDbcMessage;
    public ObservableCollection<DbcMessage> DbcMessages { get; } = new();
    public ObservableCollection<BenchDbcSignalInput> SignalInputs { get; } = new();
    public DbcMessage? SelectedDbcMessage
    {
        get => _selectedDbcMessage;
        set => SetProperty(ref _selectedDbcMessage, value);
    }
    public string DbcStatus { get => _dbcStatus; private set => SetProperty(ref _dbcStatus, value); }
    public string SignalStatus { get => _signalStatus; private set => SetProperty(ref _signalStatus, value); }
    public bool HasDbcSignals => SignalInputs.Count > 0;
    public bool HasDbc => DbcMessages.Count > 0;
    private string? _editorProjectId;
    private bool DifferentProject => _editorProjectId != _port.ActiveProjectId;
    private const string ProjectChangedMessage =
        "工程已切换：旧工程的 CAN 报文不能直接发到新 ECU。请返回原工程保存草稿，或取消草稿后重新加载。";
    public int CheckedCount => Messages.Count(m => m.IsChecked);

    public BenchCanEditorViewModel(IBenchWorkbenchPort port, Action<string>? audit = null)
    {
        _port = port;
        _audit = audit ?? (_ => { });
        AddCommand = new DelegateCommand(Add);
        AddFromDbcCommand = new DelegateCommand(AddFromDbc);
        UnbindDbcCommand = new DelegateCommand(UnbindDbc);
        RefreshDbcCommand = new DelegateCommand(RefreshDbcCatalog);
        DeleteCommand = new DelegateCommand(Delete);
        SaveCommand = new DelegateCommand(async () => await SaveAsync());
        ReloadCommand = new DelegateCommand(async () => await LoadAsync());
        SendOnceCommand = new DelegateCommand(async () => await SendOnceAsync());
        StartPeriodicCommand = new DelegateCommand(async () => await StartPeriodicAsync());
        StopPeriodicCommand = new DelegateCommand(() => _periodic?.Cancel());
    }

    public ObservableCollection<BenchCanMessageItem> Messages { get; } = new();
    public DelegateCommand AddCommand { get; }
    public DelegateCommand AddFromDbcCommand { get; }
    public DelegateCommand UnbindDbcCommand { get; }
    public DelegateCommand RefreshDbcCommand { get; }
    public DelegateCommand DeleteCommand { get; }
    public DelegateCommand SaveCommand { get; }
    public DelegateCommand ReloadCommand { get; }
    public DelegateCommand SendOnceCommand { get; }
    public DelegateCommand StartPeriodicCommand { get; }
    public DelegateCommand StopPeriodicCommand { get; }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string PeriodicStatus { get => _periodicStatus; private set => SetProperty(ref _periodicStatus, value); }
    public bool Dirty => _dirty;
    public bool CanEdit => !_working && !_periodicRunning;
    public bool IsPeriodicRunning => _periodicRunning;
    public BenchCanMessageItem? SelectedMessage
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value)) RebuildSignalInputs();
        }
    }


    public void RefreshDbcCatalog()
    {
        var selectedId = SelectedDbcMessage?.Id;
        var selectedExt = SelectedDbcMessage?.IsExtended;
        DbcMessages.Clear();
        var db = _port.ActiveDbc;
        var revision = _port.ActiveDbcRevision;
        if (db is not null && revision is not null)
        {
            foreach (var message in db.Messages.OrderBy(m => m.Id))
                DbcMessages.Add(message);
        }
        SelectedDbcMessage = DbcMessages.FirstOrDefault(
            m => m.Id == selectedId && m.IsExtended == selectedExt) ??
            DbcMessages.FirstOrDefault();
        RaisePropertyChanged(nameof(HasDbc));
        DbcStatus = db is null || revision is null
            ? "当前工程没有 DBC：先在 DBC 工具导入并纳入工程，再回来刷新"
            : $"工程 DBC：{db.Name} · {DbcMessages.Count} 条报文 · {revision}";
        RebuildSignalInputs();
    }

    private void AddFromDbc()
    {
        if (!CanEdit || SelectedDbcMessage is not { } message) {
            DbcStatus = "请先选择当前工程 DBC 中的报文；没有 DBC 时可用原始报文。";
            return;
        }
        var revision = _port.ActiveDbcRevision;
        if (_port.ActiveDbc is null || string.IsNullOrWhiteSpace(revision)) {
            DbcStatus = "工程 DBC 未加载或已切换，请刷新 DBC 列表。";
            return;
        }
        var current = _port.ActiveDbc.Messages.FirstOrDefault(
            m => m.Id == message.Id && m.IsExtended == message.IsExtended);
        if (current is null || current != message) {
            DbcStatus = "工程 DBC 已改变，请先刷新列表再添加。";
            return;
        }
        var value = new BenchCanMessage(Guid.NewGuid().ToString("D"),
            message.Name, message.Id, message.IsExtended,
            Convert.ToHexString(new byte[message.Dlc]), 100, revision, message.Name);
        var item = new BenchCanMessageItem(value);
        Track(item);
        Messages.Add(item);
        SelectedMessage = item;
        MarkDirty();
        DbcStatus = $"已从 DBC 添加 {message.Name}；编辑信号后可直接试发，无需先保存";
    }

    private void UnbindDbc()
    {
        if (!CanEdit || SelectedMessage is null) return;
        SelectedMessage.DbcRevision = null;
        SelectedMessage.DbcMessageName = null;
        RebuildSignalInputs();
        MarkDirty();
        SignalStatus = "已转为原始报文；保留当前 ID 和 Data 字节，可直接编辑。";
    }

    private void RebuildSignalInputs()
    {
        SignalInputs.Clear();
        _signalInvalid = false;
        var selected = SelectedMessage;
        if (selected?.DbcRevision is null)
        {
            SignalStatus = "原始报文：直接编辑 ID / Data HEX；如需信号编码，先从 DBC 添加报文";
            RaisePropertyChanged(nameof(HasDbcSignals));
            return;
        }
        try
        {
            var message = ResolvePinnedDbcMessage(selected);
            if (message.Signals.Any(s => s.Multiplexer is not null))
            {
                SignalStatus = "该 DBC 报文包含复用信号；为避免错误编码，请使用原始报文方式。";
                _signalInvalid = true;
                return;
            }
            var data = selected.Snapshot().Payload();
            if (data.Length != message.Dlc)
                throw new InvalidOperationException("Data 长度与 DBC DLC 不同。");
            foreach (var signal in message.Signals)
            {
                var value = DbcSignalCodec.DecodePhysical(signal, data);
                var input = new BenchDbcSignalInput(signal, value);
                input.PropertyChanged += (_, e) => {
                    if (e.PropertyName == nameof(BenchDbcSignalInput.ValueText))
                        EncodeSignalInput(input);
                };
                SignalInputs.Add(input);
            }
            SignalStatus = message.Signals.Length == 0
                ? "该 DBC 报文不含信号；可直接编辑 Data HEX"
                : $"当前编辑：{message.Name} · {message.Dlc} 字节；信号值改变后自动编码 Data HEX";
        }
        catch (Exception ex)
        {
            _signalInvalid = true;
            SignalStatus = "DBC 绑定不可用：" + ex.Message;
        }
        finally { RaisePropertyChanged(nameof(HasDbcSignals)); }
    }

    private DbcMessage ResolvePinnedDbcMessage(BenchCanMessageItem item)
    {
        var db = _port.ActiveDbc;
        if (item.DbcRevision is null || db is null ||
            _port.ActiveDbcRevision != item.DbcRevision)
            throw new InvalidOperationException("工程 DBC Revision 已变化：请重新从 DBC 添加，或解除绑定改用原始报文。");
        var id = item.Snapshot().ArbitrationId;
        var message = db.FindMessage(id, item.Extended);
        if (message.Name != item.DbcMessageName)
            throw new InvalidOperationException("当前 DBC 报文名称已变化，拒绝旧绑定。");
        return message;
    }

    private void EncodeSignalInput(BenchDbcSignalInput input)
    {
        if (SelectedMessage is not { } item || !CanEdit || _editingSignal) return;
        try
        {
            var message = ResolvePinnedDbcMessage(item);
            if (!message.Signals.Contains(input.Definition))
                throw new InvalidOperationException("信号定义已变化。");
            if (!double.TryParse(input.ValueText, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
                throw new ArgumentException("信号值应为有限数字，例如 1、12.5。");
            if (value < input.Definition.Minimum || value > input.Definition.Maximum)
                throw new ArgumentException(
                    $"信号 {input.Name} 超出 DBC 范围 [{input.Definition.Minimum}, {input.Definition.Maximum}] {input.Unit}。");
            var data = item.Snapshot().Payload();
            if (data.Length != message.Dlc) throw new ArgumentException("Data 长度不符合 DBC DLC。");
            DbcSignalCodec.EncodePhysical(input.Definition, value, data);
            _editingSignal = true;
            try { item.DataHex = Convert.ToHexString(data); }
            finally { _editingSignal = false; }
            _signalInvalid = false;
            SignalStatus = $"已将 {input.Name}={input.ValueText} {input.Unit} 编码为报文 Data（未发送）";
        }
        catch (Exception ex)
        {
            _signalInvalid = true;
            SignalStatus = "信号值无效，禁止发送：" + ex.Message;
        }
    }

    private bool CheckDbcBinding(BenchCanMessageItem item)
    {
        if (item.DbcRevision is null) return true;
        try
        {
            var m = ResolvePinnedDbcMessage(item);
            if (item.Snapshot().Payload().Length != m.Dlc)
                throw new InvalidOperationException("Data 字节数与 DBC DLC 不一致。");
            if (ReferenceEquals(item, SelectedMessage) && _signalInvalid)
                throw new InvalidOperationException("有无效的信号输入，需修正后才能发出。");
            return true;
        }
        catch (Exception ex)
        {
            Status = "DBC 报文不可发送：" + ex.Message;
            return false;
        }
    }

    private void Track(BenchCanMessageItem item)
    {
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BenchCanMessageItem.IsChecked))
            {
                RaisePropertyChanged(nameof(CheckedCount));
                return;
            }
            if (e.PropertyName is nameof(BenchCanMessageItem.Name)
                or nameof(BenchCanMessageItem.CanIdHex)
                or nameof(BenchCanMessageItem.Extended)
                or nameof(BenchCanMessageItem.DataHex)
                or nameof(BenchCanMessageItem.PeriodMsText)
                or nameof(BenchCanMessageItem.DbcRevision)
                or nameof(BenchCanMessageItem.DbcMessageName))
            {
                MarkDirty();
                if (ReferenceEquals(item, SelectedMessage) &&
                    e.PropertyName is nameof(BenchCanMessageItem.DataHex)
                        or nameof(BenchCanMessageItem.CanIdHex)
                        or nameof(BenchCanMessageItem.Extended)
                        or nameof(BenchCanMessageItem.DbcRevision))
                    if (!_editingSignal) RebuildSignalInputs();
            }
        };
    }

    private void MarkDirty()
    {
        if (_dirty) return;
        _dirty = true;
        RaisePropertyChanged(nameof(Dirty));
        Status = "已修改（可直接试发，保存后才写入工程）";
    }
    private void ClearDirty()
    {
        _dirty = false;
        RaisePropertyChanged(nameof(Dirty));
    }
    private void Add()
    {
        if (!CanEdit) return;
        var name = "新报文";
        for (var n = 2; Messages.Any(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); n++)
            name = $"新报文 {n}";
        var item = new BenchCanMessageItem(new BenchCanMessage(
            Guid.NewGuid().ToString("D"), name, 0x123, false, "00", 100));
        Track(item);
        Messages.Add(item);
        SelectedMessage = item;
        RaisePropertyChanged(nameof(CheckedCount));
        MarkDirty();
    }
    private void Delete()
    {
        if (!CanEdit || SelectedMessage is not { } selected) return;
        Messages.Remove(selected);
        SelectedMessage = Messages.FirstOrDefault();
        RaisePropertyChanged(nameof(CheckedCount));
        MarkDirty();
    }
    public async Task DiscardDraftAndReloadAsync()
    {
        if (!CanEdit) return;
        // Caller must confirm losing unsaved CAN row edits.
        SelectedMessage = null;
        Messages.Clear();
        ClearDirty();
        _editorProjectId = null;
        RaisePropertyChanged(nameof(CheckedCount));
        await LoadAsync();
    }

    public async Task LoadAsync()
    {
        if (!CanEdit) return;
        RefreshDbcCatalog();
        if (_dirty && DifferentProject)
        {
            Status = ProjectChangedMessage;
            return;
        }
        if (_dirty)
        {
            Status = "尚有未保存的 CAN 配置；请先保存，避免覆盖编辑内容。";
            return;
        }
        if (DifferentProject)
        {
            SelectedMessage = null;
            Messages.Clear();
            _editorProjectId = _port.ActiveProjectId;
            RaisePropertyChanged(nameof(CheckedCount));
        }
        _working = true;
        RaisePropertyChanged(nameof(CanEdit));
        try
        {
            var editingProjectId = _port.ActiveProjectId;
            var saved = await _port.LoadCanMessagesAsync(CancellationToken.None);
            if (editingProjectId != _port.ActiveProjectId)
                throw new InvalidOperationException("加载期间当前工程已切换。");
            _editorProjectId = editingProjectId;
            BenchCanMessage.ValidateAll(saved);
            Messages.Clear();
            foreach (var value in saved)
            {
                var item = new BenchCanMessageItem(value);
                Track(item);
                Messages.Add(item);
            }
            SelectedMessage = Messages.FirstOrDefault();
            RaisePropertyChanged(nameof(CheckedCount));
            ClearDirty();
            Status = $"当前工程已加载 {Messages.Count} 条 CAN 发送配置";
        }
        catch (Exception ex) { Status = "CAN 配置加载失败：" + ex.Message; }
        finally { _working = false; RaisePropertyChanged(nameof(CanEdit)); }
    }

    public async Task SaveAsync()
    {
        if (!CanEdit) return;
        if (DifferentProject)
        {
            Status = ProjectChangedMessage;
            return;
        }
        _working = true;
        RaisePropertyChanged(nameof(CanEdit));
        try
        {
            var items = Messages.Select(m => m.Snapshot()).ToArray();
            BenchCanMessage.ValidateAll(items);
            await _port.SaveCanMessagesAsync(items, CancellationToken.None);
            ClearDirty();
            Status = $"CAN 发送列表已保存到工程（{items.Length} 条）";
            _audit(Status);
        }
        catch (Exception ex) { Status = "保存失败，编辑内容保留：" + ex.Message; }
        finally { _working = false; RaisePropertyChanged(nameof(CanEdit)); }
    }

    private bool TryGetSavedSnapshot(out BenchCanMessage? snapshot)
    {
        snapshot = null;
        if (DifferentProject)
        {
            Status = ProjectChangedMessage;
            return false;
        }
        if (!CanEdit || SelectedMessage is null)
        {
            Status = "先选择一条 CAN 报文；周期发送中不可修改配置。";
            return false;
        }
        if (!CheckDbcBinding(SelectedMessage)) return false;
        try { snapshot = SelectedMessage.Snapshot(); return true; }
        catch (Exception ex) { Status = "CAN 报文配置无效：" + ex.Message; return false; }
    }

    public async Task SendOnceAsync()
    {
        if (!TryGetSavedSnapshot(out var message) || message is null) return;
        _working = true;
        RaisePropertyChanged(nameof(CanEdit));
        try
        {
            var result = await _port.SendCanOnceAsync(message, CancellationToken.None);
            Status = result.Accepted
                ? $"主控已确认 CAN1 单次发送 ID=0x{message.ArbitrationId:X}；不代表 ECU 已识别"
                : $"CAN1 单发拒绝/失败：{result.ErrorCode ?? "UNKNOWN"} {result.ErrorMessage}";
            _audit(Status + $" | {result.RawRequest} | {result.RawResponse} | {result.TraceReference}");
        }
        catch (Exception ex) { Status = "CAN1 单发异常，实际发送结果 UNKNOWN：" + ex.Message; _audit(Status); }
        finally { _working = false; RaisePropertyChanged(nameof(CanEdit)); }
    }

    public async Task StartPeriodicAsync()
    {
        if (!CanEdit) return;
        if (DifferentProject)
        {
            Status = ProjectChangedMessage;
            return;
        }
        var checkedRows = Messages.Where(x => x.IsChecked).ToArray();
        if (checkedRows.Length == 0)
        {
            Status = "请先勾选要周期发送的 CAN 报文。";
            return;
        }
        BenchCanMessage[] selected;
        try
        {
            selected = checkedRows.Select(x => x.Snapshot()).ToArray();
            BenchCanMessage.ValidateAll(selected);
        }
        catch (Exception ex)
        {
            Status = "勾选的报文存在无效配置：" + ex.Message;
            return;
        }
        // One bad or stale DBC binding blocks the entire set before physical TX.
        if (checkedRows.Any(x => !CheckDbcBinding(x))) return;
        // Freeze all checked frames, periods and project identity for this run.
        _periodic = new CancellationTokenSource();
        _periodicRunning = true;
        _frameCount = 0;
        RaisePropertyChanged(nameof(IsPeriodicRunning));
        RaisePropertyChanged(nameof(CanEdit));
        PeriodicStatus = $"已勾选 {selected.Length} 条；准备真实 CAN1 周期发送（Windows 主机定时，非硬实时）";
        try
        {
            var progress = new Progress<BenchCanPeriodicProgress>(p =>
            {
                _frameCount = p.AcknowledgedFrames;
                PeriodicStatus = p.Description;
            });
            _audit($"CAN1 批量发送：{selected.Length} 条，" +
                string.Join(", ", selected.Select(m => $"0x{m.ArbitrationId:X}/{m.PeriodMs}ms")));
            var result = await _port.RunCanPeriodicBatchAsync(selected, progress, _periodic.Token);
            Status = result.Completed ? "周期发送达到安全上限并停止" :
                result.Cancelled ? "用户已停止 CAN1 周期发送" : "CAN1 周期任务停止或被拒绝";
            PeriodicStatus = result.Detail + $" · 主控 ACK {_frameCount} 帧";
            _audit(Status + " | " + PeriodicStatus);
        }
        catch (OperationCanceledException)
        {
            Status = "CAN1 周期发送取消，最后一帧结果可能未知";
            _audit(Status);
        }
        catch (Exception ex)
        {
            Status = "CAN1 周期发送异常，已停止：" + ex.Message;
            _audit(Status);
        }
        finally
        {
            _periodic.Dispose();
            _periodic = null;
            _periodicRunning = false;
            RaisePropertyChanged(nameof(IsPeriodicRunning));
            RaisePropertyChanged(nameof(CanEdit));
        }
    }
}
