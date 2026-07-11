using System.Diagnostics;
using System.IO;
using System.Windows.Controls;
using Wpf = System.Windows;

namespace WindowLoggerTray;

public sealed class TrayController : IDisposable
{
    private readonly TrayIcon _trayIcon;
    private readonly MenuItem _startLoggingItem;
    private readonly MenuItem _stopLoggingItem;
    private readonly MenuItem _generateReportItem;
    private Process? _loggerProcess;
    private EventWaitHandle? _loggerStopEvent;
    private bool _ownsLoggerProcess;
    private bool _analysisInProgress;
    private bool _disposed;

    private string LoggerExe => FindComponentPath("WindowLogger", "WindowLogger.exe");
    private string AnalyserExe => FindComponentPath("WindowAnalyser", "WindowAnalyser.exe");
    private string ConfigGuiExe => FindComponentPath("WindowLoggerConfigGui", "WindowLoggerConfigGui.exe");
    private string LogFile => Path.Combine(Path.GetDirectoryName(LoggerExe) ?? string.Empty, "WindowLogger.csv");
    private string ConfigFile => Path.Combine(Path.GetDirectoryName(AnalyserExe) ?? string.Empty, "appsettings.json");
    private string ReportFile => Path.Combine(AppContext.BaseDirectory, "Report.xlsx");

    public TrayController()
    {
        _startLoggingItem = CreateMenuItem("Start Logging", (_, _) => StartLogger());
        _stopLoggingItem = CreateMenuItem("Stop Logging", async (_, _) => await StopLoggerAsync());
        _generateReportItem = CreateMenuItem("Generate Report & Open", async (_, _) => await RunAnalysisAsync());

        ContextMenu contextMenu = CreateContextMenu();
        contextMenu.Opened += (_, _) => UpdateMenuState();
        _trayIcon = new TrayIcon("Window Logger Controller", contextMenu);
        CheckExistingProcess();
    }

