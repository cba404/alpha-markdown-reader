using System.Windows;

namespace AlphaNative;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var path = e.Args.FirstOrDefault(arg =>
            arg.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
            arg.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase) ||
            arg.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));

        var window = new MainWindow(path);
        MainWindow = window;
        window.Show();
    }
}
