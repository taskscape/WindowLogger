using System.Diagnostics;
using System.Reflection;

namespace WindowLoggerTray;

public class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private Process? _loggerProcess;
    
    private const string LoggerProcessName = "WindowLogger";

    // 1. Executables
    private string LoggerExe => FindComponentPath("WindowLogger", "net10.0", "WindowLogger.exe");
    private string AnalyserExe => FindComponentPath("WindowAnalyser", "net10.0", "WindowAnalyser.exe");
    private string ConfigGuiExe => FindComponentPath("WindowLoggerConfigGui", "net48", "WindowLoggerConfigGui.exe");
    
    // 2. Data File (Input): Read from %LocalAppData%\WindowLogger
    private string LogFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
        "WindowLogger", 
        "WindowLogger.csv");
    
    // 3. Config File (per-machine, writable)
    private string ConfigFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "WindowLogger",
        "appsettings.json");

    // 4. Report File (Output): Documents\WindowLogger\Report-yymmdd-HHmmss.xlsx
    private string ReportFile
    {
        get
        {
            string docsDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string appDir = Path.Combine(docsDir, "WindowLogger");
            string fileName = $"Report-{DateTime.Now:yyMMdd-HHmmss}.xlsx";
            return Path.Combine(appDir, fileName);
        }
    }

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
        
        // Timer to periodically check if logger is alive (updates the icon menu state)
        var timer = new System.Windows.Forms.Timer();
        timer.Interval = 2000; // Check every 2 seconds
        timer.Tick += (s, e) => CheckProcessState();
        timer.Start();

        CheckProcessState();
    }
    
    private string FindComponentPath(string projectName, string framework, string fileName)
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string config = baseDir.Contains("Release") ? "Release" : "Debug";
        string solutionDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));
        string projectPath = Path.Combine(solutionDir, projectName, "bin", config, framework, fileName);
        
        if (File.Exists(projectPath)) return projectPath;

        string localPath = Path.Combine(baseDir, fileName);
        if (File.Exists(localPath)) return localPath;

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

        menu.Items.Add("Generate Report & Open", null, (s, e) => RunAnalysis());
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("Edit Configuration (GUI)", null, (s, e) => OpenConfigGui());
        menu.Items.Add("Edit Configuration (JSON)", null, (s, e) => OpenConfigJson());
        
        var clearDataMenu = new ToolStripMenuItem("Clear Collected Data");
        clearDataMenu.Click += (s, e) => ClearData();
        menu.Items.Add(clearDataMenu);
        
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (s, e) => Exit());

        return menu; // State is updated by Timer
    }

    private void StartLogger()
    {
        // 1. Double check if already running to prevent duplicates
        if (Process.GetProcessesByName(LoggerProcessName).Length > 0)
        {
            _notifyIcon.ShowBalloonTip(1000, "Window Logger", "Logger is already running.", ToolTipIcon.Info);
            CheckProcessState();
            return;
        }

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
                WorkingDirectory = Path.GetDirectoryName(LoggerExe)
            };

            startInfo.Arguments = $"\"{LogFile}\"";

            Process.Start(startInfo);
            _notifyIcon.ShowBalloonTip(3000, "Window Logger", "Logging started.", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            ShowError($"Failed to start logger: {ex.Message}");
        }

        CheckProcessState();
    }

    private void StopLogger()
    {
        // FORCE KILL: Instead of relying on a variable, we ask Windows to kill the process by name.
        // This solves issues where the Tray app lost the reference to the process.
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "taskkill",
                Arguments = $"/F /IM {LoggerProcessName}.exe",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            var proc = Process.Start(psi);
            proc?.WaitForExit();

            _notifyIcon.ShowBalloonTip(3000, "Window Logger", "Logging stopped.", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            ShowError($"Failed to stop logger: {ex.Message}");
        }

        CheckProcessState();
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
            ShowError($"No log file found at:\n{LogFile}\n\nPlease run the logger first to generate data.");
            return;
        }

        string reportPath = ReportFile; // capture once to avoid timestamp drift
        try
        {
            // Ensure output directory exists in Documents
            string? reportDir = Path.GetDirectoryName(reportPath);
            if (!string.IsNullOrEmpty(reportDir) && !Directory.Exists(reportDir))
            {
                Directory.CreateDirectory(reportDir);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = AnalyserExe,
                Arguments = $"\"{LogFile}\" \"{reportPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(AnalyserExe) 
            };

            var process = Process.Start(startInfo);
            process?.WaitForExit();

            if (File.Exists(reportPath))
            {
                new Process { StartInfo = new ProcessStartInfo(reportPath) { UseShellExecute = true } }.Start();
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
            var startInfo = new ProcessStartInfo(ConfigGuiExe)
            {
                UseShellExecute = true,
                Arguments = $"\"{ConfigFile}\""
            };
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
                // Force stop before deleting
                StopLogger();
                
                // Slight delay to ensure file handle is released
                Thread.Sleep(500);

                if (File.Exists(LogFile)) File.Delete(LogFile);

                // Restart
                StartLogger();
                
                _notifyIcon.ShowBalloonTip(3000, "Data Cleared", "Log file has been deleted.", ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                ShowError($"Failed to clear data: {ex.Message}");
            }
        }
    }

    private void CheckProcessState()
    {
        var existing = Process.GetProcessesByName(LoggerProcessName);
        bool isRunning = existing.Length > 0;
        
        // Safely update UI on the UI thread
        if (_startLoggingItem != null && _stopLoggingItem != null)
        {
            _startLoggingItem.Enabled = !isRunning;
            _stopLoggingItem.Enabled = isRunning;
        }
    }

    private void ShowError(string message)
    {
        MessageBox.Show(message, "Window Logger Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void Exit()
    {
        // Optional: Stop logger on exit?
        // StopLogger(); 
        
        _notifyIcon.Visible = false;
        Application.Exit();
    }
}

