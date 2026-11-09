using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using AutoTest.Desktop.Tools.Dbc;
using AutoTest.Desktop.Tools.Flash;
using AutoTest.Desktop.Tools.Production;
using AutoTest.Desktop.Tools.Uds;
using AutoTest.Desktop.Tools.Wave;
using Microsoft.Win32;

namespace AutoTest.Desktop.Shell.R1;

/// <summary>
/// Single-host presentation adapter. Only actually creatable specialist views
/// are listed as available; it never opens USB/CAN or grants physical authority.
/// Future device integration must inject the one Station/Hub from the HOST.
/// </summary>
public sealed class R1SpecialistDocumentHost : IWorkspaceDocumentHost, IDisposable
{
    private readonly Dictionary<string, FrameworkElement> _open = new(StringComparer.Ordinal);
    private readonly R1SharedCanSource _sharedCan;
    private readonly Func<R1SharedCanSource, UdsToolViewModel> _udsFactory;
    private readonly Func<R1SharedCanSource, FrameworkElement> _waveFactory;

    // The UDS owner supplies a physical-capable VM only after DEVICE's authorized
    // diagnostic transaction port and the matching UDS adapter have passed review.
    // Until then the default factory creates the existing Mock/Replay-only VM.
    public R1SpecialistDocumentHost(R1SharedCanSource? sharedCan = null,
        Func<R1SharedCanSource, UdsToolViewModel>? udsFactory = null,
        Func<R1SharedCanSource, FrameworkElement>? waveFactory = null)
    {
        _sharedCan = sharedCan ?? new R1SharedCanSource();
        _udsFactory = udsFactory ?? (_ => new UdsToolViewModel());
        _waveFactory = waveFactory ?? (_ => new R1WaveReplayDocument());
    }

    public R1SharedCanSource SharedCanSource => _sharedCan;

    private static readonly IReadOnlyList<string> Available =
        Array.AsReadOnly(new[] { "P04", "P07", "P08", "P09", "P12" });

    public IReadOnlyList<string> AvailableWorkspaceIds => Available;
    public FrameworkElement? GetDocument(string id) =>
        _open.TryGetValue(id, out var document) ? document : null;

    public bool TryActivate(string workspaceId, out string failureReason)
    {
        failureReason = "";
        if (_open.ContainsKey(workspaceId))
            return true;
        try
        {
            FrameworkElement document = workspaceId switch
            {
                "P04" => new DbcToolView(new DbcToolViewModel()),
                "P07" => _waveFactory(_sharedCan) ??
                    throw new InvalidOperationException("R1_WAVE_FACTORY_RETURNED_NULL"),
                "P08" => new UdsToolView { DataContext = _udsFactory(_sharedCan) ??
                    throw new InvalidOperationException("R1_UDS_FACTORY_RETURNED_NULL") },
                "P09" => new FlashToolView { DataContext = new FlashToolViewModel() },
                "P12" => new LabProductionToolView(),
                _ => throw new InvalidOperationException(
                    "该专业页面没有实际 View 或尚未实现安全接线，禁止展示占位页")
            };
            _open.Add(workspaceId, document);
            return true;
        }
        catch (Exception ex)
        {
            failureReason = ex.Message;
            return false;
        }
    }

    public bool TryClose(string workspaceId, out string failureReason)
    {
        failureReason = "";
        if (_open.Remove(workspaceId, out var document))
            ReleaseOwnedOfflineDocument(document);
        // Do not dispose arbitrary injected specialist providers: these may reference
        // DEVICE-owned USB/Hub. UI owner must explicitly define its own close contract.
        return true;
    }

    public void Dispose()
    {
        foreach (var item in _open.Values)
            ReleaseOwnedOfflineDocument(item);
        _open.Clear();
    }

    private static void ReleaseOwnedOfflineDocument(FrameworkElement document)
    {
        if (document is R1WaveReplayDocument offline)
            offline.Dispose(); // only local Wave replay session, never shared USB/CAN
    }
}

/// <summary>
/// Hosted version of the existing offline WaveWorkbench: a real import/replay UI,
/// not the old G4 simulated wave page. Never owns a physical USB/CAN transmitter.
/// </summary>
internal sealed class R1WaveReplayDocument : UserControl, IDisposable
{
    private readonly WaveOfflineLabSession _session;
    private readonly WaveWorkbenchView _wave;
    private readonly TextBlock _status;
    private bool _disposed;

    public R1WaveReplayDocument()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AutoTest", "R1WaveOffline");
        _session = new WaveOfflineLabSession(root);
        _wave = new WaveWorkbenchView();
        _wave.Model.BindBackend(_session.Backend);
        _status = new TextBlock
        {
            Text = "REPLAY · 无物理 CAN Tx · 请选择 DBC 或 AutoTest 工程",
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(10, 0, 0, 0)
        };
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(8)
        };
        var dbc = new Button { Content = "导入 DBC" };
        dbc.Click += ImportDbc;
        var project = new Button { Content = "打开 AutoTest 工程" };
        project.Click += OpenProject;
        header.Children.Add(dbc);
        header.Children.Add(project);
        header.Children.Add(_status);
        var dock = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        dock.Children.Add(header);
        dock.Children.Add(_wave);
        Content = dock;
    }

    private void ImportDbc(object sender, RoutedEventArgs args)
    {
        var picker = new OpenFileDialog
        {
            Filter = "CAN database (*.dbc)|*.dbc",
            CheckFileExists = true
        };
        if (picker.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            if (_wave.Model.Busy)
                throw new InvalidOperationException("波形正忙，请完成或取消操作后再修改 DBC");
            _session.ImportDbcFile(picker.FileName);
            _wave.Model.RefreshSignals();
            _status.Text = "DBC: " + Path.GetFileName(picker.FileName)
                + " · REPLAY_ONLY";
            _wave.Model.ReportExternalOperation("LoadDBC", "Completed", _status.Text);
        }
        catch (Exception ex)
        {
            _status.Text = "DBC 导入失败: " + ex.Message;
            _wave.Model.ReportExternalOperation("LoadDBC", "Failed",
                ex.Message, "WAVE_DBC_LOAD_FAILED");
        }
    }

    private async void OpenProject(object sender, RoutedEventArgs args)
    {
        var picker = new OpenFileDialog
        {
            Filter = "AutoTest project (*.autotest-project.json)|*.autotest-project.json",
            CheckFileExists = true
        };
        if (picker.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            if (_wave.Model.Busy)
                throw new InvalidOperationException("波形正忙，请先完成或取消采集");
            await _session.OpenProjectAsync(picker.FileName);
            _wave.Model.RefreshSignals();
            _status.Text = "工程: " + _session.Workspace.Project.Name
                + " · REPLAY_ONLY";
            _wave.Model.ReportExternalOperation("OpenProject", "Completed", _status.Text);
        }
        catch (Exception ex)
        {
            _status.Text = "工程加载失败: " + ex.Message;
            _wave.Model.ReportExternalOperation("OpenProject", "Failed",
                ex.Message, "WAVE_PROJECT_LOAD_FAILED");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_wave.Model.StopCommand.CanExecute(null))
            _wave.Model.StopCommand.Execute(null);
        _session.Dispose();
    }
}
