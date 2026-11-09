using System.IO;
using System.Text;
using System.Text.Json;
using AutoTest.Devices.Diagnostics;

namespace AutoTest.DeviceLab;

/// <summary>Independent durable UI-operation JSONL; USB/CAN raw bytes remain in HardwareTraceRecorder.</summary>
public sealed class DeviceLabAuditJournal : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly object _sync = new();
    private bool _disposed;
    public string FilePath { get; }
    public string? FailureMessage { get; private set; }

    public DeviceLabAuditJournal()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AutoTest", "DeviceLab", "Operations");
        Directory.CreateDirectory(dir);
        FilePath = Path.Combine(dir, $"device-operations-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.jsonl");
        _writer = new StreamWriter(new FileStream(FilePath, FileMode.CreateNew, FileAccess.Write,
            FileShare.Read, 4096, FileOptions.WriteThrough), new UTF8Encoding(false)) { AutoFlush = true };
    }

    public void Record(DeviceLabOperationEvent record)
    {
        lock (_sync)
        {
            if (_disposed || FailureMessage is not null)
                throw new IOException("DEVICE operation audit is unavailable: " + FailureMessage);
            try { _writer.WriteLine(JsonSerializer.Serialize(record)); }
            catch (Exception ex)
            {
                FailureMessage = ex.ToString();
                throw new IOException("DEVICE operation audit write failed; stop new hardware operations.", ex);
            }
        }
    }
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _writer.Dispose();
        }
    }
}

