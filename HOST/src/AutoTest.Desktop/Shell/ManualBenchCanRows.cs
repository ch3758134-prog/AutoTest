using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using AutoTest.Contracts;
using AutoTest.Devices.Can;
using AutoTest.Projects.Dbc;
using AutoTest.Projects.Project;
using AutoTest.Desktop.Projects;
using Prism.Commands;
using Prism.Mvvm;

namespace AutoTest.Desktop.Shell;

/// <summary>Real ECU CAN transmission list, isolated from KEY CAN and TEST execution.</summary>
public sealed partial class ManualBenchViewModel
{
    public sealed class BenchCanRow : BindableBase
    {
        private string _name;
        private string _idHex = "701", _dataHex = "00 00 00 00 00 00 00 00", _periodText = "100", _result = "待发送";
        private bool _included = true, _extended, _periodic, _useDbc;
        private bool _lockedToAuthorizedBatch;
        private bool _batchInProgress;
        private DbcChoice? _dbcChoice;
        private int _sent;
        private bool _synchronizingCanSignals;
        private string _signalEditError = "";
        private bool _explicitRawCan;
        private ManualBenchCanDraftStore.DbcPin? _staleDbcPin;
        public ManualBenchCanDraftStore.DbcPin? BoundDbcPin =>
            UseDbc && DbcMessage is { } dbc
                ? new(dbc.AssetId, dbc.Revision, dbc.Sha256, dbc.Message.Id)
                : _staleDbcPin;
        public void MarkUnresolvedDbc(ManualBenchCanDraftStore.DbcPin pin)
        {
            _staleDbcPin = pin;
            Notify();
        }
        public event Action? Edited;

