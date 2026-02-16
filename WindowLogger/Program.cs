using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace WindowLogger;

internal static class Program
{
    private const string LogFileName = "WindowLogger.csv";
    private const string MutexName = "WindowLogger_App_V2_UniqueString";
    private static readonly CancellationTokenSource Cts = new();
    private static Mutex? _mutex;
    private static StreamWriter? _logWriter;
    private static string? _lastWindowTitle;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    private static void Main(string[] args)
    {
        bool createdNew;
        _mutex = new Mutex(true, MutexName, out createdNew);
        if (!createdNew)
        {
            return;
        }

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Cts.Cancel();
        };

        try
        {
            string logPath = ResolveLogPath(args);
            string? logDir = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
            {
                Directory.CreateDirectory(logDir);
            }

            bool writeHeader = !File.Exists(logPath) || new FileInfo(logPath).Length == 0;
            using var fileStream = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.Read);
            _logWriter = new StreamWriter(fileStream, Encoding.UTF8) { AutoFlush = true };

            if (writeHeader)
            {
                _logWriter.WriteLine("Timestamp,WindowTitle");
            }

            while (!Cts.IsCancellationRequested)
            {
                LogActiveWindow();
                Cts.Token.WaitHandle.WaitOne(1000);
            }
        }
        catch
        {
            // Silent fail in production mode.
        }
        finally
        {
            _logWriter?.Dispose();
            if (createdNew)
            {
                _mutex.ReleaseMutex();
            }
            _mutex?.Dispose();
        }
    }

    private static string ResolveLogPath(string[] args)
    {
        if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
        {
            return args[0];
        }

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "WindowLogger", LogFileName);
    }

    private static void LogActiveWindow()
    {
        try
        {
            IntPtr handle = GetForegroundWindow();
            if (handle == IntPtr.Zero) return;

            const int nChars = 256;
            StringBuilder buff = new(nChars);

            if (GetWindowText(handle, buff, nChars) > 0)
            {
                string currentTitle = buff.ToString();
                if (currentTitle != _lastWindowTitle)
                {
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    string safeTitle = currentTitle.Replace("\"", "\"\"");

                    _logWriter?.WriteLine($"{timestamp},\"{safeTitle}\"");
                    _lastWindowTitle = currentTitle;
                }
            }
        }
        catch
        {
            // Ignore transient failures while sampling windows.
        }
    }
}
