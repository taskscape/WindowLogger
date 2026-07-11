using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace WindowLogger;

internal static class Program
{
    private const string LogFileName = "WindowLogger.csv";
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan InactivityThreshold = TimeSpan.FromMinutes(1);

    private static string _lastWindowTitle = string.Empty;
    private static string _lastExecutableName = string.Empty;
    private static uint _lastProcessId;
    private static bool _inactive;
    private static StreamWriter? _logWriter;
    private static readonly object LogLock = new();

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern int GetWindowTextLengthW(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private static void Main(string[] args)
    {
        Console.WriteLine("Active window logger started.");

        string logPath = args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal)
            ? args[0]
            : Path.Combine(AppContext.BaseDirectory, LogFileName);

        string? logDirectory = Path.GetDirectoryName(Path.GetFullPath(logPath));
        if (!string.IsNullOrEmpty(logDirectory))
        {
            Directory.CreateDirectory(logDirectory);
        }

        bool writeHeader = !File.Exists(logPath) || new FileInfo(logPath).Length == 0;
        _logWriter = new StreamWriter(logPath, append: true, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true
        };

        if (writeHeader)
        {
            _logWriter.WriteLine("Timestamp,WindowTitle,Status");
        }
        WriteSessionMarker("SessionStart");

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };

        Task pollingTask = PollAsync(cancellation.Token);

        try
        {
            WaitForShutdown(args, cancellation.Token);
        }
        finally
        {
            cancellation.Cancel();
            try
            {
                pollingTask.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                // Expected during normal shutdown.
            }

            WriteSessionMarker("SessionEnd");
            lock (LogLock)
            {
                _logWriter?.Dispose();
                _logWriter = null;
            }
        }
    }

    private static void WaitForShutdown(string[] args, CancellationToken cancellationToken)
    {
        int eventOptionIndex = Array.FindIndex(args, arg =>
            string.Equals(arg, "--stop-event", StringComparison.OrdinalIgnoreCase));

        if (eventOptionIndex >= 0 && eventOptionIndex + 1 < args.Length)
        {
            using EventWaitHandle stopEvent = EventWaitHandle.OpenExisting(args[eventOptionIndex + 1]);
            WaitHandle.WaitAny([stopEvent, cancellationToken.WaitHandle]);
            return;
        }

        Console.WriteLine("Press Enter to exit...");
        Task inputTask = Task.Run(Console.ReadLine);
        Task cancellationTask = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        Task.WhenAny(inputTask, cancellationTask).GetAwaiter().GetResult();
    }

    private static async Task PollAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(PollInterval);

        do
        {
            try
            {
                PollActiveWindow();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Window polling failed: {ex.Message}");
            }
        }
        while (await timer.WaitForNextTickAsync(cancellationToken));
    }

    private static void WriteLog(string windowTitle, string executableName, string status)
    {
        string displayName = $"{windowTitle} [{executableName}]";
        WriteCsvRecord(displayName, status);
    }

    private static void WriteSessionMarker(string status) => WriteCsvRecord(string.Empty, status);

    private static void WriteCsvRecord(string windowTitle, string status)
    {
        string logLine = string.Join(",",
            EscapeCsv(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")),
            EscapeCsv(windowTitle),
            EscapeCsv(status));

        Console.WriteLine(logLine);
        lock (LogLock)
        {
            _logWriter?.WriteLine(logLine);
        }
    }

    private static string EscapeCsv(string value)
    {
        if (value.IndexOfAny([',', '"', '\r', '\n']) < 0)
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static void PollActiveWindow()
    {
        bool isInactive = UserActivityDetector.IsUserInactive(InactivityThreshold);

        if (isInactive != _inactive)
        {
            _inactive = isInactive;

            if (_inactive)
            {
                WriteLog(
                    string.IsNullOrWhiteSpace(_lastWindowTitle) ? "Unknown" : _lastWindowTitle,
                    string.IsNullOrWhiteSpace(_lastExecutableName) ? "unknown" : _lastExecutableName,
                    "Inactive");
            }
            else if (TryGetActiveWindow(out WindowInfo activeWindow))
            {
                UpdateLastWindow(activeWindow);
                WriteLog(_lastWindowTitle, _lastExecutableName, "Active");
            }

            return;
        }

        if (_inactive || !TryGetActiveWindow(out WindowInfo currentWindow))
        {
            return;
        }

        if (currentWindow.ProcessId == _lastProcessId && currentWindow.Title == _lastWindowTitle)
        {
            return;
        }

        UpdateLastWindow(currentWindow);
        WriteLog(_lastWindowTitle, _lastExecutableName, "Active");
    }

    private static void UpdateLastWindow(WindowInfo window)
    {
        string executableName = window.ProcessId == _lastProcessId && !string.IsNullOrEmpty(_lastExecutableName)
            ? _lastExecutableName
            : GetExecutableName(window.ProcessId);

        _lastWindowTitle = window.Title;
        _lastProcessId = window.ProcessId;
        _lastExecutableName = executableName;
    }

    private static bool TryGetActiveWindow(out WindowInfo window)
    {
        window = default;
        IntPtr handle = GetForegroundWindow();
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        int titleLength = GetWindowTextLengthW(handle);
        if (titleLength <= 0)
        {
            return false;
        }

        var buffer = new StringBuilder(titleLength + 1);
        if (GetWindowTextW(handle, buffer, buffer.Capacity) <= 0)
        {
            return false;
        }

        GetWindowThreadProcessId(handle, out uint processId);
        window = new WindowInfo(buffer.ToString(), processId);
        return true;
    }

    private static string GetExecutableName(uint processId)
    {
        try
        {
            using Process process = Process.GetProcessById(checked((int)processId));
            return Path.GetFileName(process.MainModule?.FileName) ?? "unknown";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return "unknown";
        }
    }

    private readonly record struct WindowInfo(string Title, uint ProcessId);
}