        public BenchCanRow(string name) : this(name, new ObservableCollection<DbcChoice>()) { }
        public BenchCanRow(string name, ObservableCollection<DbcChoice> dbcChoices)
        {
            _name = name;
            DbcChoices = dbcChoices;
        }
        // Share the live project DBC list, rather than duplicate DBC data
        // inside each editable row or reopen an independent selector window.
        public ObservableCollection<DbcChoice> DbcChoices { get; }
        public string Name { get => _name; set { if (SetProperty(ref _name, value ?? "")) Notify(); } }
        public string IdHex { get => _idHex; set { if (SetProperty(ref _idHex, value ?? "")) Notify(); } }
        public string DataHex
        {
            get => _dataHex;
            set
            {
                if (!SetProperty(ref _dataHex, value ?? "")) return;
                _signalEditError = "";
                SynchronizeCanSignals();
                Notify();
            }
        }
        public string PeriodText { get => _periodText; set { if (SetProperty(ref _periodText, value ?? "")) Notify(); } }
        public bool Included { get => _included; set { if (SetProperty(ref _included, value)) Notify(); } }
        public bool CanToggleIncluded => !_batchInProgress || _lockedToAuthorizedBatch;
        public bool CanEditFields => !_batchInProgress;
        public void SetBatchMembership(bool running, bool authorized)
        {
            _batchInProgress = running;
            _lockedToAuthorizedBatch = authorized;
            RaisePropertyChanged(nameof(CanToggleIncluded));
            RaisePropertyChanged(nameof(CanEditFields));
        }
        public bool Extended { get => _extended; set { if (SetProperty(ref _extended, value)) Notify(); } }
        public bool IsPeriodic { get => _periodic; set { if (SetProperty(ref _periodic, value)) Notify(); } }
        public bool UseDbc
        {
            get => _useDbc;
            set
            {
                if (!SetProperty(ref _useDbc, value)) return;
                if (value && _dbcChoice is not null)
                    AdoptDbcIdentity(_dbcChoice);
                SynchronizeCanSignals();
                Notify();
            }
        }
        public bool IsRaw => !UseDbc;
        public DbcChoice? DbcMessage
        {
            get => _dbcChoice;
            set
            {
                if (!SetProperty(ref _dbcChoice, value)) return;
                foreach (var signal in SignalValues) signal.PropertyChanged -= OnSignalChanged;
                SignalValues.Clear();
                _signalEditError = "";
                if (value is not null)
                {
                    _staleDbcPin = null;
                    var previousId = IdHex.Trim();
                    if (previousId.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                        previousId = previousId[2..];
                    var sameFrame = uint.TryParse(previousId, NumberStyles.HexNumber,
                            CultureInfo.InvariantCulture, out var oldId) &&
                        oldId == value.Message.ArbitrationId &&
                        CleanDataHex().Length == value.Message.Dlc * 2 &&
                        CleanDataHex().All(Uri.IsHexDigit);
                    // The DBC owns the IDE bit. Preserve valid DATA for the
                    // same numeric ID even for a low-numbered extended frame.
                    _explicitRawCan = false;
                    _useDbc = true;
                    RaisePropertyChanged(nameof(UseDbc));
                    AdoptDbcIdentity(value);
                    if (!sameFrame)
                        DataHex = FormatHex(new byte[value.Message.Dlc]);

                    var overlapping = new HashSet<int>();
                    var editingSafe = !value.Message.Signals.Any(signal =>
                        !string.IsNullOrEmpty(signal.Multiplexer) ||
                        DbcSignalCodec.BitPositions(signal).Any(bit => !overlapping.Add(bit)));
                    foreach (var signal in value.Message.Signals)
                    {
                        var alias = value.DisplayNames.SignalAlias(value.Message.Id, signal.Name);
                        var labels = value.ValueLabels is not null &&
                            value.ValueLabels.TryGetValue(signal.Name, out var table) ? table : null;
                        var entry = new DbcSignalValue(signal,
                            string.IsNullOrWhiteSpace(alias) ? signal.Name : alias,
                            value.DisplayNames.SignalDescription(value.Message.Id, signal.Name),
                            labels, summary: value.DisplayNames.SignalSummary(value.Message.Id, signal.Name),
                            isEditable: editingSafe);
                        entry.PropertyChanged += OnSignalChanged;
                        SignalValues.Add(entry);
                    }
                    SynchronizeCanSignals();
                }
                else
                {
                    _useDbc = false;
                    RaisePropertyChanged(nameof(UseDbc));
                }
                Notify();
            }
        }

        public void ClearDbcAssistance()
        {
            _explicitRawCan = true;
            _staleDbcPin = null;
            DbcMessage = null;
            Notify();
        }

        public void AttachUniqueMatchingDbc()
        {
            if (_explicitRawCan || DbcMessage is not null) return;
            var rawId = IdHex.Trim();
            if (rawId.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                rawId = rawId[2..];
            if (!uint.TryParse(rawId, NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out var id)) return;
            var matches = DbcChoices.Where(x => x.Message.ArbitrationId == id).ToArray();
            // Never guess when multiple DBC definitions share an arbitration ID.
            if (matches.Length != 1) return;
            var data = CleanDataHex();
            if (data.Length == matches[0].Message.Dlc * 2 &&
                data.All(Uri.IsHexDigit))
                DbcMessage = matches[0];
        }

        private void AdoptDbcIdentity(DbcChoice choice)
        {
            IdHex = "0x" + choice.Message.ArbitrationId.ToString("X", CultureInfo.InvariantCulture);
            Extended = choice.Message.IsExtended;
        }

        private void SynchronizeCanSignals()
        {
            if (_synchronizingCanSignals || !UseDbc || DbcMessage is null) return;
            byte[]? data = null;
            try
            {
                var bytes = ReadDataBytes();
                if (bytes.Length == DbcMessage.Message.Dlc) data = bytes;
            }
            catch (ArgumentException) { /* DATA can be incomplete while typing. */ }

            _synchronizingCanSignals = true;
            try
            {
                foreach (var field in SignalValues)
                {
                    var decoded = "";
                    if (data is not null)
                    {
                        try
                        {
                            decoded = DbcSignalCodec.DecodePhysical(field.Signal, data)
                                .ToString("G12", CultureInfo.InvariantCulture);
                        }
                        catch (Exception error) when (error is ArgumentException or
                            IndexOutOfRangeException or ProjectDataException) { }
                    }
                    field.Value = decoded;
                }
            }
            finally { _synchronizingCanSignals = false; }
        }

        private void OnSignalChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(DbcSignalValue.Value) ||
                _synchronizingCanSignals || !UseDbc ||
                sender is not DbcSignalValue signal || !signal.IsEditable) return;
            try
            {
                if (!double.TryParse(signal.Value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var physical) ||
                    !double.IsFinite(physical))
                    throw new ArgumentException("信号必须是有效物理数值。");
                if (physical < signal.Signal.Minimum || physical > signal.Signal.Maximum)
                    throw new ArgumentException("信号值超出 DBC 范围。");
                var data = ReadDataBytes();
                if (data.Length != DbcMessage?.Message.Dlc)
                    throw new ArgumentException("请先填写完整 CAN DATA。");
                DbcSignalCodec.EncodePhysical(signal.Signal, physical, data);
                _signalEditError = "";
                DataHex = FormatHex(data);
            }
            catch (Exception error) when (error is ArgumentException or FormatException or
                OverflowException or ProjectDataException)
            {
                _signalEditError = signal.DisplayName + "：" + error.Message;
                Notify();
            }
        }
        public ObservableCollection<DbcSignalValue> SignalValues { get; } = new();
        public string Summary => UseDbc && DbcMessage is not null
            ? DbcMessage.DisplayNames.MessageLabel(DbcMessage.Message) + " · 0x" + DbcMessage.Message.ArbitrationId.ToString("X")
            : "0x" + IdHex + (Extended ? " 扩展" : " 标准");
        public string ModeLabel => IsPeriodic ? "周期 " + PeriodText + "ms" : "单发";
        // The send table is the only persistent raw-data preview. DBC signal
        // edits are encoded by the same production codec used when sending.
        public string PreviewId
        {
            get
            {
                if (UseDbc)
                {
                    var m = DbcMessage?.Message;
                    return m is null ? "选择 DBC" :
                        "0x" + m.ArbitrationId.ToString(m.IsExtended ? "X8" : "X3");
                }
                var value = IdHex.Trim().StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? IdHex.Trim()[2..] : IdHex.Trim();
                if (!uint.TryParse(value, NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture, out var id) ||
                    id > 0x1FFFFFFFu)
                    return "ID 无效";
                var extended = Extended || id > 0x7FFu;
                return "0x" + id.ToString(extended ? "X8" : "X3");
            }
        }
        public string PreviewFrameFormat
        {
            get
            {
                if (UseDbc && DbcMessage is { } bound)
                    return bound.Message.IsExtended ? "扩展帧 · DBC" : "标准帧 · DBC";
                var text = IdHex.Trim();
                if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    text = text[2..];
                return uint.TryParse(text, NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out var id) && (Extended || id > 0x7FFu)
                    ? "扩展帧 · 自动" : "标准帧 · 默认";
            }
        }
        public string PreviewDlc
        {
            get
            {
                if (UseDbc) return DbcMessage?.Message.Dlc.ToString(CultureInfo.InvariantCulture) ?? "—";
                var hex = CleanDataHex();
                return hex.Length <= 16 && hex.Length % 2 == 0 &&
                    hex.All(Uri.IsHexDigit)
                    ? (hex.Length / 2).ToString(CultureInfo.InvariantCulture) : "—";
            }
        }
        private string CleanDataHex() =>
            new(DataHex.Where(c => !char.IsWhiteSpace(c) && c is not '-' and not ',').ToArray());

