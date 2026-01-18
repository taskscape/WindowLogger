using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Timers;
using Timer = System.Timers.Timer;

namespace WindowLogger;

internal static class Program
{
    private static Timer? _timer;
    private const string LogFileName = "window_log.csv";
    private static string? _lastWindowTitle = "";
    private static string? _lastExecutableName = "";
    private static bool _inactive;
    private const double MinutesToInactive = 1;
    private static StreamWriter? _logWriter;
    private static readonly object _logLock = new object();

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    static void Main(string[] args)
    {
        Console.WriteLine("Active window logger started.");
        
        // Initialize StreamWriter with AutoFlush enabled
        _logWriter = new StreamWriter(LogFileName, append: true, Encoding.UTF8)
        {
            AutoFlush = true
        };
        
        _timer = new Timer(100); 
        _timer.Elapsed += TimerElapsed;
        _timer.AutoReset = true;
        _timer.Start();

        Console.WriteLine("Press Enter to exit...");
        Console.ReadLine();
        
        // Cleanup
        _timer?.Stop();
        _timer?.Dispose();
        
        lock (_logLock)
        {
            _logWriter?.Flush();
            _logWriter?.Dispose();
        }
    }

    private static void WriteLog(string logLine)
    {
        Console.WriteLine(logLine);
        lock (_logLock)
        {
            _logWriter?.WriteLine(logLine);
            // AutoFlush ensures immediate write, but we can also call Flush explicitly for critical data
            _logWriter?.Flush();
        }
        Console.WriteLine(logLine);
    }

    private static void TimerElapsed(object? sender, ElapsedEventArgs e)
    {
        TimeSpan threshold = TimeSpan.FromMinutes(MinutesToInactive);
        bool isInactive = UserActivityDetector.IsUserInactive(threshold);

        if (isInactive != _inactive)
        {
            _inactive = isInactive;

            if (_inactive)
            {
                string inactiveWindowName = string.IsNullOrWhiteSpace(_lastWindowTitle) ? "Unknown" : _lastWindowTitle;
                string inactiveExecutableName = string.IsNullOrWhiteSpace(_lastExecutableName) ? "Unknown" : _lastExecutableName;
                string logLine = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{inactiveWindowName} [{inactiveExecutableName}],Inactive";
                WriteLog(logLine);
            }
            else
            {
                string? activeWindow = GetActiveWindowTitle(out string fileName);
                activeWindow = activeWindow?.Replace(",", "");
                if (string.IsNullOrWhiteSpace(activeWindow)) return;
                _lastWindowTitle = activeWindow;
                _lastExecutableName = fileName;
                string logLine = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{activeWindow} [{fileName}],Active";
                WriteLog(logLine);
            }

            return;
        }

        if (_inactive)
        {
            return;
        }

        string? currentWindow = GetActiveWindowTitle(out string activeFileName);
        currentWindow = currentWindow?.Replace(",", "");

        if (string.IsNullOrWhiteSpace(currentWindow) || currentWindow == _lastWindowTitle) return;
        _lastWindowTitle = currentWindow;
        _lastExecutableName = activeFileName;
        string activeLogLine = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{currentWindow} [{activeFileName}],Active";
        WriteLog(activeLogLine);
    }

    private static string GetActiveWindowTitle(out string executableName)
    {
        const int nChars = 256;
        StringBuilder buff = new(nChars);
        executableName = "unknown";

        IntPtr handle = GetForegroundWindow();

        if (GetWindowText(handle, buff, nChars) <= 0) return null!;
        GetWindowThreadProcessId(handle, out uint processId);

        try
        {
            Process processById = Process.GetProcessById((int)processId);
            executableName = Path.GetFileName(processById.MainModule?.FileName) ?? "unknown";
        }
        catch (Exception ex)
        {
            executableName = $"error: {ex.Message}";
        }

        return buff.ToString();

    }
}