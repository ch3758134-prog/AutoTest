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
        try
        {
            var message = "界面操作发生异常：" + e.Exception.Message +
                (log is null ? "\n未能写入错误日志。" :
                    "\n已记录完整异常堆栈：" + log);
            MessageBox.Show(message, "AutoTest · 界面错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception displayError)
        {
            UiExceptionReporter.Record("Failed to show dispatcher error", displayError);
        }
        // Keep the user's project and other editor tabs alive for recoverable
        // WPF interaction errors; fatal CLR exceptions still terminate normally.
        e.Handled = e.Exception is not (OutOfMemoryException or AccessViolationException);
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