        public string PreviewData
        {
            get
            {
                try { return FormatHex(ReadDataBytes()); }
                catch (ArgumentException) { return "HEX 尚未输入完整"; }
            }
        }
        private byte[] ReadDataBytes()
        {
            var hex = CleanDataHex();
            if (hex.Length > 16 || hex.Length % 2 != 0 || !hex.All(Uri.IsHexDigit))
                throw new ArgumentException("CAN DATA 必须是最多 8 个完整十六进制字节。");
            return Convert.FromHexString(hex);
        }
        public string PreviewError
        {
            get
            {
                if (_signalEditError.Length > 0) return _signalEditError;
                try
                {
                    _ = BuildFrame();
                    return "";
                }
                catch (Exception e) when (e is ArgumentException or FormatException or
                                            InvalidOperationException or ProjectDataException)
                {
                    return e.Message;
                }
            }
        }
        public string PreviewFull => PreviewId + " · DLC " + PreviewDlc + " · " +
            PreviewData + (string.IsNullOrWhiteSpace(PreviewError) ? "" :
                " · " + PreviewError);
        private static string FormatHex(byte[] data) =>
            string.Join(" ", data.Select(x => x.ToString("X2", CultureInfo.InvariantCulture)));
        public string DataSummary => "DATA " + DataHex.Trim() +
            (UseDbc && DbcMessage is not null
                ? " · DBC " + DbcMessage.DisplayNames.MessageLabel(DbcMessage.Message) : "");
        public string Result { get => _result; set => SetProperty(ref _result, value); }
        public int Sent { get => _sent; set => SetProperty(ref _sent, value); }
        private void Notify()
        {
            RaisePropertyChanged(nameof(Summary));
            RaisePropertyChanged(nameof(DataSummary));
            RaisePropertyChanged(nameof(ModeLabel));
            RaisePropertyChanged(nameof(PreviewId));
            RaisePropertyChanged(nameof(PreviewDlc));
            RaisePropertyChanged(nameof(PreviewFrameFormat));
            RaisePropertyChanged(nameof(PreviewData));
            RaisePropertyChanged(nameof(PreviewError));
            RaisePropertyChanged(nameof(PreviewFull));
            RaisePropertyChanged(nameof(IsRaw));
            Edited?.Invoke();
        }
        public CanFrame BuildFrame()
        {
            if (_staleDbcPin is not null)
                throw new InvalidOperationException(
                    "已保存的 DBC 修订/报文不再匹配。请显式重新绑定最新 DBC，或清除辅助后核对原始 DATA。");
            if (_signalEditError.Length > 0)
                throw new ArgumentException(_signalEditError);
            var bytes = ReadDataBytes();
            if (UseDbc)
            {
                var dbc = DbcMessage ?? throw new InvalidOperationException("请先选择 DBC 报文。");
                if (bytes.Length != dbc.Message.Dlc)
                    throw new ArgumentException("CAN DATA 字节数必须与 DBC DLC 一致。");
                return CanFrame.Create(BusRole.DutCan, dbc.Message.IsExtended,
                    dbc.Message.ArbitrationId, bytes);
            }
            var rawId = IdHex.Trim();
            if (rawId.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) rawId = rawId[2..];
            var canId = uint.Parse(rawId, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return CanFrame.Create(BusRole.DutCan, Extended || canId > 0x7FFu, canId, bytes);
        }
    }

