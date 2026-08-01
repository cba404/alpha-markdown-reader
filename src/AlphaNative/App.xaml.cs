using System.IO;
using System.Linq;
using System.Windows;

namespace AlphaNative;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
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
    }
}
