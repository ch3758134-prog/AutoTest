using System.Diagnostics;
using System.IO;
using System.Text;

namespace AutoTest.Desktop.Diagnostics;

/// <summary>Best-effort crash evidence outside the user project; never blocks error reporting.</summary>
public static class UiExceptionReporter
{
    public static string? Record(string context, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        try
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(root)) root = Path.GetTempPath();
            var folder = Path.Combine(root, "AutoTest", "logs");
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, "ui-errors.log");
            var body = Environment.NewLine + "=== " +
                DateTimeOffset.Now.ToString("O") + " | PID=" +
                Environment.ProcessId + " | " + context + " ===" +
                Environment.NewLine + exception + Environment.NewLine;
            File.AppendAllText(file, body, new UTF8Encoding(false));
            Trace.TraceError(body);
            return file;
        }
        catch (Exception logError)
        {
            try { Trace.TraceError("Unable to write AutoTest UI error: " + logError); }
            catch { /* Never replace the original UI exception. */ }
            return null;
        }
    }
}
