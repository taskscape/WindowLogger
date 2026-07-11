using System.Windows;

namespace WindowLoggerConfigGui;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var window = new MainWindow(e.Args.Length > 0 ? e.Args[0] : null);
        MainWindow = window;
        window.Show();
    }
}
