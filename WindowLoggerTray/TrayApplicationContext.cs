using System.Diagnostics;
using System.Reflection;

namespace WindowLoggerTray;

public class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private Process? _loggerProcess;
    
    // 1. Executables: Located in their respective project build folders
    private string LoggerExe => FindComponentPath("WindowLogger", "net10.0", "WindowLogger.exe");
    private string AnalyserExe => FindComponentPath("WindowAnalyser", "net10.0", "WindowAnalyser.exe");
    private string ConfigGuiExe => FindComponentPath("WindowLoggerConfigGui", "net48", "WindowLoggerConfigGui.exe");
    
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
        
        // Check Local (if all files are copied to one folder)
        string localPath = Path.Combine(baseDir, fileName);
        if (File.Exists(localPath)) return localPath;

        // Check Project Structure (Development mode)
        // Go up from: WindowLoggerTray/bin/Debug/net10.0-windows/
        string config = baseDir.Contains("Release") ? "Release" : "Debug";
        string solutionDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));
        
        return Path.Combine(solutionDir, projectName, "bin", config, framework, fileName);
    }

    private ContextMenuStrip CreateContextMenu()
    {
        var menu = new ContextMenuStrip();

        _startLoggingItem = new ToolStripMenuItem("Start Logging", null, (s, e) => StartLogger());
        _stopLoggingItem = new ToolStripMenuItem("Stop Logging", null, (s, e) => StopLogger());
        
        menu.Items.Add(_startLoggingItem);
        menu.Items.Add(_stopLoggingItem);
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("Generate Report & Open", null, (s, e) => RunAnalysis());
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
            var startInfo = new ProcessStartInfo
            {
                FileName = LoggerExe,
                UseShellExecute = false,
                CreateNoWindow = true,
                // CRITICAL: Set WorkingDirectory to the LOGGER'S folder.
                // This ensures WindowLogger.csv is created in WindowLogger/bin/..., not in Tray folder.
                WorkingDirectory = Path.GetDirectoryName(LoggerExe)
            };

            _loggerProcess = Process.Start(startInfo);
            _notifyIcon.ShowBalloonTip(3000, "Window Logger", "Logging started.", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            ShowError($"Failed to start logger: {ex.Message}");
        }

        UpdateMenuState();
    }

    private void StopLogger()
    {
        if (_loggerProcess == null || _loggerProcess.HasExited) return;

        try
        {
            _loggerProcess.Kill();
            _loggerProcess.WaitForExit(1000);
            _loggerProcess = null;
            _notifyIcon.ShowBalloonTip(3000, "Window Logger", "Logging stopped.", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            ShowError($"Failed to stop logger: {ex.Message}");
        }

        UpdateMenuState();
    }

    private void RunAnalysis()
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
            var startInfo = new ProcessStartInfo
            {
                FileName = AnalyserExe,
                // Arguments: [Input: Path to Logger's CSV] [Output: Path to Tray's Report]
                Arguments = $"\"{LogFile}\" \"{ReportFile}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                // Analyser needs to run in its own folder to find appsettings.json
                WorkingDirectory = Path.GetDirectoryName(AnalyserExe) 
            };

            var process = Process.Start(startInfo);
            process?.WaitForExit();

            if (File.Exists(ReportFile))
            {
                new Process { StartInfo = new ProcessStartInfo(ReportFile) { UseShellExecute = true } }.Start();
            }
            else
            {
                ShowError("Report file was not created. The log file might be empty.");
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
            Process.Start(new ProcessStartInfo(ConfigGuiExe) { UseShellExecute = true });
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
        Application.Exit();
    }
}
