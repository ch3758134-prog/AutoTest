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
            // Unexpected WPF faults are logged automatically, not shown as
            // technical stack dumps or clipboard questions to an operator.
            MessageBox.Show(
                "当前操作未完成，请检查编辑内容是否已保存。" +
                (log is null
                    ? "\n错误日志写入失败。"
                    : "\n技术诊断已自动记录；重复出现时请向维护人员提供日志。"),
                "AutoTest · 操作失败", MessageBoxButton.OK, MessageBoxImage.Error);
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
