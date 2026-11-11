using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Prism.Commands;
using Prism.Mvvm;

namespace AutoTest.Desktop.Bench;

/// <summary>An editable DUT CAN1 message, never an internal KEY_SIM control frame.</summary>
public sealed class BenchCanMessageItem : BindableBase
{
    private string _name;
    private bool _checked;
    public bool IsChecked { get => _checked; set => SetProperty(ref _checked, value); }
    private string _canIdHex;
    private bool _extended;
    private string _dataHex;
    private string _periodMsText;
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
            period);
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
        DeleteCommand = new DelegateCommand(Delete);
        SaveCommand = new DelegateCommand(async () => await SaveAsync());
        ReloadCommand = new DelegateCommand(async () => await LoadAsync());
        SendOnceCommand = new DelegateCommand(async () => await SendOnceAsync());
        StartPeriodicCommand = new DelegateCommand(async () => await StartPeriodicAsync());
        StopPeriodicCommand = new DelegateCommand(() => _periodic?.Cancel());
    }

    public ObservableCollection<BenchCanMessageItem> Messages { get; } = new();
    public DelegateCommand AddCommand { get; }
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
    public BenchCanMessageItem? SelectedMessage { get => _selected; set => SetProperty(ref _selected, value); }

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
                or nameof(BenchCanMessageItem.PeriodMsText))
                MarkDirty();
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
    public async Task LoadAsync()
    {
        if (!CanEdit) return;
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
