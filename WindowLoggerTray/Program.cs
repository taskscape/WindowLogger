using System;
using System.Windows.Forms;

namespace WindowLoggerTray;

static class Program
{
    [STAThread]
    static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Launching the app while using out context instead of Form
        Application.Run(new TrayApplicationContext());
    }
}