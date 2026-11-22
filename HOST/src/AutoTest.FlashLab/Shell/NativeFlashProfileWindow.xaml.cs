using System.ComponentModel;
using System.Globalization;
using System.IO;
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
            _saved = await NativeFlashProfileStore.LoadAsync(_root);
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
            BlockBox.Text = _draft.BlockLength.ToString(CultureInfo.InvariantCulture);
            BinBaseBox.Text = _draft.BinBaseAddress ?? "";
            FirmwareLabel.Text = _draft.FirmwareRelativePath ?? "尚未导入固件";
            SecurityLabel.Text = _draft.SecurityRelativePath ?? "尚未导入安全算法";
            StatusLabel.Text = _saved is null
                ? "新工程原生烧录草稿：填入参数后自动保存"
                : "已读取工程内配置 · 修订 " + _saved.Profile.Revision;
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

    private static uint ParseHex(TextBox input) =>
        string.IsNullOrWhiteSpace(input.Text) ? 0u :
        CanToolXmlProfileReader.ParseHexUInt(input.Text, input.Name);

    private NativeFlashProfileDocument ReadFields()
    {
        if (!int.TryParse(BlockBox.Text, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var block))
            throw new FormatException("块长请输入 64～4096 的整数字节数。");
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
            BlockLength = block,
            BinBaseAddress = string.IsNullOrWhiteSpace(BinBaseBox.Text)
                ? null : BinBaseBox.Text.Trim()
        };
    }

    private void EditorChanged(object sender, TextChangedEventArgs e) => QueueAutoSave();
    private void EditorSelectionChanged(object sender, SelectionChangedEventArgs e) => QueueAutoSave();

    private void QueueAutoSave()
    {
        if (_initializing || _loadFailure || _closing) return;
        _idleSave.Stop();
        _idleSave.Start();
    }

    private async Task<bool> SaveAsync()
    {
        if (_saving || _loadFailure) return false;
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
        finally { _saving = false; }
    }

    private async void Save_Click(object sender, RoutedEventArgs e) =>
        await SaveAsync();

    private async void ImportFirmware_Click(object sender, RoutedEventArgs e)
    {
        if (_loadFailure) return;
        var dialog = new OpenFileDialog
        {
            Title = "选择 HEX / S19 / BIN 固件",
            Filter = "ECU 固件|*.hex;*.h86;*.s19;*.s28;*.s37;*.sx;*.mot;*.bin"
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
            await SaveAsync();
        }
        catch (Exception error) when (error is IOException or FormatException or InvalidDataException
                                      or NotSupportedException or UnauthorizedAccessException or JsonException)
        { StatusLabel.Text = "安全文件导入失败：" + error.Message; }
    }

    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (!await SaveAsync()) return;
        try
        {
            var source = (await NativeFlashProfileStore.LoadAsync(_root))!;
            var job = await Task.Run(() => NativeFlashJobBuilder.Freeze(source.Profile, _root));
            StatusLabel.Text = $"离线冻结成功：{job.Blocks.Length} 个块，" +
                $"计划 {job.PlannedBytes:N0} 字节，任务 SHA {job.Fingerprint[..12]}…；没有发送任何 CAN。";
        }
        catch (Exception error) when (error is IOException or FormatException or InvalidDataException
                                      or NotSupportedException or UnauthorizedAccessException or JsonException)
        { StatusLabel.Text = "原生烧录预检拒绝：" + error.Message; }
    }

    private async void Publish_Click(object sender, RoutedEventArgs e)
    {
        if (!await SaveAsync()) return;
        try
        {
            var draft = (await NativeFlashProfileStore.LoadAsync(_root))!;
            var preview = await Task.Run(() =>
                NativeFlashJobBuilder.Freeze(draft.Profile, _root));
            var confirm = MessageBox.Show(this,
                "工程型号：" + draft.Profile.EcuModelCode + "\\n" +
                "固件 SHA256：" + preview.FirmwareSha256 + "\\n" +
                "真实编程任务 SHA256：" + preview.Fingerprint + "\\n\\n" +
                "发布后生产预检将冻结此版本；后续草稿修改不影响已发布版本。" +
                "\\n本操作不连接设备，也不代表已授权对 ECU 擦写。\\n\\n确认发布？",
                "发布当前烧录配置", MessageBoxButton.YesNo,
                MessageBoxImage.Question, MessageBoxResult.No);
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
                StatusLabel.Text += "。窗口保留，防止未保存内容丢失。";
                return;
            }
            _closeApproved = true;
            Close();
        }
        finally { _closing = false; }
    }
}
