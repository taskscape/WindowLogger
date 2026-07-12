using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using WindowLogger.Shared.Activity;
using WindowLogger.Shared.Configuration;
using WindowLogger.Shared.Settings;

namespace WindowLogger;

internal static class Program
{
    private const string DatabaseFileName = "WindowLogger.db";
    private const string SingleInstanceMutexName = @"Local\WindowLogger-Collector-D78EF332-EED0-4B29-B475-6D74299D71E5";
    private static TimeSpan _pollInterval = TimeSpan.FromMilliseconds(500);
    private static TimeSpan _inactivityThreshold = TimeSpan.FromMinutes(1);
    private static bool _storeWindowTitles = true;

    private static string _lastWindowTitle = string.Empty;
    private static string _lastExecutableName = string.Empty;
    private static uint _lastProcessId;
    private static bool _inactive;
    private static bool _sessionOpen;
    private static ActivityDatabase? _database;
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
        using var singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out bool createdNew);
        if (!createdNew)
        {
            Console.Error.WriteLine("Another WindowLogger collector is already running.");
            return;
        }

        Console.WriteLine("Active window logger started.");

        string logPath = args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal)
            ? args[0]
            : Path.Combine(AppContext.BaseDirectory, DatabaseFileName);

        string? logDirectory = Path.GetDirectoryName(Path.GetFullPath(logPath));
        if (!string.IsNullOrEmpty(logDirectory))
        {
            Directory.CreateDirectory(logDirectory);
        }

        _database = new ActivityDatabase(logPath);
        MigrateLegacyCsv(logPath);
        UserSettings settings = LoadUserSettings(args);
        _pollInterval = TimeSpan.FromMilliseconds(settings.PollingIntervalMilliseconds);
        _inactivityThreshold = TimeSpan.FromSeconds(settings.InactivityThresholdSeconds);
        _storeWindowTitles = settings.StoreWindowTitles;
        ActivityRecord? recovered = _database.RecoverIncompleteSession(
            DateTime.Now,
            TimeSpan.FromMinutes(settings.CrashRecoveryMaxMinutes));
        if (recovered is not null)
        {
            Console.WriteLine($"Recovered incomplete session at {recovered.Timestamp:yyyy-MM-dd HH:mm:ss}.");
        }
        if (settings.RetentionDays > 0)
        {
            _database.DeleteBefore(DateTime.Now.AddDays(-settings.RetentionDays));
        }

        using EventWaitHandle? pauseEvent = OpenPauseEvent(args);
        bool initiallyPaused = pauseEvent?.WaitOne(0) == true || (pauseEvent is null && settings.StartTrackingPaused);
        if (!initiallyPaused)
        {
            StartSession();
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };

        Task pollingTask = PollAsync(pauseEvent, initiallyPaused, cancellation.Token);

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

            EndSession();
            lock (LogLock)
            {
                _database?.Dispose();
                _database = null;
            }
        }
    }

    private static UserSettings LoadUserSettings(string[] args)
    {
        int optionIndex = Array.FindIndex(args, arg =>
            string.Equals(arg, "--settings", StringComparison.OrdinalIgnoreCase));
        string path = optionIndex >= 0 && optionIndex + 1 < args.Length
            ? args[optionIndex + 1]
            : Path.Combine(AppContext.BaseDirectory, "WindowLogger.settings.json");
        return UserSettingsStore.Load(path);
    }

    private static EventWaitHandle? OpenPauseEvent(string[] args)
    {
        int eventOptionIndex = Array.FindIndex(args, arg =>
            string.Equals(arg, "--pause-event", StringComparison.OrdinalIgnoreCase));

        return eventOptionIndex >= 0 && eventOptionIndex + 1 < args.Length
            ? EventWaitHandle.OpenExisting(args[eventOptionIndex + 1])
            : null;
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

    private static async Task PollAsync(
        EventWaitHandle? pauseEvent,
        bool initiallyPaused,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_pollInterval);
        bool wasPaused = initiallyPaused;

        do
        {
            try
            {
                bool isPaused = pauseEvent?.WaitOne(0) ?? initiallyPaused;
                if (isPaused)
                {
                    if (!wasPaused)
                    {
                        EndSession();
                    }
                }
                else if (wasPaused)
                {
                    StartSession();
                    CaptureCurrentWindowSnapshot();
                }
                else
                {
                    PollActiveWindow();
                }

                wasPaused = isPaused;
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
        string displayName = _storeWindowTitles
            ? ActivityCapturedValue.Format(windowTitle, executableName)
            : ActivityCapturedValue.Format(string.Empty, executableName);
        WriteRecord(displayName, status);
    }

    private static void WriteSessionMarker(string status) => WriteRecord(string.Empty, status);

    private static void StartSession()
    {
        if (_sessionOpen)
        {
            return;
        }

        WriteSessionMarker("SessionStart");
        _sessionOpen = true;
    }

    private static void EndSession()
    {
        if (!_sessionOpen)
        {
            return;
        }

        WriteSessionMarker("SessionEnd");
        _sessionOpen = false;
    }

    private static void WriteRecord(string windowTitle, string status)
    {
        ActivityRecord record = ActivityRecord.Create(DateTime.Now, windowTitle, status);
        Console.WriteLine($"{record.Timestamp:yyyy-MM-dd HH:mm:ss},{record.WindowTitle},{record.Status}");
        lock (LogLock)
        {
            _database?.Append(record);
        }
    }

    private static void MigrateLegacyCsv(string databasePath)
    {
        if (_database is null || _database.Count != 0)
        {
            return;
        }

        string legacyCsvPath = Path.ChangeExtension(databasePath, ".csv");
        if (!File.Exists(legacyCsvPath))
        {
            return;
        }

        int imported = _database.Import(ActivityCsv.Read(legacyCsvPath, Console.Error.WriteLine));
        if (imported == 0)
        {
            return;
        }

        string archivePath = Path.Combine(
            Path.GetDirectoryName(legacyCsvPath) ?? string.Empty,
            $"{Path.GetFileNameWithoutExtension(legacyCsvPath)}.migrated-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
        File.Move(legacyCsvPath, archivePath);
        Console.WriteLine($"Imported {imported} legacy CSV records and archived the source as {archivePath}.");
    }

    private static void PollActiveWindow()
    {
        bool isInactive = UserActivityDetector.IsUserInactive(_inactivityThreshold);

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

    private static void CaptureCurrentWindowSnapshot()
    {
        _inactive = UserActivityDetector.IsUserInactive(_inactivityThreshold);
        if (!TryGetActiveWindow(out WindowInfo currentWindow))
        {
            _lastWindowTitle = string.Empty;
            _lastExecutableName = string.Empty;
            _lastProcessId = 0;
            return;
        }

        UpdateLastWindow(currentWindow);
        WriteLog(_lastWindowTitle, _lastExecutableName, _inactive ? "Inactive" : "Active");
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
