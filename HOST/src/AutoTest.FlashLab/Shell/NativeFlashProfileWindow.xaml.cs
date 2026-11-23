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
    private readonly string _expectedModel;
    private readonly DispatcherTimer _idleSave = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private NativeFlashProfileStore.Snapshot? _saved;
    private NativeFlashProfileDocument _draft;
    private bool _initializing = true;
    private bool _saving;
    private bool _closing;
    private bool _closeApproved;

    public NativeFlashProfileWindow(string projectRoot, string? modelCode)
    {
        _root = Path.GetFullPath(projectRoot);
        _expectedModel = modelCode ?? "";
        _draft = NativeFlashProfileDocument.CreateDraft(_expectedModel);
        InitializeComponent();
        Closing += OnClosing;
        Loaded += async (_, _) => await LoadAsync();
        Closed += (_, _) => _idleSave.Stop();
        _idleSave.Tick += async (_, _) =>
        {
            _idleSave.Stop();
            await SaveAsync();
        };
    }

    private async Task LoadAsync()
    {
        _initializing = true;
        try
        {
            _saved = await NativeFlashProfileStore.EnsureHydEpbReferenceAsync(
                _root, _expectedModel);
            if (_saved is not null)
                _draft = _saved.Profile;
            if (!string.IsNullOrWhiteSpace(_expectedModel) &&
                !string.Equals(_draft.EcuModelCode, _expectedModel, StringComparison.Ordinal))
                throw new InvalidDataException("原生烧录配置绑定型号与当前工程型号不同，不允许静默替换。");
            ModelBox.Text = _draft.EcuModelCode;
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
            FirmwareLabel.Text = _draft.FirmwareRelativePath ?? "尚未导入固件";
            SecurityLabel.Text = _draft.SecurityRelativePath ?? "尚未导入安全算法";
            StatusLabel.Text = _saved is null
                ? "新工程配置尚未保存；选择 HEX 与安全文件"
                : "工程内已保存烧录参数 · 修订 " + _saved.Profile.Revision +
                  "；还须配置 HEX 和安全文件并校验发布。";
        }
        catch (Exception error) when (error is IOException or FormatException or InvalidDataException
                                     or UnauthorizedAccessException or NotSupportedException or JsonException)
        {
            StatusLabel.Text = "读取原生配置失败：" + error.Message;
            // The editor remains open, but subsequent save must not overwrite a bad file.
            _saved = null;
            _loadFailure = true;
        }
        finally { _initializing = false; }
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
            EcuModelCode = ModelBox.Text.Trim(),
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
        JobDetailsLabel.Text = "参数已变更，请重新校验实际烧录 Job。";
        _idleSave.Stop();
        _idleSave.Start();
    }

    private async Task<bool> SaveAsync()
    {
        if (_loadFailure) return false;
        // Wait for any in-flight auto-save. Do not mistake a concurrent save
        // for a failure when the user closes the window.
        await _saveGate.WaitAsync();
        _saving = true;
        _idleSave.Stop();
        try
        {
            var next = ReadFields();
            next.ValidateDraft();
            if (!string.IsNullOrWhiteSpace(_expectedModel) &&
                !string.Equals(next.EcuModelCode, _expectedModel, StringComparison.Ordinal))
                throw new InvalidDataException("烧录配置型号必须与当前工程正式 EcuModelCode 相同。");
            if (_saved is not null &&
                Equals(next with { Revision = _saved.Profile.Revision }, _saved.Profile))
            {
                StatusLabel.Text = "原生配置已保存 · 修订 " + _saved.Profile.Revision;
                return true;
            }
            _saved = await NativeFlashProfileStore.SaveDraftAsync(_root,
                next, _saved?.ProfileSha256);
            _draft = _saved.Profile;
            StatusLabel.Text = "已自动保存原生配置 · 修订 " + _saved.Profile.Revision;
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
            _saving = false;
            _saveGate.Release();
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e) =>
        await SaveAsync();

    private async void ImportFirmware_Click(object sender, RoutedEventArgs e)
    {
        if (_loadFailure) return;
        var dialog = new OpenFileDialog
        {
            Title = "选择当前 ECU 的 HEX 固件",
            Filter = "Intel HEX 固件|*.hex;*.h86"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var next = ReadFields();
            var imported = await NativeFlashProfileStore.ImportFirmwareAsync(
                _root, dialog.FileName, next.BinBaseAddress);
            _draft = next with
            {
                FirmwareRelativePath = imported.RelativePath,
                FirmwareSha256 = imported.Sha256
            };
            FirmwareLabel.Text = imported.RelativePath;
            JobDetailsLabel.Text = "固件已更换，原 Job 预览失效，请重新校验。";
            await SaveAsync();
        }
        catch (Exception error) when (error is IOException or FormatException or InvalidDataException
                                      or NotSupportedException or UnauthorizedAccessException or JsonException)
        { StatusLabel.Text = "固件导入失败：" + error.Message; }
    }

    private async void ImportSecurity_Click(object sender, RoutedEventArgs e)
    {
        if (_loadFailure) return;
        var dialog = new OpenFileDialog
        {
            Title = "选择已审查 SafetyFile.dll",
            Filter = "DLL|*.dll"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var imported = await NativeFlashProfileStore.ImportSecurityAsync(
                _root, dialog.FileName);
            _draft = ReadFields() with
            {
                SecurityRelativePath = imported.RelativePath,
                SecuritySha256 = imported.Sha256
            };
            SecurityLabel.Text = imported.RelativePath;
            JobDetailsLabel.Text = "安全文件已更换，原 Job 预览失效，请重新校验。";
            await SaveAsync();
        }
        catch (Exception error) when (error is IOException or FormatException or InvalidDataException
                                      or NotSupportedException or UnauthorizedAccessException or JsonException)
        { StatusLabel.Text = "安全文件导入失败：" + error.Message; }
    }

    private static string FormatFrozenJob(CanToolFlashJob job)
    {
        var text =
            "编程任务 SHA256：" + job.Fingerprint + "\n" +
            "固件 SHA256：" + job.FirmwareSha256 + "\n" +
            "安全文件 SHA256：" + job.SecuritySha256 + "\n" +
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
        if (!await SaveAsync()) return;
        try
        {
            var source = (await NativeFlashProfileStore.LoadAsync(_root))!;
            var job = await Task.Run(() => NativeFlashJobBuilder.Freeze(source.Profile, _root));
            JobDetailsLabel.Text = FormatFrozenJob(job);
            StatusLabel.Text = $"离线冻结成功：{job.Blocks.Length} 个块，" +
                $"计划 {job.PlannedBytes:N0} 字节，任务 SHA {job.Fingerprint[..12]}…；没有发送任何 CAN。";
        }
        catch (Exception error) when (error is IOException or FormatException or InvalidDataException
                                      or NotSupportedException or UnauthorizedAccessException or JsonException)
        {
            JobDetailsLabel.Text = "未获得有效的冻结 Job：" + error.Message;
            StatusLabel.Text = "原生烧录预检拒绝：" + error.Message;
        }
    }

    private async void Publish_Click(object sender, RoutedEventArgs e)
    {
        if (!await SaveAsync()) return;
        try
        {
            var draft = (await NativeFlashProfileStore.LoadAsync(_root))!;
            var preview = await Task.Run(() =>
                NativeFlashJobBuilder.Freeze(draft.Profile, _root));
            JobDetailsLabel.Text = FormatFrozenJob(preview);
            var confirm = MessageBox.Show(this,
                "工程型号：" + draft.Profile.EcuModelCode + "\n\n" +
                FormatFrozenJob(preview) + "\n\n" +
                "发布后生产预检将冻结此版本；草稿修改不会影响已发布版本。" +
                "\n本操作不连接设备，也不代表已授权对 ECU 擦写。" +
                "\n\n我已核查固件、算法及上述实际下载镜像行为，确认发布？",
                "核对完整实际烧录计划后发布",
                MessageBoxButton.YesNo, MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (confirm != MessageBoxResult.Yes) return;
            var published = await NativeFlashPublicationStore.PublishAsync(_root,
                draft.ProfileSha256);
            StatusLabel.Text = "已发布当前生产配置 · " +
                published.PublishedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") +
                " · Job " + published.JobFingerprint[..12] + "…";
        }
        catch (Exception error) when (error is IOException or FormatException or
                                      InvalidDataException or NotSupportedException or
                                      UnauthorizedAccessException or JsonException)
        { StatusLabel.Text = "生产配置未发布：" + error.Message; }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closeApproved) return;
        e.Cancel = true;
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
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!IsLoaded) return;
                _closeApproved = true;
                Close();
            }), DispatcherPriority.Background);
        }
        finally { _closing = false; }
    }
}