    private sealed record PreparedCanRow(BenchCanRow Row, CanFrame Frame, int PeriodMs);

    private CancellationTokenSource? _canBatchStop;
    private Task? _canBatchTask;
    private bool _canBatchRunning;
    private int _newCanRowNumber;
    private BenchCanRow? _selectedCanRow;
    private string? _canDraftRoot;
    private bool _canRowsLoading;
    private long _canEditGeneration, _canSavedGeneration;
    private readonly SemaphoreSlim _canDraftWriteGate = new(1, 1);
    public bool CanRowsDirty => _canDraftRoot is not null &&
        _canEditGeneration != _canSavedGeneration;
    private void MarkCanRowsDirty()
    {
        if (_canRowsLoading || _canDraftRoot is null) return;
        _canEditGeneration++;
        RaisePropertyChanged(nameof(CanRowsDirty));
    }
    public ObservableCollection<BenchCanRow> CanSendRows { get; } = new();
    public BenchCanRow? SelectedCanRow
    {
        get => _selectedCanRow;
        set
        {
            if (SetProperty(ref _selectedCanRow, value))
            {
                RaisePropertyChanged(nameof(HasSelectedCanRow));
                RaiseCommands();
            }
        }
    }
    public bool HasSelectedCanRow => SelectedCanRow is not null;
    public bool IsCanBatchRunning
    {
        get => _canBatchRunning;
        private set { if (SetProperty(ref _canBatchRunning, value)) RaiseCommands(); }
    }
    public bool CanEditCanRows => !IsBusy && !IsCanBatchRunning;
    public bool CanStartCanRows => !IsBusy && !IsCanBatchRunning &&
        CanSendRows.Any(x => x.Included);
    public bool CanStopCanRows => IsCanBatchRunning;

