using System.Windows;
using System.Windows.Threading;
using AutoTest.Desktop.Diagnostics;
using AutoTest.Desktop.Bootstrap;
using AutoTest.Desktop.Shell;
using Prism.DryIoc;
using Prism.Ioc;

namespace AutoTest.Desktop;

public partial class App : PrismApplication
{
    private static int _uiErrorDialogActive;
    private static long _nextUiErrorDialogAt;

    public App()
    {
        // Runtime WPF faults may be raised after an editor has been attached to
        // the visual tree, outside its command's local try/catch.
        DispatcherUnhandledException += OnUiDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += OnClrUnhandledException;
    }

    private static void OnUiDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var log = UiExceptionReporter.Record("WPF dispatcher (unhandled)", e.Exception);
        var fatal = e.Exception is OutOfMemoryException or AccessViolationException;
        e.Handled = !fatal;
        if (fatal) return;

        // During a failing data-binding/update storm, nested modal dialogs can
        // themselves lock the dispatcher. Show at most one diagnostic per 30 s;
        // always retain the full error stack in the on-disk log.
        if (Environment.TickCount64 < Interlocked.Read(ref _nextUiErrorDialogAt) ||
            Interlocked.CompareExchange(ref _uiErrorDialogActive, 1, 0) != 0)
            return;
        Interlocked.Exchange(ref _nextUiErrorDialogAt, Environment.TickCount64 + 30_000);
        try
        {
            var message = "界面操作发生异常：" + e.Exception.Message +
                (log is null ? "\n未能写入错误日志。" :
                    "\n已记录完整异常堆栈：" + log);
            var answer = MessageBox.Show(message +
                "\n\n是否将异常类型、完整调用堆栈和日志路径复制到剪贴板？",
                "AutoTest · 界面错误", MessageBoxButton.YesNo, MessageBoxImage.Error);
            if (answer == MessageBoxResult.Yes)
                Clipboard.SetText("AutoTest · UI 诊断\n" +
                    DateTimeOffset.Now.ToString("O") + "\n" +
                    e.Exception + (log is null ? "" : "\n日志：" + log));
        }
        catch (Exception displayError)
        {
            UiExceptionReporter.Record("Failed to show dispatcher error", displayError);
        }
        finally
        {
            Interlocked.Exchange(ref _uiErrorDialogActive, 0);
        }
    }

    private static void OnClrUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
            UiExceptionReporter.Record("CLR unhandled (fatal)", exception);
    }

    protected override void RegisterTypes(IContainerRegistry registry) =>
        PrismRegistration.RegisterTypes(registry);

    protected override Window CreateShell() => Container.Resolve<MainWindow>();
}
