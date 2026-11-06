using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using AutoTest.Devices.Can;
using AutoTest.Devices.Usb;

namespace AutoTest.Devices.Diagnostics;

/// <summary>Non-blocking, bounded JSONL trace writer. Queue loss is accounted, never hidden.</summary>
public sealed class HardwareTraceRecorder : IAsyncDisposable
{
    private readonly Channel<object> _queue;
    private readonly Task _pump;
    private readonly string _file;
    private readonly FileStream _fileStream;
    private long _dropped, _written;
    private bool _closed;
    public string FilePath => _file;
    public long Dropped => Interlocked.Read(ref _dropped);
    public long Written => Interlocked.Read(ref _written);
    public string? FailureMessage => _pump.IsFaulted ? _pump.Exception?.GetBaseException().Message : null;
    public event Action<string>? LineFormatted;

    public HardwareTraceRecorder(string file, int capacity = 8192)
    {
        if (string.IsNullOrWhiteSpace(file)) throw new ArgumentException("Log path required.", nameof(file));
        if (capacity < 2) throw new ArgumentOutOfRangeException(nameof(capacity));
        _file = Path.GetFullPath(file);
        // This does not create or replace the log until opened; create unique filenames in CLI.
        if (File.Exists(_file)) throw new IOException("Refusing to overwrite an existing physical trace file.");
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        // Fail connection immediately if the trace cannot be reserved/opened.
        _fileStream = new FileStream(_file, FileMode.CreateNew, FileAccess.Write, FileShare.Read,
            8192, FileOptions.Asynchronous);
        _queue = Channel.CreateBounded<object>(new BoundedChannelOptions(capacity)
        { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait });
        _pump = Task.Run(WriteLoopAsync);
    }
    public void Record(UsbTraceRecord evt) => Enqueue(evt);
    public void Record(HardwareCanTrace evt) => Enqueue(evt);
    public void RecordMessage(string kind, string message) => Enqueue(new { Time = DateTimeOffset.UtcNow, Kind = kind, Message = message });
    private void Enqueue(object item)
    {
        if (!_queue.Writer.TryWrite(item)) Interlocked.Increment(ref _dropped);
    }
    private async Task WriteLoopAsync()
    {
        await using var writer = new StreamWriter(_fileStream, new UTF8Encoding(false));
        writer.AutoFlush = true; // prioritize preserving fault/timeout facts at the bench
        await foreach (var item in _queue.Reader.ReadAllAsync())
        {
            string line = JsonSerializer.Serialize(item, item.GetType());
            await writer.WriteLineAsync(line).ConfigureAwait(false);
            Interlocked.Increment(ref _written);
            foreach (Action<string> callback in LineFormatted?.GetInvocationList()
                         .Cast<Action<string>>() ?? Enumerable.Empty<Action<string>>())
            { try { callback(line); } catch { } }
        }
        await writer.FlushAsync().ConfigureAwait(false);
    }
    public async ValueTask DisposeAsync()
    {
        if (_closed) return;
        _closed = true;
        _queue.Writer.TryComplete();
        await _pump.ConfigureAwait(false);
    }
}
