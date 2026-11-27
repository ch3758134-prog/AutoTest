using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AutoTest.FlashLab.Configuration;
using AutoTest.FlashLab.Programming;
using Microsoft.Win32;

namespace AutoTest.FlashLab.Shell;

/// <summary>
/// A human-editable native configuration. Autosaves only valid drafts and
/// preserves a failed edit on screen rather than silently closing or discarding
/// it. Does not own a WinUSB handle or physical write consent.
/// </summary>
public partial class NativeFlashProfileWindow : Window
{
    private readonly string _root;
    private const string VerifiedMethodTarget = "HYDEPB";
    private readonly DispatcherTimer _idleSave = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private NativeFlashProfileStore.Snapshot? _saved;
    private NativeFlashProfileDocument _draft;
    private bool _initializing = true;
    private bool _closing;
    private bool _closeApproved;
    private bool _importBusy;
    private bool _previewBusy;

    private void SetFieldsEditable(bool enabled)
    {
        enabled &= !_previewBusy && !_importBusy && !_initializing;
        foreach (var input in new TextBox[]
        {
            AppTxBox, AppRxBox, BootTxBox, BootRxBox,
            VersionIdBox, DiagnosticsIdBox, BootFeatureBox, AppFeatureBox,
            StartBox, EndBox
        }) input.IsEnabled = enabled;
        BaudBox.IsEnabled = enabled;
        FirmwareImportButton.IsEnabled = enabled;
    }

