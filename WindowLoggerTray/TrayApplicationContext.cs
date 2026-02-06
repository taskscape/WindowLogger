using System.Diagnostics;
using System.Reflection;

namespace WindowLoggerTray;

public class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private Process? _loggerProcess;
    
    // Executable names (located in application directory)
    private string LoggerExe => FindComponentPath("WindowLogger", "net10.0", "WindowLogger.exe");
    private string AnalyserExe => FindComponentPath("WindowAnalyser", "net10.0", "WindowAnalyser.exe");
    private string ConfigGuiExe => FindComponentPath("WindowLoggerConfigGui", "net48", "WindowLoggerConfigGui.exe");
    private string LogFile => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WindowLogger.csv");
    private string ReportFile => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Report.xlsx");
    private string ConfigFile => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

    // Menu items
    private ToolStripMenuItem _startLoggingItem = null!;
    private ToolStripMenuItem _stopLoggingItem = null!;

    public TrayApplicationContext()
    {
        _notifyIcon = new NotifyIcon
        {
            // Use the application icon; replace with a custom .ico if desired
            Icon = SystemIcons.Application, 
            Visible = true,
            Text = "Window Logger Controller"
        };

        _notifyIcon.ContextMenuStrip = CreateContextMenu();
        
        // Check if the logger process is already running
        CheckExistingProcess();
    }
    
    private string FindComponentPath(string projectName, string framework, string fileName)
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        
        // 1. Check if the file exists in the current directory (Production/Copied mode)
        string localPath = Path.Combine(baseDir, fileName);
        if (File.Exists(localPath)) return localPath;

        // 2. If not found, try to locate it in the source project build folders (Development mode)
        // Expected structure: Solution/ProjectName/bin/Configuration/Framework/File.exe
        // Tray location: Solution/WindowLoggerTray/bin/Configuration/Framework/
        
        // Detect configuration (Debug/Release) based on current path
        string config = baseDir.Contains("Release") ? "Release" : "Debug";

        // Navigate 4 levels up to the Solution directory
        string solutionDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));
        
        string devPath = Path.Combine(solutionDir, projectName, "bin", config, framework, fileName);
        
        // Return the development path (even if missing, it will be handled by the caller)
        return devPath;
    }

    private ContextMenuStrip CreateContextMenu()
    {
        var menu = new ContextMenuStrip();

        // Logger control
        _startLoggingItem = new ToolStripMenuItem("Start Logging", null, (s, e) => StartLogger());
        _stopLoggingItem = new ToolStripMenuItem("Stop Logging", null, (s, e) => StopLogger());
        
        menu.Items.Add(_startLoggingItem);
        menu.Items.Add(_stopLoggingItem);
        menu.Items.Add(new ToolStripSeparator());

        // Analysis
        menu.Items.Add("Generate Report & Open", null, (s, e) => RunAnalysis());
        menu.Items.Add(new ToolStripSeparator());

        // Configuration
        menu.Items.Add("Edit Configuration (GUI)", null, (s, e) => OpenConfigGui());
        menu.Items.Add("Edit Configuration (JSON)", null, (s, e) => OpenConfigJson());
        
        // Data
        var clearDataMenu = new ToolStripMenuItem("Clear Collected Data");
        clearDataMenu.Click += (s, e) => ClearData();
        menu.Items.Add(clearDataMenu);
        
        menu.Items.Add(new ToolStripSeparator());

        // Exit
        menu.Items.Add("Exit", null, (s, e) => Exit());

        UpdateMenuState();
        return menu;
    }

    private void StartLogger()
    {
        if (_loggerProcess != null && !_loggerProcess.HasExited) return;

        if (!File.Exists(LoggerExe))
        {
            ShowError($"Could not find {LoggerExe} in the application directory.\nPath: {AppDomain.CurrentDomain.BaseDirectory}");
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = LoggerExe,
                UseShellExecute = false,
                CreateNoWindow = true, // Run with a hidden console window
                WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
            };

            _loggerProcess = Process.Start(startInfo);
            _notifyIcon.ShowBalloonTip(3000, "Window Logger", "Logging started in background.", ToolTipIcon.Info);
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
            // Terminate the process; logger flushes output on exit.
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

        if (!File.Exists(LogFile))
        {
            ShowError("No log file found to analyze. Please run the logger first.");
            return;
        }

        try
        {
            // Run analyzer and open the generated report
            var startInfo = new ProcessStartInfo
            {
                FileName = AnalyserExe,
                Arguments = $"\"{LogFile}\" \"{ReportFile}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(AnalyserExe)
            };

            var process = Process.Start(startInfo);
            process?.WaitForExit();

            // Open the generated report
            if (File.Exists(ReportFile))
            {
                new Process
                {
                    StartInfo = new ProcessStartInfo(ReportFile) { UseShellExecute = true }
                }.Start();
            }
            else
            {
                ShowError("Report file was not created. Check if the log file is empty.");
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
    string configPath = ConfigFile;
    
    if (!File.Exists(configPath))
    {
         string analyserDir = Path.GetDirectoryName(AnalyserExe) ?? string.Empty;
         string altConfig = Path.Combine(analyserDir, "appsettings.json");
         if (File.Exists(altConfig)) configPath = altConfig;
    }

    if (File.Exists(configPath))
    {
        Process.Start(new ProcessStartInfo(configPath) { UseShellExecute = true });
    }
    else
    {
        ShowError($"appsettings.json not found.");
    }
}

    private void ClearData()
    {
        if (MessageBox.Show("Are you sure you want to delete the log file? This cannot be undone.", 
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
        // Detect any running logger process by name
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
        // Stop logger on exit
        if (_loggerProcess != null && !_loggerProcess.HasExited)
        {
            StopLogger();
        }
        _notifyIcon.Visible = false;
        Application.Exit();
    }
}