    private ContextMenu CreateContextMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(_startLoggingItem);
        menu.Items.Add(_stopLoggingItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(_generateReportItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("Edit Configuration (GUI)", (_, _) => OpenConfigGui()));
        menu.Items.Add(CreateMenuItem("Edit Configuration (JSON)", (_, _) => OpenConfigJson()));
        menu.Items.Add(CreateMenuItem("Clear Collected Data", async (_, _) => await ClearDataAsync()));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("Exit", (_, _) => Wpf.Application.Current.Shutdown()));
        return menu;
    }

    private static MenuItem CreateMenuItem(string header, Wpf.RoutedEventHandler clickHandler)
    {
        var item = new MenuItem { Header = header };
        item.Click += clickHandler;
        return item;
    }

    private static string FindComponentPath(string projectName, string fileName)
    {
        string baseDirectory = AppContext.BaseDirectory;
        string localPath = Path.Combine(baseDirectory, fileName);
        if (File.Exists(localPath))
        {
            return localPath;
        }

        string configuration = baseDirectory.Contains("Release", StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";
        string solutionDirectory = Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", ".."));
        string buildDirectory = Path.Combine(solutionDirectory, projectName, "bin", configuration);
        if (Directory.Exists(buildDirectory))
        {
            string? projectPath = Directory
                .EnumerateFiles(buildDirectory, fileName, SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (projectPath is not null)
            {
                return projectPath;
            }
        }

        return localPath;
    }

    private void StartLogger()
    {
        if (IsLoggerRunning())
        {
            return;
        }

        if (!File.Exists(LoggerExe))
        {
            ShowError($"Could not find {LoggerExe}.");
            return;
        }

        try
        {
            DisposeLoggerHandles();

            string stopEventName = $"Local\\WindowLogger-{Guid.NewGuid():N}";
            _loggerStopEvent = new EventWaitHandle(false, EventResetMode.ManualReset, stopEventName);

            var startInfo = new ProcessStartInfo
            {
                FileName = LoggerExe,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(LoggerExe)
            };
            startInfo.ArgumentList.Add(LogFile);
            startInfo.ArgumentList.Add("--stop-event");
            startInfo.ArgumentList.Add(stopEventName);

            _loggerProcess = Process.Start(startInfo)
                ?? throw new InvalidOperationException("The logger process could not be created.");
            _ownsLoggerProcess = true;
            _trayIcon.ShowBalloon("Window Logger", "Logging started.");
        }
        catch (Exception ex)
        {
            DisposeLoggerHandles();
            ShowError($"Failed to start logger: {ex.Message}");
        }

        UpdateMenuState();
    }

    private async Task StopLoggerAsync(bool showNotification = true)
    {
        if (_loggerProcess is null)
        {
            return;
        }

        try
        {
            if (!_loggerProcess.HasExited)
            {
                _loggerStopEvent?.Set();
                if (!await WaitForExitAsync(_loggerProcess, TimeSpan.FromSeconds(3)))
                {
                    _loggerProcess.Kill(entireProcessTree: true);
                    await WaitForExitAsync(_loggerProcess, TimeSpan.FromSeconds(1));
                }
            }

            if (showNotification)
            {
                _trayIcon.ShowBalloon("Window Logger", "Logging stopped.");
            }
        }
        catch (Exception ex)
        {
            if (showNotification)
            {
                ShowError($"Failed to stop logger: {ex.Message}");
            }
        }
        finally
        {
            DisposeLoggerHandles();
        }

        UpdateMenuState();
    }

    private async Task RunAnalysisAsync()
    {
        if (_analysisInProgress)
        {
            return;
        }

        _analysisInProgress = true;
        UpdateMenuState();

        try
        {
            if (!File.Exists(AnalyserExe))
            {
                ShowError($"{AnalyserExe} not found.");
                return;
            }

            if (!File.Exists(LogFile))
            {
                ShowError($"No log file found at:\n{LogFile}\n\nPlease run the logger first to generate data.");
                return;
            }

            if (File.Exists(ReportFile))
            {
                File.Delete(ReportFile);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = AnalyserExe,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(AnalyserExe)
            };
            startInfo.ArgumentList.Add(LogFile);
            startInfo.ArgumentList.Add(ReportFile);

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("The analyser process could not be created.");
            Task<string> errorOutputTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            string errorOutput = (await errorOutputTask).Trim();

            if (process.ExitCode != 0 || !File.Exists(ReportFile))
            {
                string details = string.IsNullOrWhiteSpace(errorOutput) ? string.Empty : $"\n\n{errorOutput}";
                ShowError($"Report generation failed with exit code {process.ExitCode}.{details}");
                return;
            }

            Process.Start(new ProcessStartInfo(ReportFile) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowError($"Analysis failed: {ex.Message}");
        }
        finally
        {
            _analysisInProgress = false;
            UpdateMenuState();
        }
    }

    private void OpenConfigGui()
    {
        if (!File.Exists(ConfigGuiExe))
        {
            ShowError($"{ConfigGuiExe} not found.");
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo(ConfigGuiExe) { UseShellExecute = true };
            startInfo.ArgumentList.Add(ConfigFile);
            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            ShowError($"Failed to open the configuration editor: {ex.Message}");
        }
    }

    private void OpenConfigJson()
    {
        if (!File.Exists(ConfigFile))
        {
            ShowError($"{ConfigFile} not found.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(ConfigFile) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowError($"Failed to open the configuration file: {ex.Message}");
        }
    }

    private async Task ClearDataAsync()
    {
        Wpf.MessageBoxResult result = Wpf.MessageBox.Show(
            $"Are you sure you want to delete the log file?\nTarget: {LogFile}",
            "Clear Data",
            Wpf.MessageBoxButton.YesNo,
            Wpf.MessageBoxImage.Warning);
        if (result != Wpf.MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            bool wasRunning = IsLoggerRunning();
            if (wasRunning)
            {
                await StopLoggerAsync(showNotification: false);
            }

            if (File.Exists(LogFile))
            {
                File.Delete(LogFile);
            }

            if (wasRunning)
            {
                StartLogger();
            }

            _trayIcon.ShowBalloon("Data Cleared", "Log file has been deleted.");
        }
        catch (Exception ex)
        {
            ShowError($"Failed to clear data: {ex.Message}");
        }
    }

    private void CheckExistingProcess()
    {
        Process[] existing = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(LoggerExe));
        if (existing.Length > 0)
        {
            _loggerProcess = existing[0];
            _ownsLoggerProcess = false;
        }

        foreach (Process process in existing.Skip(1))
        {
            process.Dispose();
        }

        UpdateMenuState();
    }

    private bool IsLoggerRunning()
    {
        if (_loggerProcess is null)
        {
            return false;
        }

        try
        {
            return !_loggerProcess.HasExited;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private void UpdateMenuState()
    {
        bool isRunning = IsLoggerRunning();
        _startLoggingItem.IsEnabled = !isRunning;
        _stopLoggingItem.IsEnabled = isRunning;
        _generateReportItem.IsEnabled = !_analysisInProgress;
    }

    private static void ShowError(string message) =>
        Wpf.MessageBox.Show(message, "Window Logger Error", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Error);

    private void DisposeLoggerHandles()
    {
        _loggerProcess?.Dispose();
        _loggerProcess = null;
        _loggerStopEvent?.Dispose();
        _loggerStopEvent = null;
        _ownsLoggerProcess = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_ownsLoggerProcess && IsLoggerRunning())
        {
            _loggerStopEvent?.Set();
        }
        DisposeLoggerHandles();

        _trayIcon.Dispose();
    }

    private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cancellation.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