    public NativeFlashProfileWindow(string projectRoot)
    {
        _root = Path.GetFullPath(projectRoot);
        // Verified target is programming-method provenance, not barcode model.
        _draft = NativeFlashProfileDocument.CreateHydEpbReferenceDraft();
        InitializeComponent();
        Loaded += (_, _) => FlashWindowSizing.Fit(this);
        Closing += OnClosing;
        Loaded += async (_, _) => await LoadAsync();
        Closed += (_, _) => _idleSave.Stop();
        _idleSave.Tick += async (_, _) =>
        {
            _idleSave.Stop();
            try { await SaveAsync(); }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                // A WPF async timer must never lose the unsaved FLASH draft
                // or crash the configuration tool due to an unusual IO failure.
                StatusLabel.Text = "原生烧录参数自动保存失败：" + error.Message;
                System.Diagnostics.Trace.TraceWarning("Native FLASH autosave failed: " + error);
            }
        };
    }

    private async Task LoadAsync()
    {
        _initializing = true;
        SetFieldsEditable(false);
        try
        {
            _saved = await NativeFlashProfileStore.LoadAsync(_root);
            if (_saved is not null)
                _draft = _saved.Profile;
            if (_draft.ProgrammingMethodId != NativeFlashProfileDocument.VerifiedMethod ||
                _draft.EcuModelCode != VerifiedMethodTarget)
                throw new InvalidDataException(
                    "保存的 FLASH 工艺不属于已验证 HydEPB 方法。拒绝自动替换，需人工核对。");
            AppTxBox.Text = Format(_draft.AppRequestCanId);
            AppRxBox.Text = Format(_draft.AppResponseCanId);
            BootTxBox.Text = Format(_draft.BootRequestCanId);
            BootRxBox.Text = Format(_draft.BootResponseCanId);
            BaudBox.SelectedIndex = _draft.EcuCanKbps == 250 ? 0 : 1;
            StartBox.Text = Format(_draft.FlashStart);
            EndBox.Text = Format(_draft.FlashEndInclusive);
            VersionIdBox.Text = FormatOptional(_draft.ReferenceVersionCanId);
            DiagnosticsIdBox.Text = FormatOptional(_draft.ReferenceDiagnosticsCanId);
            BootFeatureBox.Text = _draft.ReferenceBootFeature ?? "";
            AppFeatureBox.Text = _draft.ReferenceAppFeature ?? "";
            FirmwareLabel.Text = _draft.FirmwareDisplayName ??
                (_draft.FirmwareRelativePath is null ? "尚未选择 HEX" : "已选择 HEX 固件");
            StatusLabel.Text = _draft.FirmwareRelativePath is null
                ? "请选择 HEX 固件；烧录参数会自动保存。"
                : "烧录参数已就绪，可校验烧录计划。";
        }
        catch (Exception error) when (error is IOException or FormatException or InvalidDataException
                                     or UnauthorizedAccessException or NotSupportedException or JsonException)
        {
            StatusLabel.Text = "读取原生配置失败：" + error.Message;
            // The editor remains open, but subsequent save must not overwrite a bad file.
            _saved = null;
            _loadFailure = true;
        }
        finally
        {
            _initializing = false;
            SetFieldsEditable(!_loadFailure);
        }
    }

    private bool _loadFailure;

    private static string Format(uint value) => value == 0
        ? "" : "0x" + value.ToString("X", CultureInfo.InvariantCulture);
    private static string FormatOptional(uint? value) => value is null
        ? "" : Format(value.Value);
    private static uint? ParseOptionalHex(TextBox input) =>
        string.IsNullOrWhiteSpace(input.Text) ? null : ParseHex(input);

    private static uint ParseHex(TextBox input) =>
        string.IsNullOrWhiteSpace(input.Text) ? 0u :
        CanToolXmlProfileReader.ParseHexUInt(input.Text, input.Name);

    private NativeFlashProfileDocument ReadFields()
    {
        var chosenBaud = (BaudBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        if (!int.TryParse(chosenBaud, NumberStyles.None, CultureInfo.InvariantCulture,
                out var baud))
            throw new FormatException("请选择有效 ECU CAN 波特率。");
        return _draft with
        {
            AppRequestCanId = ParseHex(AppTxBox),
            AppResponseCanId = ParseHex(AppRxBox),
            BootRequestCanId = ParseHex(BootTxBox),
            BootResponseCanId = ParseHex(BootRxBox),
            EcuCanKbps = baud,
            FlashStart = ParseHex(StartBox),
            FlashEndInclusive = ParseHex(EndBox),
            ReferenceVersionCanId = ParseOptionalHex(VersionIdBox),
            ReferenceDiagnosticsCanId = ParseOptionalHex(DiagnosticsIdBox),
            ReferenceBootFeature = string.IsNullOrWhiteSpace(BootFeatureBox.Text)
                ? null : BootFeatureBox.Text.Trim(),
            ReferenceAppFeature = string.IsNullOrWhiteSpace(AppFeatureBox.Text)
                ? null : AppFeatureBox.Text.Trim()
        };
    }

    private void EditorChanged(object sender, TextChangedEventArgs e) => QueueAutoSave();
    private void EditorSelectionChanged(object sender, SelectionChangedEventArgs e) => QueueAutoSave();

    private void QueueAutoSave()
    {
        if (_initializing || _loadFailure || _closing) return;
        JobDetailsLabel.Text = "参数已修改，请重新检查烧录计划。";
        _idleSave.Stop();
        _idleSave.Start();
    }

    private async Task<bool> SaveAsync()
    {
        if (_initializing || _loadFailure || _importBusy) return false;
        // Wait for any in-flight auto-save. Do not mistake a concurrent save
        // for a failure when the user closes the window.
        await _saveGate.WaitAsync();
        _idleSave.Stop();
        SetFieldsEditable(false);
        try
        {
            var next = ReadFields();
            next.ValidateDraft();
            if (next.EcuModelCode != VerifiedMethodTarget ||
                next.ProgrammingMethodId != NativeFlashProfileDocument.VerifiedMethod)
                throw new InvalidDataException("烧录方法目标已改变，禁止作为已验证实体 FLASH 保存。");
            if (_saved is not null &&
                Equals(next with { Revision = _saved.Profile.Revision }, _saved.Profile))
            {
                StatusLabel.Text = "已保存";
                return true;
            }
            _saved = await NativeFlashProfileStore.SaveDraftAsync(_root,
                next, _saved?.ProfileSha256);
            _draft = _saved.Profile;
            StatusLabel.Text = "已自动保存";
            return true;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or
                                      FormatException or ArgumentException or NotSupportedException or
                                      UnauthorizedAccessException or JsonException)
        {
            StatusLabel.Text = "配置未保存：" + error.Message;
            return false;
        }
        finally
        {
            SetFieldsEditable(!_loadFailure && !_importBusy);
            _saveGate.Release();
        }
    }

    private async void ImportFirmware_Click(object sender, RoutedEventArgs e)
    {
        if (_initializing || _loadFailure || _importBusy || _previewBusy || _closing) return;
        var dialog = new OpenFileDialog
        {
            Title = "选择当前 ECU 的 HEX 固件",
            Filter = "Intel HEX 固件|*.hex;*.h86"
        };
        if (dialog.ShowDialog(this) != true) return;
        _importBusy = true;
        SetFieldsEditable(false);
        try
        {
            var next = ReadFields();
            var imported = await NativeFlashProfileStore.ImportFirmwareAsync(
                _root, dialog.FileName, next.BinBaseAddress);
            _draft = next with
            {
                FirmwareRelativePath = imported.RelativePath,
                FirmwareSha256 = imported.Sha256,
                FirmwareDisplayName = Path.GetFileName(dialog.FileName)
            };
            FirmwareLabel.Text = Path.GetFileName(dialog.FileName);
            JobDetailsLabel.Text = "HEX 已更新，可重新检查烧录范围。";
            _importBusy = false;
            if (!await SaveAsync())
                StatusLabel.Text = "固件已经导入但配置尚未持久保存；请处理错误后再退出。";
        }
        catch (Exception error) when (error is IOException or FormatException or InvalidDataException
                                      or NotSupportedException or UnauthorizedAccessException or JsonException)
        { StatusLabel.Text = "固件导入失败：" + error.Message; }
        finally
        {
            _importBusy = false;
            SetFieldsEditable(!_loadFailure);
        }
    }

    private static string FormatFrozenJob(CanToolFlashJob job)
    {
        var text =
            "Flash 范围：0x" + job.FlashStart.ToString("X8") + "～0x" +
                job.FlashEndInclusive.ToString("X8") + "\n" +
            "计划下载：" + job.Blocks.Length + " 块 × " +
                job.AlignmentBlockLength + " 字节（合计 " +
                job.PlannedBytes.ToString("N0") + " 字节）";
        if (job.LegacyTailSignature)
        {
            var address = job.FlashEndInclusive - 3;
            var baseAddress = address / (uint)job.AlignmentBlockLength *
                (uint)job.AlignmentBlockLength;
            text += "\n\n注意：已验证的 OEM 工艺自动写入签名 12 34 56 78" +
                "\n写入地址：0x" + address.ToString("X8") +
                "～0x" + job.FlashEndInclusive.ToString("X8") +
                "\n所属对齐块：0x" + baseAddress.ToString("X8") +
                "；当 HEX 未提供该块时将补入下载计划。" +
                "\n若 HEX 在此地址有原字节，实际下载内容会被覆盖。";
        }
        else text += "\n此任务未启用 HydEPB OEM 末端签名。";
        return text;
    }

    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (_previewBusy || _importBusy || _initializing || _closing) return;
        _previewBusy = true;
        SetFieldsEditable(false);
        try
        {
            // Saving, loading and freezing are one UI transaction. New edits
            // cannot silently invalidate the displayed frozen firmware plan.
            if (!await SaveAsync()) return;
            var source = await NativeFlashProfileStore.LoadAsync(_root)
                ?? throw new InvalidDataException("请先保存烧录参数和 HEX。");
            if (_saved is null || !string.Equals(source.ProfileSha256,
                    _saved.ProfileSha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("烧录配置在冻结前被外部修改；请重新打开配置进行核对。");
            var job = await Task.Run(() =>
                NativeFlashJobBuilder.Freeze(source.Profile, _root));
            JobDetailsLabel.Text = FormatFrozenJob(job);
            StatusLabel.Text = $"校验通过：{job.Blocks.Length} 个块，" +
                $"计划 {job.PlannedBytes:N0} 字节；尚未向 ECU 发送指令。";
        }
        catch (Exception error) when (error is IOException or FormatException or InvalidDataException
                                      or NotSupportedException or UnauthorizedAccessException or
                                      JsonException or ArgumentException)
        {
            JobDetailsLabel.Text = "无法生成烧录计划：" + error.Message;
            StatusLabel.Text = "原生烧录预检拒绝：" + error.Message;
        }
        finally
        {
            _previewBusy = false;
            SetFieldsEditable(!_loadFailure);
        }
    }

    /// <summary>
    /// Called by AutoTest's parent shell BEFORE it starts asynchronous USB
    /// teardown. An owned editor must finish its autosave and close in a
    /// controlled, separate operation; WPF owner shutdown cannot safely
    /// re-enter an async Closing handler on this child.
    /// </summary>
    public async Task<bool> PrepareForParentShutdownAsync()
    {
        if (_closing || _importBusy || _previewBusy || _initializing) return false;
        if (!IsLoaded) return true;
        _closing = true;
        _idleSave.Stop();
        try
        {
            if (!await SaveAsync())
            {
                StatusLabel.Text = "退出已阻止：" + StatusLabel.Text +
                    "。请修正烧录配置并重新关闭。";
                Activate();
                return false;
            }
            _closeApproved = true;
            Close();
            return true;
        }
        finally { _closing = false; }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closeApproved) return;
        e.Cancel = true;
        if (_importBusy || _previewBusy || _initializing)
        {
            StatusLabel.Text = "正在保存或检查烧录参数，请先完成当前操作。";
            return;
        }
        if (_closing) return;
        _closing = true;
        _idleSave.Stop();
        try
        {
            if (!await SaveAsync())
            {
                // A malformed draft or damaged on-disk profile must not trap
                // an engineer in a window that can never close. This warning
                // is displayed ONLY for exceptional invalid/failed saves.
                var choice = MessageBox.Show(this,
                    "当前原生烧录草稿没有成功保存。\n" +
                    StatusLabel.Text + "\n\n" +
                    "选择“是”将放弃本次未保存的编辑并关闭窗口；磁盘上原文件和备份不受影响。\n" +
                    "选择“否”返回编辑继续修正。",
                    "存在未保存的无效配置", MessageBoxButton.YesNo,
                    MessageBoxImage.Warning, MessageBoxResult.No);
                if (choice != MessageBoxResult.Yes) return;
            }
            // WPF forbids calling Close() while the first Closing event is
            // still active. Its initial event is canceled; schedule a distinct
            // dispatcher turn instead of re-entering Window.Close during async
            // closing continuation (which previously raised UI errors).
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!IsLoaded) return;
                _closeApproved = true;
                Close();
            }), DispatcherPriority.Background);
        }
        finally { _closing = false; }
    }
}
