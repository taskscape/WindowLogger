namespace WindowLoggerTray;

public partial class App : System.Windows.Application
{
    private TrayController? _controller;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        _controller = new TrayController();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _controller?.Dispose();
        _controller = null;
        base.OnExit(e);
    }
}
