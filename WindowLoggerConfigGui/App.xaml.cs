using System.Threading;
using System.Windows;

namespace WindowLoggerConfigGui;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\WindowLoggerControlCenter-D78EF332-EED0-4B29-B475-6D74299D71E5";
    private Mutex? _singleInstanceMutex;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out bool createdNew);
        _ownsMutex = createdNew;
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        var window = new MainWindow(e.Args.Length > 0 ? e.Args[0] : null);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
