using System.IO;

namespace AutoTest.Desktop.Diagnostics;

/// <summary>
/// Describes the actual loaded desktop binary. The module version ID
/// changes when the compiled assembly changes; this is not a fabricated
/// Git commit SHA and does not assume a particular checkout directory.
/// </summary>
public static class RuntimeBuildIdentity
{
    private static readonly Guid ModuleId =
        typeof(App).Assembly.ManifestModule.ModuleVersionId;

    public static string ShortLabel => "构建 " + ModuleId.ToString("N")[..10];

    public static string DiagnosticText
    {
        get
        {
            var file = Environment.ProcessPath ??
                typeof(App).Assembly.Location;
            var timestamp = "unknown";
            try
            {
                if (File.Exists(file))
                    timestamp = File.GetLastWriteTimeUtc(file).ToString("O");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            return "运行程序：" + file + Environment.NewLine +
                "程序集 MVID：" + ModuleId.ToString("D") + Environment.NewLine +
                "文件修改 UTC：" + timestamp + Environment.NewLine +
                "进程 PID：" + Environment.ProcessId;
        }
    }
}
