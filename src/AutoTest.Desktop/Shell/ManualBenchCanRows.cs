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
using AutoTest.Desktop.Projects;
using Prism.Commands;
using Prism.Mvvm;

namespace AutoTest.Desktop.Shell;

/// <summary>Real manual CAN1 transmission list, kept apart from KEY_SIM/CAN0 and TEST execution.</summary>
public sealed partial class ManualBenchViewModel
{
    public sealed class BenchCanRow : BindableBase
    {
        private string _name;
        private string _idHex = "701", _dataHex = "", _periodText = "100", _result = "待发送";
        private bool _included = true, _extended, _periodic, _useDbc;
        private DbcChoice? _dbcChoice;
        private int _sent;
        public event Action? Edited;

        public BenchCanRow(string name) => _name = name;
        public string Name { get => _name; set { if (SetProperty(ref _name, value ?? "")) Notify(); } }
        public string IdHex { get => _idHex; set { if (SetProperty(ref _idHex, value ?? "")) Notify(); } }
        public string DataHex { get => _dataHex; set { if (SetProperty(ref _dataHex, value ?? "")) Notify(); } }
        public string PeriodText { get => _periodText; set { if (SetProperty(ref _periodText, value ?? "")) Notify(); } }
        public bool Included { get => _included; set { if (SetProperty(ref _included, value)) Notify(); } }
        public bool Extended { get => _extended; set { if (SetProperty(ref _extended, value)) Notify(); } }
        public bool IsPeriodic { get => _periodic; set { if (SetProperty(ref _periodic, value)) Notify(); } }
        public bool UseDbc { get => _useDbc; set { if (SetProperty(ref _useDbc, value)) Notify(); } }
        public bool IsRaw => !UseDbc;
        public DbcChoice? DbcMessage
        {
            get => _dbcChoice;
            set
            {
                if (!SetProperty(ref _dbcChoice, value)) return;
                foreach (var signal in SignalValues) signal.PropertyChanged -= OnSignalChanged;
                SignalValues.Clear();
                if (value is not null)
                    foreach (var signal in value.Message.Signals)
                    {
                        var entry = new DbcSignalValue(signal);
                        entry.PropertyChanged += OnSignalChanged;
                        SignalValues.Add(entry);
                    }
                Notify();
            }
        }
        private void OnSignalChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DbcSignalValue.Value)) Notify();
        }
        public ObservableCollection<DbcSignalValue> SignalValues { get; } = new();
        public string Summary => UseDbc && DbcMessage is not null
            ? DbcMessage.Message.Name + " · 0x" + DbcMessage.Message.ArbitrationId.ToString("X")
            : "0x" + IdHex + (Extended ? " 扩展" : " 标准");
        public string ModeLabel => IsPeriodic ? PeriodText + "ms" : "单次";
        public string Result { get => _result; set => SetProperty(ref _result, value); }
        public int Sent { get => _sent; set => SetProperty(ref _sent, value); }
        private void Notify()
        {
            RaisePropertyChanged(nameof(Summary));
            RaisePropertyChanged(nameof(ModeLabel));
            RaisePropertyChanged(nameof(IsRaw));
            Edited?.Invoke();
        }
        public CanFrame BuildFrame()
        {
            if (UseDbc)
            {
                var dbc = DbcMessage ?? throw new InvalidOperationException("请先选择 DBC 报文。");
                var spec = string.Join(";", SignalValues.Select(x => x.Signal.Name + "=" + x.Value.Trim()));
                var bytes = DbcCanMessageEncoder.Encode(dbc.Message, spec);
                return CanFrame.Create(BusRole.DutCan, dbc.Message.IsExtended,
                    dbc.Message.ArbitrationId, bytes);
            }
            var rawId = IdHex.Trim();
            if (rawId.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) rawId = rawId[2..];
            var canId = uint.Parse(rawId, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var rawHex = new string(DataHex.Where(c => !char.IsWhiteSpace(c) && c is not '-' and not ',').ToArray());
            return CanFrame.Create(BusRole.DutCan, Extended, canId,
                rawHex.Length == 0 ? Array.Empty<byte>() : Convert.FromHexString(rawHex));
        }
    }

    private sealed record PreparedCanRow(BenchCanRow Row, CanFrame Frame, int PeriodMs);

    private CancellationTokenSource? _canBatchStop;
    private Task? _canBatchTask;
    private bool _canBatchRunning;
    private int _newCanRowNumber;
    private BenchCanRow? _selectedCanRow;
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
    public bool CanStartCanRows => CanSendHardware && CanSendRows.Any(x => x.Included);
    public bool CanStopCanRows => IsCanBatchRunning;

    public DelegateCommand AddCanRowCommand { get; private set; } = null!;
    public DelegateCommand CopyCanRowCommand { get; private set; } = null!;
    public DelegateCommand DeleteCanRowCommand { get; private set; } = null!;
    public DelegateCommand StartCanRowsCommand { get; private set; } = null!;
    public DelegateCommand StopCanRowsCommand { get; private set; } = null!;

    private void InitializeCanRows()
    {
        AddCanRowCommand = new DelegateCommand(AddCanRow, () => CanEditCanRows);
        CopyCanRowCommand = new DelegateCommand(CopyCanRow, () => CanEditCanRows && HasSelectedCanRow);
        DeleteCanRowCommand = new DelegateCommand(DeleteCanRow, () => CanEditCanRows && HasSelectedCanRow);
        StartCanRowsCommand = new DelegateCommand(async () => await StartCanRowsAsync(), () => CanStartCanRows);
        StopCanRowsCommand = new DelegateCommand(async () => await StopCanRowsAsync(), () => CanStopCanRows);
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
        StartCanRowsCommand?.RaiseCanExecuteChanged();
        StopCanRowsCommand?.RaiseCanExecuteChanged();
    }

    private void OnCanRowEdited()
    {
        if (!_disposed) NotifyCanRows();
    }

    private void AddCanRow()
    {
        var row = new BenchCanRow("报文 " + (++_newCanRowNumber).ToString(CultureInfo.InvariantCulture));
        row.Edited += OnCanRowEdited;
        CanSendRows.Add(row);
        SelectedCanRow = row;
        NotifyCanRows();
    }

    private void CopyCanRow()
    {
        if (SelectedCanRow is not { } source) return;
        var row = new BenchCanRow(source.Name + " 副本")
        {
            IdHex = source.IdHex, DataHex = source.DataHex, Extended = source.Extended,
            PeriodText = source.PeriodText, IsPeriodic = source.IsPeriodic,
            Included = source.Included, UseDbc = source.UseDbc, DbcMessage = source.DbcMessage
        };
        for (var i = 0; i < row.SignalValues.Count && i < source.SignalValues.Count; i++)
            row.SignalValues[i].Value = source.SignalValues[i].Value;
        row.Edited += OnCanRowEdited;
        CanSendRows.Insert(CanSendRows.IndexOf(source) + 1, row);
        SelectedCanRow = row;
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
        NotifyCanRows();
    }

    private PreparedCanRow[] PrepareCanRows()
    {
        var chosen = CanSendRows.Where(row => row.Included).ToArray();
        if (chosen.Length is < 1 or > 32)
            throw new InvalidOperationException("请选择 1～32 条 CAN1 报文。");
        var entries = chosen.Select(row =>
        {
            var frame = row.BuildFrame();
            int period = 0;
            if (row.IsPeriodic && (!int.TryParse(row.PeriodText, NumberStyles.None,
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
                new BusRoleMap(default, DutPort: 1, KeypadPort: 0));
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

    private async Task StartCanRowsAsync() => await Execute("CAN1 多报文", async () =>
    {
        if (!_hardware.IsConnected || IsCanBatchRunning)
            throw new InvalidOperationException("需要空闲的真实主控 USB 会话。");
        var prepared = PrepareCanRows();
        foreach (var row in prepared) await VerifyCanRowDbcFreshAsync(row.Row);
        var fingerprint = BatchFingerprint(prepared);
        var detail = string.Join("\n", prepared.Select(item => item.Row.Name + "  ID=0x" +
            item.Frame.Id.ToString("X") + "  " + (item.PeriodMs > 0 ? item.PeriodMs + "ms 周期" : "单次") +
            "  DATA=" + Convert.ToHexString(item.Frame.Data.AsSpan())));
        var approved = MessageBox.Show(
            "即将向真实 ECU CAN1 总线发送：\n\n" + detail +
            "\n\n运行中帧内容不可修改；点击“停止全部”可停止。" +
            "\n单次报文发送一次，周期报文最多运行 5 分钟。\n\n确定执行？",
            "确认真实 CAN1 发送", MessageBoxButton.YesNo, MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (approved != MessageBoxResult.Yes)
        {
            Status = "已取消 CAN1 真实发送。";
            return;
        }
        await _hardware.AuthorizeManualCanBatchAsync(fingerprint,
            "AUTHORIZE BENCH CAN " + fingerprint, prepared.Select(item => item.Frame).ToArray(),
            maxWrites: 3000);
        var stop = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        _canBatchStop = stop;
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
                while (!stop.IsCancellationRequested)
                {
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
                        entry.Row.Result = "已发送 " + entry.Row.Sent + " 次 · USB ACK";
                        if (entry.Row.Sent == 1)
                            Append(new BenchLogEntry(DateTime.Now.ToString("HH:mm:ss.fff"),
                                "CAN1 DUT", "TX ACK", "0x" + entry.Frame.Id.ToString("X"),
                                Convert.ToHexString(entry.Frame.Data.AsSpan()),
                                entry.Row.Name + "：主控 ACK 并非 DUT 应答"));
                    }
                    if (entry.PeriodMs == 0) break;
                    await Task.Delay(entry.PeriodMs, stop.Token);
                }
            }));
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Status = "CAN1 多报文异常终止：" + ex.Message;
            Append(new BenchLogEntry(DateTime.Now.ToString("HH:mm:ss.fff"),
                "CAN1 DUT", "ERROR", "", "", Status));
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
            IsCanBatchRunning = false;
            stop.Dispose();
        }
    }

    private async Task StopCanRowsAsync()
    {
        var stop = _canBatchStop;
        if (stop is null) return;
        stop.Cancel();
        if (_canBatchTask is not null) await _canBatchTask;
        Status = "CAN1 多报文任务已停止。";
    }
}
