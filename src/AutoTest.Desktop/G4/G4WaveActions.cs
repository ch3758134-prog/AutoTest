using AutoTest.Application.G4;
using AutoTest.Desktop.Tools;

namespace AutoTest.Desktop.G4;

/// <summary>Wave buttons invoke the actual CAN Hub / DBC / reference evidence adapters.</summary>
public sealed class G4WaveActions : IWaveToolActions
{
    private readonly G4IntegrationHost _host;
    private readonly WaveToolViewModel _view;
    private CancellationTokenSource? _running;
    public G4WaveActions(G4IntegrationHost host, WaveToolViewModel view)
    { _host = host; _view = view; }
    public Task StartAsync(CancellationToken ct) => ReplayAsync(ct);
    public async Task ReplayAsync(CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _running = cts;
        try
        {
            var result = await _host.Wave.ReplayAsync(ct: cts.Token);
            Draw(result);
            _view.ShowComparison("REPLAY / NOT_PRODUCTION", null, null,
                "CAN Hub RX → DBC Signal → immutable WaveEvidence; 模拟回放完成");
        }
        finally { if (ReferenceEquals(_running, cts)) _running = null; }
    }
    public Task StopAsync(CancellationToken ct)
    {
        _running?.Cancel();
        return Task.CompletedTask;
    }
    public Task MarkReferenceAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var reference = _host.Wave.SaveReference();
        if (_host.Wave.Latest is { } result) Draw(result);
        _view.ShowComparison("REFERENCE PINNED", null, null,
            "参考资源版本和 SHA 已保存：" + reference.Reference.Asset.Revision.Value);
        return Task.CompletedTask;
    }
    public async Task CompareAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var result = await _host.Wave.CompareAsync();
        _view.ShowComparison("MOCK / " + result.Verdict + " / NOT_PRODUCTION",
            result.Coverage, null, result.Reason);
    }
    private void Draw(WaveReplayResult result)
    {
        var baseTime = result.Series.Origin.Utc;
        var samples = result.Series.Samples.Select(x =>
            new WavePlotPoint((x.Sample.Time.Utc - baseTime).TotalSeconds, x.Sample.Value)).ToArray();
        var reference = _host.Wave.Reference;
        var refPoints = reference?.Series.Samples.Select(x =>
            new WavePlotPoint((x.Sample.Time.Utc - reference.Series.Origin.Utc).TotalSeconds,
                x.Sample.Value)).ToArray();
        _view.ShowChart(result.Signal.SignalName, "CAN-HUB / DBC / REPLAY",
            result.Series.Origin.Quality.ToString(), samples, refPoints,
            0.1, result.Signal.Unit, reference?.Name ?? "尚无参考波形");
    }
}