    public DelegateCommand AddCanRowCommand { get; private set; } = null!;
    public DelegateCommand CopyCanRowCommand { get; private set; } = null!;
    public DelegateCommand DeleteCanRowCommand { get; private set; } = null!;
    public DelegateCommand MoveCanRowUpCommand { get; private set; } = null!;
    public DelegateCommand MoveCanRowDownCommand { get; private set; } = null!;
    public DelegateCommand StartCanRowsCommand { get; private set; } = null!;
    public DelegateCommand StopCanRowsCommand { get; private set; } = null!;
    public DelegateCommand<BenchCanRow> SendCanRowOnceCommand { get; private set; } = null!;

    private void InitializeCanRows()
    {
        AddCanRowCommand = new DelegateCommand(AddCanRow, () => CanEditCanRows);
        CopyCanRowCommand = new DelegateCommand(CopyCanRow, () => CanEditCanRows && HasSelectedCanRow);
        DeleteCanRowCommand = new DelegateCommand(DeleteCanRow, () => CanEditCanRows && HasSelectedCanRow);
        MoveCanRowUpCommand = new DelegateCommand(() => MoveCanRow(-1), () => CanEditCanRows && HasSelectedCanRow);
        MoveCanRowDownCommand = new DelegateCommand(() => MoveCanRow(1), () => CanEditCanRows && HasSelectedCanRow);
        StartCanRowsCommand = new DelegateCommand(async () => await StartCanRowsAsync(), () => CanStartCanRows);
        StopCanRowsCommand = new DelegateCommand(async () => await StopCanRowsAsync(), () => CanStopCanRows);
        SendCanRowOnceCommand = new DelegateCommand<BenchCanRow>(
            async row => await StartCanRowsAsync(row),
            row => row is not null && !IsBusy && !IsCanBatchRunning);
        AddCanRow();
    }

    private void NotifyCanRows()
    {
        RaisePropertyChanged(nameof(CanEditCanRows));
        RaisePropertyChanged(nameof(CanStartCanRows));
        RaisePropertyChanged(nameof(CanStopCanRows));
        AddCanRowCommand?.RaiseCanExecuteChanged();
        CopyCanRowCommand?.RaiseCanExecuteChanged();
        DeleteCanRowCommand?.RaiseCanExecuteChanged();
        MoveCanRowUpCommand?.RaiseCanExecuteChanged();
        MoveCanRowDownCommand?.RaiseCanExecuteChanged();
        StartCanRowsCommand?.RaiseCanExecuteChanged();
        StopCanRowsCommand?.RaiseCanExecuteChanged();
        SendCanRowOnceCommand?.RaiseCanExecuteChanged();
    }

    private void OnCanRowEdited()
    {
        if (_disposed) return;
        MarkCanRowsDirty();
        NotifyCanRows();
    }

    private void AddCanRow()
    {
        var row = new BenchCanRow("报文 " + (++_newCanRowNumber).ToString(CultureInfo.InvariantCulture), DbcMessages);
        row.Edited += OnCanRowEdited;
        CanSendRows.Add(row);
        SelectedCanRow = row;
        MarkCanRowsDirty();
        NotifyCanRows();
    }

    private void CopyCanRow()
    {
        if (SelectedCanRow is not { } source) return;
        var row = new BenchCanRow(source.Name + " 副本", DbcMessages)
        {
            IdHex = source.IdHex, Extended = source.Extended,
            PeriodText = source.PeriodText, IsPeriodic = source.IsPeriodic,
            Included = source.Included, DbcMessage = source.UseDbc ? source.DbcMessage : null
        };
        row.DataHex = source.DataHex; // Preserve all bytes, including reserved bits.
        if (!source.UseDbc) row.UseDbc = false;
        row.Edited += OnCanRowEdited;
        CanSendRows.Insert(CanSendRows.IndexOf(source) + 1, row);
        SelectedCanRow = row;
        MarkCanRowsDirty();
        NotifyCanRows();
    }

    private void DeleteCanRow()
    {
        if (SelectedCanRow is not { } selected) return;
        var index = CanSendRows.IndexOf(selected);
        selected.Edited -= OnCanRowEdited;
        CanSendRows.Remove(selected);
        SelectedCanRow = CanSendRows.Count == 0 ? null :
            CanSendRows[Math.Min(index, CanSendRows.Count - 1)];
        MarkCanRowsDirty();
        NotifyCanRows();
    }

