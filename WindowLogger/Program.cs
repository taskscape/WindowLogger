using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Timers;
using Timer = System.Timers.Timer;

class Program
{
    private static Timer? _timer;
    private const string logFileName = "window_log.csv";
    private static string? _lastWindowTitle = "";

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    static void Main(string[] args)
    {
        Console.WriteLine("Active window logger started.");
        
        _timer = new Timer(100); 
        _timer.Elapsed += TimerElapsed;
        _timer.AutoReset = true;
        _timer.Start();

        Console.WriteLine("Press Enter to exit...");
        Console.ReadLine();
    }

    private static void TimerElapsed(object sender, ElapsedEventArgs e)
    {
        string? currentWindow = GetActiveWindowTitle(out string fileName);
        currentWindow = currentWindow?.Replace(",", "");

        if (string.IsNullOrWhiteSpace(currentWindow) || currentWindow == _lastWindowTitle) return;
        _lastWindowTitle = currentWindow;
        string logLine = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{currentWindow} [{fileName}]";
        File.AppendAllText(logFileName, logLine + Environment.NewLine);
        Console.WriteLine(logLine);
    }

    private static string GetActiveWindowTitle(out string executableName)
    {
        const int nChars = 256;
        StringBuilder buff = new(nChars);
        executableName = "unknown";

        IntPtr handle = GetForegroundWindow();

        if (GetWindowText(handle, buff, nChars) > 0)
        {
            uint processId;
            GetWindowThreadProcessId(handle, out processId);

            try
            {
                Process processById = Process.GetProcessById((int)processId);
                executableName = Path.GetFileName(processById.MainModule.FileName);
            }
            catch (Exception ex)
            {
                executableName = $"error: {ex.Message}";
            }

            return buff.ToString();
        }

        return null;
    }
}