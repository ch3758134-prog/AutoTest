using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace EPB_UsbTest;

/// <summary>
/// 全局异常兜底：任何未捕获异常都弹窗显示具体错误（含堆栈）+ 落盘 crash.log。
/// 根因：此前无任何处理器，任何一行异常都会让程序无声闪退，导致所有 bug 不可见。
/// </summary>
public partial class App : Application
{
    public App()
    {
        // 某些受控启动环境没有继承 WINDIR；WPF 的字体缓存会在创建第一个窗口前依赖它。
        // 正常 Windows 会保留原值，这里只补齐缺失的系统目录。
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WINDIR")))
            Environment.SetEnvironmentVariable("WINDIR", Environment.GetEnvironmentVariable("SystemRoot") ?? "C:\\Windows");
        DispatcherUnhandledException += OnDispatcherUnhandledException;   // 界面线程（含按钮点击）
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;   // 进程级
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;   // 后台 fire-and-forget 任务
    }

    /// <summary>界面线程异常（按钮点击等）。标记 Handled 保持程序存活，让错误可见而非整窗消失。</summary>
    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        Crash(e.Exception, "界面线程异常");
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        => Crash(e.ExceptionObject as Exception ?? new Exception("未知非托管异常"), "进程级异常");

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
        Crash(e.Exception, "后台任务异常");
    }

    /// <summary>把异常写进 crash.log 并弹窗（两者各自 try/catch，日志失败绝不掩盖真实错误）。</summary>
    private static void Crash(Exception ex, string where)
    {
        string text = $"{where}\n\n{ex}";   // 完整 ToString 含调用堆栈
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "crash.log");
            File.AppendAllText(path, $"\n[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {where}\n{ex}\n");
        }
        catch { }
        try { MessageBox.Show(text, "程序异常", MessageBoxButton.OK, MessageBoxImage.Error); }
        catch { }
    }
}