    private void MoveCanRow(int delta)
    {
        if (SelectedCanRow is not { } selected) return;
        var current = CanSendRows.IndexOf(selected);
        var target = current + delta;
        if (target < 0 || target >= CanSendRows.Count) return;
        CanSendRows.Move(current, target);
        SelectedCanRow = selected;
        MarkCanRowsDirty();
        NotifyCanRows();
    }

    /// <summary>
    /// Save only the editable CAN configuration in the project, never an active
    /// physical USB grant or a runnable task. Validates the saved JSON envelope
    /// and writes atomically; incomplete HEX remains a visible draft on reopening.
    /// </summary>
    public async Task SaveCanRowsForCloseAsync()
    {
        if (_canDraftRoot is not { } root || !CanRowsDirty) return;
        var version = _canEditGeneration;
        var document = new ManualBenchCanDraftStore.Document(1, CanSendRows.Select(row =>
            new ManualBenchCanDraftStore.Row(row.Name, row.IdHex, row.DataHex,
                row.Extended, row.IsPeriodic, row.PeriodText, row.Included,
                row.BoundDbcPin)).ToList());
        await _canDraftWriteGate.WaitAsync();
        try
        {
            // An older queued save may not overwrite a newer saved snapshot.
            if (version <= _canSavedGeneration) return;
            await ManualBenchCanDraftStore.SaveAsync(root, document);
            _canSavedGeneration = version;
            RaisePropertyChanged(nameof(CanRowsDirty));
        }
        finally { _canDraftWriteGate.Release(); }
    }

    private async Task RestoreCanRowsForProjectAsync()
    {
        var root = _projects.Current?.RootDirectory;
        if (string.Equals(_canDraftRoot, root, StringComparison.OrdinalIgnoreCase))
            return;
        // Keep the previous engineering project's edits before switching scope.
        await SaveCanRowsForCloseAsync();
        if (!string.Equals(root, _projects.Current?.RootDirectory,
                StringComparison.OrdinalIgnoreCase)) return;

        ManualBenchCanDraftStore.Document? document;
        try
        {
            document = root is null ? null : await ManualBenchCanDraftStore.LoadAsync(root);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                                       System.Text.Json.JsonException or InvalidDataException)
        {
            // No valid rows => no accidental physical transmit from a broken draft.
            foreach (var row in CanSendRows) row.Edited -= OnCanRowEdited;
            CanSendRows.Clear();
            SelectedCanRow = null;
            _canDraftRoot = null;
            Status = "CAN 工作配置损坏，已禁止发送并保留原文件：" + error.Message;
            NotifyCanRows();
            return;
        }

        _canRowsLoading = true;
        try
        {
            foreach (var row in CanSendRows) row.Edited -= OnCanRowEdited;
            CanSendRows.Clear();
            _newCanRowNumber = 0;
            _canDraftRoot = root;
            _canEditGeneration = _canSavedGeneration = 0;
            if (document is { } loaded)
            {
                foreach (var entry in loaded.Rows)
                {
                    var row = new BenchCanRow(entry.Name, DbcMessages)
                    {
                        IdHex = entry.IdHex, DataHex = entry.DataHex,
                        Extended = entry.Extended, IsPeriodic = entry.IsPeriodic,
                        PeriodText = entry.PeriodText, Included = entry.Included
                    };
                    if (entry.Dbc is { } pin)
                    {
                        var match = DbcMessages.FirstOrDefault(choice =>
                            choice.AssetId == pin.AssetId && choice.Revision == pin.Revision &&
                            string.Equals(choice.Sha256, pin.Sha256,
                                StringComparison.OrdinalIgnoreCase) &&
                            choice.Message.Id == pin.MessageId);
                        if (match is null) row.MarkUnresolvedDbc(pin);
                        else row.DbcMessage = match;
                    }
                    row.Edited += OnCanRowEdited;
                    CanSendRows.Add(row);
                }
            }
            else AddCanRow(); // First use: a harmless unsent editable default.
            SelectedCanRow = CanSendRows.FirstOrDefault();
            RaisePropertyChanged(nameof(CanRowsDirty));
            NotifyCanRows();
        }
        finally { _canRowsLoading = false; }
    }

