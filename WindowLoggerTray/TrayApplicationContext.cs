using System.Diagnostics;

namespace WindowLoggerTray;

public class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private Process? _loggerProcess;
    private EventWaitHandle? _loggerStopEvent;
    
    // 1. Executables: Located in their respective project build folders
    private string LoggerExe => FindComponentPath("WindowLogger", "net10.0-windows", "WindowLogger.exe");
    private string AnalyserExe => FindComponentPath("WindowAnalyser", "net10.0", "WindowAnalyser.exe");
    private string ConfigGuiExe => FindComponentPath("WindowLoggerConfigGui", "net10.0-windows", "WindowLoggerConfigGui.exe");
    
    // 2. Data File (CSV): Located in the WindowLogger directory (producer owns the data)
    private string LogFile => Path.Combine(Path.GetDirectoryName(LoggerExe) ?? string.Empty, "WindowLogger.csv");
    
    // 3. Config File: Located in the WindowAnalyser directory
    private string ConfigFile => Path.Combine(Path.GetDirectoryName(AnalyserExe) ?? string.Empty, "appsettings.json");

    // 4. Report File: Generated in the Tray directory (for easy user access)
    private string ReportFile => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Report.xlsx");

    // Menu items
    private ToolStripMenuItem _startLoggingItem = null!;
    private ToolStripMenuItem _stopLoggingItem = null!;

    public TrayApplicationContext()
    {
        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application, 
            Visible = true,
            Text = "Window Logger Controller"
        };

        _notifyIcon.ContextMenuStrip = CreateContextMenu();
        
        CheckExistingProcess();
    }
    
    private string FindComponentPath(string projectName, string framework, string fileName)
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        // Prefer the project build output (development mode) if it exists
        string config = baseDir.Contains("Release") ? "Release" : "Debug";
        string solutionDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));
        string projectPath = Path.Combine(solutionDir, projectName, "bin", config, framework, fileName);
        if (File.Exists(projectPath)) return projectPath;

        // Fallback to a local copy in the tray folder
        string localPath = Path.Combine(baseDir, fileName);
        if (File.Exists(localPath)) return localPath;

        // Last-resort: return the project path even if missing (caller may show helpful errors)
        return projectPath;
    }

    private ContextMenuStrip CreateContextMenu()
    {
        var menu = new ContextMenuStrip();

        _startLoggingItem = new ToolStripMenuItem("Start Logging", null, (s, e) => StartLogger());
        _stopLoggingItem = new ToolStripMenuItem("Stop Logging", null, (s, e) => StopLogger());
        
        menu.Items.Add(_startLoggingItem);
        menu.Items.Add(_stopLoggingItem);
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("Generate Report & Open", null, async (s, e) => await RunAnalysisAsync());
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("Edit Configuration (GUI)", null, (s, e) => OpenConfigGui());
        menu.Items.Add("Edit Configuration (JSON)", null, (s, e) => OpenConfigJson());
        
        var clearDataMenu = new ToolStripMenuItem("Clear Collected Data");
        clearDataMenu.Click += (s, e) => ClearData();
        menu.Items.Add(clearDataMenu);
        
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (s, e) => Exit());

        UpdateMenuState();
        return menu;
    }

    private void StartLogger()
    {
        if (_loggerProcess != null && !_loggerProcess.HasExited) return;

        if (!File.Exists(LoggerExe))
        {
            ShowError($"Could not find {LoggerExe}.");
            return;
        }

        try
        {
            _loggerProcess?.Dispose();
            _loggerProcess = null;
            _loggerStopEvent?.Dispose();

            string stopEventName = $"Local\\WindowLogger-{Guid.NewGuid():N}";
            _loggerStopEvent = new EventWaitHandle(
                initialState: false,
                EventResetMode.ManualReset,
                stopEventName);

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

            _loggerProcess = Process.Start(startInfo);
            if (_loggerProcess is null)
            {
                throw new InvalidOperationException("The logger process could not be created.");
            }
            _notifyIcon.ShowBalloonTip(3000, "Window Logger", "Logging started.", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            _loggerStopEvent?.Dispose();
            _loggerStopEvent = null;
            ShowError($"Failed to start logger: {ex.Message}");
        }

        UpdateMenuState();
    }

    private void StopLogger()
    {
        if (_loggerProcess == null)
        {
            return;
        }

        try
        {
            if (!_loggerProcess.HasExited)
            {
                _loggerStopEvent?.Set();
                if (!_loggerProcess.WaitForExit(3000))
                {
                    _loggerProcess.Kill(entireProcessTree: true);
                    _loggerProcess.WaitForExit(1000);
                }
            }

            _notifyIcon.ShowBalloonTip(3000, "Window Logger", "Logging stopped.", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            ShowError($"Failed to stop logger: {ex.Message}");
        }
        finally
        {
            _loggerProcess.Dispose();
            _loggerProcess = null;
            _loggerStopEvent?.Dispose();
            _loggerStopEvent = null;
        }

        UpdateMenuState();
    }

    private async Task RunAnalysisAsync()
    {
        if (!File.Exists(AnalyserExe))
        {
            ShowError($"{AnalyserExe} not found.");
            return;
        }

        // Logic check: Verify if the CSV exists in the REMOTE (Logger) folder
        if (!File.Exists(LogFile))
        {
            ShowError($"No log file found at:\n{LogFile}\n\nPlease run the logger first to generate data.");
            return;
        }

        try
        {
            if (File.Exists(ReportFile))
            {
                File.Delete(ReportFile);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = AnalyserExe,
                UseShellExecute = false,
                CreateNoWindow = true,
                // Analyser needs to run in its own folder to find appsettings.json
                WorkingDirectory = Path.GetDirectoryName(AnalyserExe) 
            };

            startInfo.ArgumentList.Add(LogFile);
            startInfo.ArgumentList.Add(ReportFile);

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("The analyser process could not be created.");
            await process.WaitForExitAsync();

            if (process.ExitCode == 0 && File.Exists(ReportFile))
            {
                Process.Start(new ProcessStartInfo(ReportFile) { UseShellExecute = true });
            }
            else
            {
                ShowError($"Report generation failed with exit code {process.ExitCode}.");
            }
        }
        catch (Exception ex)
        {
            ShowError($"Analysis failed: {ex.Message}");
        }
    }

    private void OpenConfigGui()
    {
        if (File.Exists(ConfigGuiExe))
        {
            var startInfo = new ProcessStartInfo(ConfigGuiExe) { UseShellExecute = true };
            startInfo.ArgumentList.Add(ConfigFile);
            Process.Start(startInfo);
        }
        else
        {
            ShowError($"{ConfigGuiExe} not found.");
        }
    }

    private void OpenConfigJson()
    {
        if (File.Exists(ConfigFile))
        {
            Process.Start(new ProcessStartInfo(ConfigFile) { UseShellExecute = true });
        }
        else
        {
            ShowError($"{ConfigFile} not found.");
        }
    }

    private void ClearData()
    {
        if (MessageBox.Show($"Are you sure you want to delete the log file?\nTarget: {LogFile}", 
            "Clear Data", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
        {
            try
            {
                bool wasRunning = _loggerProcess != null && !_loggerProcess.HasExited;
                if (wasRunning) StopLogger();

                if (File.Exists(LogFile)) File.Delete(LogFile);

                if (wasRunning) StartLogger();
                
                _notifyIcon.ShowBalloonTip(3000, "Data Cleared", "Log file has been deleted.", ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                ShowError($"Failed to clear data: {ex.Message}");
            }
        }
    }

    private void CheckExistingProcess()
    {
        var existing = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(LoggerExe));
        if (existing.Length > 0)
        {
            _loggerProcess = existing[0];
        }
        foreach (Process process in existing.Skip(1))
        {
            process.Dispose();
        }
        UpdateMenuState();
    }

    private void UpdateMenuState()
    {
        bool isRunning = _loggerProcess != null && !_loggerProcess.HasExited;
        _startLoggingItem.Enabled = !isRunning;
        _stopLoggingItem.Enabled = isRunning;
    }

    private void ShowError(string message)
    {
        MessageBox.Show(message, "Window Logger Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void Exit()
    {
        if (_loggerProcess != null && !_loggerProcess.HasExited)
        {
            StopLogger();
        }
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        Application.Exit();
    }
}

