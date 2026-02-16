using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace WindowLogger;

internal static class Program
{
    private const string LogFileName = "WindowLogger.csv";
    private const string MutexName = "WindowLogger_App_V2_UniqueString";
    private static Mutex? _mutex;
    private static StreamWriter? _logWriter;
    private static string? _lastWindowTitle;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    static void Main(string[] args)
    {
        bool createdNew;
        _mutex = new Mutex(true, MutexName, out createdNew);
        if (!createdNew)
        {
            return; 
        }

        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string logDir = Path.Combine(appData, "WindowLogger");

            if (!Directory.Exists(logDir))
            {
                Directory.CreateDirectory(logDir);
            }

            string logPath = Path.Combine(logDir, LogFileName);
            

            bool writeHeader = !File.Exists(logPath) || new FileInfo(logPath).Length == 0;
            

            var fileStream = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.Read);
            _logWriter = new StreamWriter(fileStream, Encoding.UTF8) { AutoFlush = true };

            if (writeHeader)
            {
                _logWriter.WriteLine("Timestamp,WindowTitle");
            }

            while (true)
            {
                LogActiveWindow();
                Thread.Sleep(1000);
            }
        }
        catch (Exception)
        {
        }
    }

    private static void LogActiveWindow()
    {
        try
        {
            IntPtr handle = GetForegroundWindow();
            if (handle == IntPtr.Zero) return;

            const int nChars = 256;
            StringBuilder buff = new StringBuilder(nChars);
            
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
        }
    }
}