    private PreparedCanRow[] PrepareCanRows(BenchCanRow? single = null)
    {
        var chosen = single is null ? CanSendRows.Where(row => row.Included).ToArray() :
            CanSendRows.Contains(single) ? new[] { single } : Array.Empty<BenchCanRow>();
        if (chosen.Length is < 1 or > 32)
            throw new InvalidOperationException("请选择 1～32 条 ECU CAN 报文。");
        var entries = chosen.Select(row =>
        {
            // Host-timed ECU CAN USB 0x40 only. Firmware 0x50 scheduling is KEY CAN.
            // Every ECU CAN cycle uses the real physical fence, per-write ACK,
            // cancellation, bounded session and maximum authorized writes.
            var frame = row.BuildFrame();
            int period = 0;
            if (row.IsPeriodic && single is null && (!int.TryParse(row.PeriodText, NumberStyles.None,
                    CultureInfo.InvariantCulture, out period) || period is < 50 or > 60000))
                throw new InvalidOperationException(row.Name + " 的发送周期必须是 50～60000 ms。");
            return new PreparedCanRow(row, frame, period);
        }).ToArray();
        if (entries.Where(item => item.PeriodMs != 0).Sum(item => 1000d / item.PeriodMs) > 25d)
            throw new InvalidOperationException("周期发送总负载不得超过 25 帧/秒，请调大周期。");
        return entries;
    }

