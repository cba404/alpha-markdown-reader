using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace AlphaNative;

public partial class App : Application
{
    private static readonly string DiagnosticDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Alpha");

    private static readonly string CrashLogPath = Path.Combine(DiagnosticDirectory, "startup-error.log");

    public App()
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var paths = e.Args
                .Where(arg =>
                    arg.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
                    arg.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase) ||
                    arg.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                .Where(File.Exists)
                .ToArray();

            var window = new MainWindow(paths);
            MainWindow = window;
            window.Show();
            window.Activate();
        }
        catch (Exception ex)
        {
            ReportFatalError("应用启动", ex);
            Shutdown(-1);
        }
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ReportFatalError("界面线程", e.Exception);
        e.Handled = true;
        Shutdown(-1);
    }

    private static void CurrentDomain_UnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            WriteCrashLog("后台线程", ex);
        }
    }

    private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteCrashLog("后台任务", e.Exception);
        e.SetObserved();
    }

    private static void ReportFatalError(string stage, Exception exception)
    {
        WriteCrashLog(stage, exception);
        try
        {
            MessageBox.Show(
                $"α 无法继续运行。\n\n错误阶段：{stage}\n错误信息：{exception.Message}\n\n诊断日志已保存到：\n{CrashLogPath}",
                "α 启动错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // Even diagnostics must not throw a second startup exception.
        }
    }

    private static void WriteCrashLog(string stage, Exception exception)
    {
        try
        {
            Directory.CreateDirectory(DiagnosticDirectory);
            var builder = new StringBuilder();
            builder.AppendLine(new string('=', 72));
            builder.AppendLine($"Time: {DateTimeOffset.Now:O}");
            builder.AppendLine($"Stage: {stage}");
            builder.AppendLine($"OS: {Environment.OSVersion}");
            builder.AppendLine($"Runtime: {Environment.Version}");
            builder.AppendLine($"Executable: {Environment.ProcessPath}");
            builder.AppendLine(exception.ToString());
            File.AppendAllText(CrashLogPath, builder.ToString(), Encoding.UTF8);
        }
        catch
        {
            // Ignore logging errors so they do not hide the original exception.
        }
    }
}
