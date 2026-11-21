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
        // A presentation exception may have interrupted an equipment write or
        // left a live task in an unknown state. Interlock before showing UI.
        try
        {
            if (Application.Current?.MainWindow is MainWindow shell)
                shell.EnterUnhandledUiFaultState(e.Exception);
        }
        catch (Exception interlockError)
        {
            UiExceptionReporter.Record("Emergency physical interlock failed", interlockError);
        }
        try
        {
            var message = "界面操作发生异常：" + e.Exception.Message +
                "\n主控写入已请求异常联锁；若实体动作不确定，请按 UNKNOWN 处理。";
                (log is null ? "\n未能写入错误日志。" :
                    "\n已记录完整异常堆栈：" + log);
            var answer = MessageBox.Show(message +
                "\n\n是否将异常类型、完整调用堆栈和日志路径复制到剪贴板？" +
                "\n复制后可以直接粘贴给研发人员，无需自行翻找日志。",
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