    private static string BatchFingerprint(IEnumerable<PreparedCanRow> plan)
    {
        var text = string.Join("\n", plan.Select(item =>
        {
            var raw = CanFrameCodec.ToUsbPayload(item.Frame,
                StationCanWiring.Current);
            return Convert.ToHexString(raw) + ":" + item.PeriodMs;
        }));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    private async Task VerifyCanRowDbcFreshAsync(BenchCanRow row)
    {
        if (!row.UseDbc) return;
        var choice = row.DbcMessage ?? throw new InvalidOperationException(row.Name + " 没有选择 DBC 报文。");
        var project = _projects.Current ?? throw new InvalidOperationException("没有打开工程。");
        var asset = project.Definition.Assets.SingleOrDefault(a =>
            a.Id.Value == choice.AssetId && a.Kind == ProjectWorkspaceService.DbcKind)
            ?? throw new InvalidDataException(row.Name + " 的 DBC 资源不在当前工程。");
        if (asset.Revision != choice.Revision ||
            !string.Equals(asset.Sha256, choice.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(row.Name + " 的 DBC 已修改，请重新加载资源。");
        var loaded = await _projects.OpenDbcEditorAsync(asset.Id);
        if (!loaded.Document.Model.Messages.Any(msg => msg.Id == choice.Message.Id &&
                msg.Name == choice.Message.Name))
            throw new InvalidDataException(row.Name + " 对应的 DBC 报文不存在。");
    }

    private async Task StartCanRowsAsync(BenchCanRow? single = null) => await Execute(
        single is null ? "ECU CAN 多报文" : "ECU CAN 单帧", async () =>
    {
        if (!_hardware.IsConnected)
            throw new IOException("USB 未连接，请先点击顶部“连接”。");
        if (IsCanBatchRunning)
            throw new InvalidOperationException("ECU CAN 正在发送，请先停止当前任务。");
        var prepared = PrepareCanRows(single);
        foreach (var row in prepared) await VerifyCanRowDbcFreshAsync(row.Row);
        var fingerprint = BatchFingerprint(prepared);
        await _hardware.AuthorizeManualCanBatchAsync(fingerprint,
            "AUTHORIZE BENCH CAN " + fingerprint, prepared.Select(item => item.Frame).ToArray(),
            maxWrites: 8000);
        var stop = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        _canBatchStop = stop;
        var authorized = prepared.Select(x => x.Row).ToHashSet();
        foreach (var row in CanSendRows) row.SetBatchMembership(true, authorized.Contains(row));
        foreach (var row in prepared) { row.Row.Sent = 0; row.Row.Result = "运行中"; }
        IsCanBatchRunning = true;
        _canBatchTask = RunCanRowsAsync(prepared, fingerprint, stop);
    });

    private async Task RunCanRowsAsync(PreparedCanRow[] plan, string fingerprint,
        CancellationTokenSource stop)
    {
        try
        {
            await Task.WhenAll(plan.Select(async entry =>
            {
                // A monotonic deadline avoids adding the WinUSB ACK duration
                // to every requested period. Missed deadlines are skipped, never
                // compensated by a burst of unapproved catch-up CAN writes.
                var scheduled = System.Diagnostics.Stopwatch.GetTimestamp();
                var periodTicks = entry.PeriodMs == 0 ? 0L : Math.Max(1L,
                    (long)Math.Round(entry.PeriodMs *
                        (double)System.Diagnostics.Stopwatch.Frequency / 1000d));
                long skipped = 0;
                while (!stop.IsCancellationRequested)
                {
                    if (!entry.Row.Included)
                    {
                        entry.Row.Result = "已暂停 · 本轮不发送";
                        await Task.Delay(90, stop.Token);
                        scheduled = System.Diagnostics.Stopwatch.GetTimestamp();
                        continue;
                    }
                    try
                    {
                        await _hardware.SendManualBatchCanAsync(entry.Frame, fingerprint, stop.Token);
                    }
                    catch
                    {
                        // A failure on any row must stop every other periodic sender.
                        stop.Cancel();
                        throw;
                    }
                    entry.Row.Sent++;
                    if (entry.Row.Sent == 1 || entry.Row.Sent % 10 == 0)
                    {
                        entry.Row.Result = "已发送 " + entry.Row.Sent +
                            " 次 · USB ACK" + (skipped > 0 ? " · 跳过逾期周期 " + skipped : "");
                        if (entry.Row.Sent == 1)
                            Append(new BenchLogEntry(DateTime.Now.ToString("HH:mm:ss.fff"),
                                "ECU CAN", "TX ACK", "0x" + entry.Frame.Id.ToString("X"),
                                Convert.ToHexString(entry.Frame.Data.AsSpan()),
                                entry.Row.Name + "：主控 ACK 并非 DUT 应答"));
                    }
                    if (entry.PeriodMs == 0) break;
                    var now = System.Diagnostics.Stopwatch.GetTimestamp();
                    var deadline = PeriodicCanDeadline.Advance(scheduled, now, periodTicks);
                    scheduled = deadline.NextTicks;
                    skipped += deadline.SkippedPeriods;
                    if (deadline.SkippedPeriods > 0)
                        entry.Row.Result = "已发送 " + entry.Row.Sent +
                            " 次 · USB ACK · 跳过逾期周期 " + skipped;
                    var untilDue = System.Diagnostics.Stopwatch.GetElapsedTime(now, scheduled);
                    if (untilDue > TimeSpan.Zero)
                        await Task.Delay(untilDue, stop.Token);
                }
            }));
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Status = "ECU CAN 多报文异常终止：" + ex.Message;
            Append(new BenchLogEntry(DateTime.Now.ToString("HH:mm:ss.fff"),
                "ECU CAN", "ERROR", "", "", Status));
            stop.Cancel();
        }
        finally
        {
            try { await _hardware.RevokeManualCanBatchAsync(); }
            catch (Exception ex)
            {
                Append(new BenchLogEntry(DateTime.Now.ToString("HH:mm:ss.fff"),
                    "USB", "ERROR", "", "", "撤销 CAN 授权失败：" + ex.Message));
            }
            foreach (var entry in plan)
                entry.Row.Result = entry.Row.Sent > 0
                    ? "已停止 · " + entry.Row.Sent + " 次 USB ACK" : "未完成";
            _canBatchStop = null;
            foreach (var row in CanSendRows) row.SetBatchMembership(false, false);
            IsCanBatchRunning = false;
            stop.Dispose();
        }
    }

    /// <summary>Window closing waits for all ECU CAN writers to stop and for the fence to revoke.</summary>
    public Task StopCanBatchForCloseAsync() => StopCanRowsAsync();

    private async Task StopCanRowsAsync()
    {
        var stop = _canBatchStop;
        if (stop is null) return;
        stop.Cancel();
        if (_canBatchTask is not null) await _canBatchTask;
        Status = "ECU CAN 多报文任务已停止。";
    }
}
