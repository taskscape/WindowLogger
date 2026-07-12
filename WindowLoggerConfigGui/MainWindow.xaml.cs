using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using WindowLogger.Shared.Activity;
using WindowLogger.Shared.Configuration;
using WindowLogger.Shared.Settings;

namespace WindowLoggerConfigGui;

public partial class MainWindow : Window
{
    private const string DefaultRelativePath = @"..\..\..\..\WindowAnalyser\appsettings.json";

    private readonly string? _initialPath;
    private AppSettings _settings = new();
    private string? _currentPath;
    private bool _isDirty;
    private bool _suppressEditorEvents;
    private int _loadedApplicationIndex = -1;
    private int _loadedExclusionIndex = -1;
    private int _loadedCategoryIndex = -1;
    private readonly DispatcherTimer _dashboardTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private UserSettings _userSettings = new();
    private Process? _loggerProcess;
    private EventWaitHandle? _loggerStopEvent;
    private EventWaitHandle? _loggerPauseEvent;
    private bool _ownsLogger;
    private bool _isPaused;

    private string LoggerExe => FindComponentPath("WindowLogger", "WindowLogger.exe");
    private string AnalyserExe => FindComponentPath("WindowAnalyser", "WindowAnalyser.exe");
    private string DatabaseFile => Path.Combine(Path.GetDirectoryName(LoggerExe) ?? AppContext.BaseDirectory, "WindowLogger.db");
    private string UserSettingsFile => Path.Combine(Path.GetDirectoryName(LoggerExe) ?? AppContext.BaseDirectory, "WindowLogger.settings.json");

    public MainWindow(string? initialPath = null)
    {
        _initialPath = initialPath;
        InitializeComponent();
        foreach (TextBox editor in new[]
                 {
                     AppNameText, AppIncludeText, AppExcludeText, ExclusionIncludeText,
                     CategoryNameText, CategoryIncludeText, CategoryExcludeText
                 })
        {
            editor.TextChanged += EditorField_TextChanged;
        }
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        _dashboardTimer.Tick += (_, _) => RefreshDashboard();
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        TryLoadDefaultConfig();
        LoadUserSettings();
        DetectExistingLogger();
        RefreshDashboard();
        _dashboardTimer.Start();
        if (_userSettings.StartTrackingOnLaunch && !IsLoggerRunning())
        {
            StartTracking();
        }
    }

    private void TryLoadDefaultConfig()
    {
        if (!string.IsNullOrWhiteSpace(_initialPath) && File.Exists(_initialPath))
        {
            LoadConfig(_initialPath);
            return;
        }

        string baseDirectory = AppContext.BaseDirectory;
        string localPath = Path.Combine(baseDirectory, "appsettings.json");
        if (File.Exists(localPath))
        {
            LoadConfig(localPath);
            return;
        }

        string developmentPath = Path.GetFullPath(Path.Combine(baseDirectory, DefaultRelativePath));
        if (File.Exists(developmentPath))
        {
            LoadConfig(developmentPath);
            return;
        }

        _settings = new AppSettings();
        _currentPath = null;
        RefreshAllLists();
        UpdatePathText();
        SetStatus("New configuration (no file loaded).");
    }

    private static string FindComponentPath(string projectName, string fileName)
    {
        string localPath = Path.Combine(AppContext.BaseDirectory, fileName);
        if (File.Exists(localPath))
        {
            return localPath;
        }

        string configuration = AppContext.BaseDirectory.Contains("Release", StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";
        string solutionDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string buildDirectory = Path.Combine(solutionDirectory, projectName, "bin", configuration);
        return Directory.Exists(buildDirectory)
            ? Directory.EnumerateFiles(buildDirectory, fileName, SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault() ?? localPath
            : localPath;
    }

    private void LoadUserSettings()
    {
        try
        {
            bool settingsFileExists = File.Exists(UserSettingsFile);
            _userSettings = UserSettingsStore.Load(UserSettingsFile);
            if (!settingsFileExists)
            {
                _userSettings.LaunchAtWindowsStartup = IsWindowsStartupEnabled();
                UserSettingsStore.Save(UserSettingsFile, _userSettings);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"User settings could not be loaded; defaults will be used.\n{ex.Message}",
                "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            _userSettings = new UserSettings();
        }

        InactivitySecondsText.Text = _userSettings.InactivityThresholdSeconds.ToString();
        PollingMillisecondsText.Text = _userSettings.PollingIntervalMilliseconds.ToString();
        RetentionDaysText.Text = _userSettings.RetentionDays.ToString();
        CrashRecoveryMinutesText.Text = _userSettings.CrashRecoveryMaxMinutes.ToString();
        ReportFolderText.Text = string.IsNullOrWhiteSpace(_userSettings.ReportFolder)
            ? AppContext.BaseDirectory
            : _userSettings.ReportFolder;
        StoreWindowTitlesCheck.IsChecked = _userSettings.StoreWindowTitles;
        StartPausedCheck.IsChecked = _userSettings.StartTrackingPaused;
        StartTrackingOnLaunchCheck.IsChecked = _userSettings.StartTrackingOnLaunch;
        LaunchAtStartupCheck.IsChecked = _userSettings.LaunchAtWindowsStartup;
    }

    private async void SaveUserSettings_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(InactivitySecondsText.Text, out int inactivitySeconds) ||
            !int.TryParse(PollingMillisecondsText.Text, out int pollingMilliseconds) ||
            !int.TryParse(RetentionDaysText.Text, out int retentionDays) ||
            !int.TryParse(CrashRecoveryMinutesText.Text, out int recoveryMinutes))
        {
            ShowWarning("Timing and retention settings must be whole numbers.");
            return;
        }

        bool wasRunning = IsLoggerRunning() && _ownsLogger;
        bool wasPaused = _isPaused;
        _userSettings = new UserSettings
        {
            InactivityThresholdSeconds = inactivitySeconds,
            PollingIntervalMilliseconds = pollingMilliseconds,
            RetentionDays = retentionDays,
            CrashRecoveryMaxMinutes = recoveryMinutes,
            ReportFolder = ReportFolderText.Text.Trim(),
            StoreWindowTitles = StoreWindowTitlesCheck.IsChecked == true,
            StartTrackingPaused = StartPausedCheck.IsChecked == true,
            StartTrackingOnLaunch = StartTrackingOnLaunchCheck.IsChecked == true,
            LaunchAtWindowsStartup = LaunchAtStartupCheck.IsChecked == true
        };
        _userSettings.Normalize();

        try
        {
            UserSettingsStore.Save(UserSettingsFile, _userSettings);
            ApplyWindowsStartupPreference(_userSettings.LaunchAtWindowsStartup);
            if (File.Exists(DatabaseFile) && _userSettings.RetentionDays > 0)
            {
                using var database = new ActivityDatabase(DatabaseFile);
                database.DeleteBefore(DateTime.Now.AddDays(-_userSettings.RetentionDays));
            }

            if (wasRunning)
            {
                await StopTrackingAsync();
                StartTracking(startPaused: wasPaused);
            }
            SetStatus($"Settings saved to {UserSettingsFile}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Settings Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void ApplyWindowsStartupPreference(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled)
        {
            key.SetValue("WindowLogger", $"\"{Environment.ProcessPath}\"");
        }
        else
        {
            key.DeleteValue("WindowLogger", throwOnMissingValue: false);
        }
    }

    private static bool IsWindowsStartupEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        return key?.GetValue("WindowLogger") is string value && !string.IsNullOrWhiteSpace(value);
    }

    private void DetectExistingLogger()
    {
        Process[] existing = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(LoggerExe));
        if (existing.Length > 0)
        {
            _loggerProcess = existing[0];
            _ownsLogger = false;
        }
        foreach (Process process in existing.Skip(1))
        {
            process.Dispose();
        }
    }

    private void StartTracking_Click(object sender, RoutedEventArgs e) => StartTracking();

    private void StartTracking(bool? startPaused = null)
    {
        if (IsLoggerRunning())
        {
            return;
        }
        if (!File.Exists(LoggerExe))
        {
            MessageBox.Show(this, $"Logger not found: {LoggerExe}", "Tracking", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        try
        {
            DisposeLoggerHandles();
            string stopEventName = $"Local\\WindowLogger-Control-{Guid.NewGuid():N}";
            string pauseEventName = $"Local\\WindowLogger-Pause-Control-{Guid.NewGuid():N}";
            bool paused = startPaused ?? _userSettings.StartTrackingPaused;
            _loggerStopEvent = new EventWaitHandle(false, EventResetMode.ManualReset, stopEventName);
            _loggerPauseEvent = new EventWaitHandle(paused, EventResetMode.ManualReset, pauseEventName);
            var startInfo = new ProcessStartInfo(LoggerExe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(LoggerExe)
            };
            foreach (string argument in new[]
                     {
                         DatabaseFile, "--stop-event", stopEventName, "--pause-event", pauseEventName,
                         "--settings", UserSettingsFile
                     })
            {
                startInfo.ArgumentList.Add(argument);
            }
            _loggerProcess = Process.Start(startInfo)
                ?? throw new InvalidOperationException("The logger process could not be started.");
            _ownsLogger = true;
            _isPaused = paused;
            RefreshDashboard();
        }
        catch (Exception ex)
        {
            DisposeLoggerHandles();
            MessageBox.Show(this, ex.Message, "Tracking Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PauseTracking_Click(object sender, RoutedEventArgs e)
    {
        if (!_ownsLogger || !IsLoggerRunning() || _loggerPauseEvent is null)
        {
            return;
        }

        if (_isPaused)
        {
            _loggerPauseEvent.Reset();
            _isPaused = false;
        }
        else
        {
            _loggerPauseEvent.Set();
            _isPaused = true;
        }
        RefreshDashboard();
    }

    private async void StopTracking_Click(object sender, RoutedEventArgs e) => await StopTrackingAsync();

    private async Task StopTrackingAsync()
    {
        if (!_ownsLogger || _loggerProcess is null)
        {
            return;
        }
        try
        {
            _loggerStopEvent?.Set();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            try
            {
                await _loggerProcess.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                _loggerProcess.Kill(entireProcessTree: true);
                await _loggerProcess.WaitForExitAsync();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Tracking Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            DisposeLoggerHandles();
            RefreshDashboard();
        }
    }

    private bool IsLoggerRunning()
    {
        try
        {
            return _loggerProcess is { HasExited: false };
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private void DisposeLoggerHandles()
    {
        _loggerProcess?.Dispose();
        _loggerProcess = null;
        _loggerStopEvent?.Dispose();
        _loggerStopEvent = null;
        _loggerPauseEvent?.Dispose();
        _loggerPauseEvent = null;
        _ownsLogger = false;
        _isPaused = false;
    }

    private void RefreshDashboard_Click(object sender, RoutedEventArgs e) => RefreshDashboard();

    private void RefreshDashboard()
    {
        bool running = IsLoggerRunning();
        TrackingStatusText.Text = !running ? "Stopped" : !_ownsLogger ? "Running (external)" : _isPaused ? "Paused" : "Tracking";
        StartTrackingButton.IsEnabled = !running;
        PauseTrackingButton.IsEnabled = running && _ownsLogger;
        PauseTrackingButton.Content = _isPaused ? "Resume" : "Pause";
        StopTrackingButton.IsEnabled = running && _ownsLogger;

        try
        {
            if (!File.Exists(DatabaseFile))
            {
                RecentActivityList.ItemsSource = Array.Empty<string>();
            }
            else
            {
                using var database = new ActivityDatabase(DatabaseFile, readOnly: true);
                RecentActivityList.ItemsSource = database.ReadRecent(30)
                    .Select(record => $"{record.Timestamp:yyyy-MM-dd HH:mm:ss}  {record.Status,-12} {record.WindowTitle}")
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            RecentActivityList.ItemsSource = new[] { $"Activity unavailable: {ex.Message}" };
        }
    }

    private async void GenerateReport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!File.Exists(DatabaseFile) || !File.Exists(AnalyserExe))
            {
                ShowWarning("Activity data or analyser executable is not available yet.");
                return;
            }
            string reportFolder = string.IsNullOrWhiteSpace(_userSettings.ReportFolder)
                ? AppContext.BaseDirectory
                : _userSettings.ReportFolder;
            Directory.CreateDirectory(reportFolder);
            string reportPath = Path.Combine(reportFolder, $"WindowLogger-Report-{DateTime.Now:yyyyMMdd-HHmmss}.xlsx");
            var startInfo = new ProcessStartInfo(AnalyserExe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(AnalyserExe)
            };
            startInfo.ArgumentList.Add(DatabaseFile);
            startInfo.ArgumentList.Add(reportPath);
            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("The analyser could not be started.");
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(error);
            }
            Process.Start(new ProcessStartInfo(reportPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Report Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RunDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        CommitPendingEdits();
        IEnumerable<string>? titles = null;
        try
        {
            if (File.Exists(DatabaseFile))
            {
                using var database = new ActivityDatabase(DatabaseFile, readOnly: true);
                titles = database.Read()
                    .Where(record => record.Kind == ActivityRecordKind.Usage)
                    .Select(record => record.WindowTitle)
                    .Distinct()
                    .ToArray();
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Diagnostics could not read history: {ex.Message}");
        }

        IReadOnlyList<RuleDiagnostic> diagnostics = ConfigurationDiagnostics.Analyze(_settings, titles);
        DiagnosticsList.ItemsSource = diagnostics.Count == 0
            ? new[] { "No rule problems detected." }
            : diagnostics.Select(item => $"[{item.Severity}] {item.Code}: {item.Message}").ToList();
    }

    private void OpenConfig_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscardChanges())
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            Title = "Open appsettings.json"
        };

        if (!string.IsNullOrWhiteSpace(_currentPath))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(_currentPath);
        }

        if (dialog.ShowDialog(this) == true)
        {
            LoadConfig(dialog.FileName);
        }
    }

    private void SaveConfig_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentPath))
        {
            SaveConfigAs();
            return;
        }

        SaveConfigToPath(_currentPath);
    }

    private void SaveConfigAs_Click(object sender, RoutedEventArgs e) => SaveConfigAs();

    private void ReloadConfig_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentPath) || !ConfirmDiscardChanges())
        {
            return;
        }

        LoadConfig(_currentPath);
    }

    private void SaveConfigAs()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            Title = "Save appsettings.json",
            FileName = "appsettings.json"
        };

        if (!string.IsNullOrWhiteSpace(_currentPath))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(_currentPath);
        }

        if (dialog.ShowDialog(this) == true)
        {
            SaveConfigToPath(dialog.FileName);
        }
    }

    private void LoadConfig(string path)
    {
        try
        {
            string json = File.ReadAllText(path);
            AppSettings settings = AppSettingsJson.Deserialize(json);

            _settings = settings;
            _currentPath = path;
            _isDirty = false;

            RefreshAllLists();
            UpdatePathText();
            SetStatus($"Loaded {path}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to load configuration:\n{ex.Message}", "Load Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveConfigToPath(string path)
    {
        try
        {
            CommitPendingEdits();
            if (!ValidateSettingsForSave())
            {
                return;
            }
            string json = AppSettingsJson.Serialize(_settings);
            File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            _currentPath = path;
            _isDirty = false;
            UpdatePathText();
            SetStatus($"Saved {path}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to save configuration:\n{ex.Message}", "Save Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RefreshAllLists()
    {
        RefreshApplicationsList(-1);
        RefreshExclusionsList(-1);
        RefreshCategoriesList(-1);
    }

    private void RefreshApplicationsList(int selectedIndex)
    {
        _loadedApplicationIndex = -1;
        ApplicationsList.ItemsSource = null;
        ApplicationsList.ItemsSource = _settings.Applications
            .Select(application => string.IsNullOrWhiteSpace(application.Name) ? "(unnamed)" : application.Name)
            .ToList();

        if (_settings.Applications.Count == 0)
        {
            ApplicationsList.SelectedIndex = -1;
            ClearApplicationFields();
            return;
        }

        ApplicationsList.SelectedIndex = NormalizeSelection(selectedIndex, _settings.Applications.Count);
    }

    private void ApplicationsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        CommitApplicationEditor();
        int index = ApplicationsList.SelectedIndex;
        _loadedApplicationIndex = index;
        if (index < 0 || index >= _settings.Applications.Count)
        {
            ClearApplicationFields();
            return;
        }

        ApplicationDefinition application = _settings.Applications[index];
        SetEditorText(() =>
        {
            AppNameText.Text = application.Name ?? string.Empty;
            AppIncludeText.Text = JoinLines(application.Include);
            AppExcludeText.Text = JoinLines(application.Exclude);
        });
    }

    private void AddApplication_Click(object sender, RoutedEventArgs e)
    {
        string name = AppNameText.Text.Trim();
        List<string> include = ParseLines(AppIncludeText);
        if (!ValidateNamedRule(name, include, "Application name is required.", "Include keywords are required."))
        {
            return;
        }

        _settings.Applications.Add(new ApplicationDefinition
        {
            Name = name,
            Include = include,
            Exclude = ParseLines(AppExcludeText)
        });
        MarkDirty();
        _loadedApplicationIndex = -1;
        RefreshApplicationsList(_settings.Applications.Count - 1);
    }

    private void UpdateApplication_Click(object sender, RoutedEventArgs e)
    {
        int index = ApplicationsList.SelectedIndex;
        if (index < 0)
        {
            return;
        }

        string name = AppNameText.Text.Trim();
        List<string> include = ParseLines(AppIncludeText);
        if (!ValidateNamedRule(name, include, "Application name is required.", "Include keywords are required."))
        {
            return;
        }

        if (ConfigurationDraftUpdater.ApplyApplication(
                _settings, index, name, include, ParseLines(AppExcludeText)))
        {
            MarkDirty();
        }
        _loadedApplicationIndex = -1;
        RefreshApplicationsList(index);
    }

    private void RemoveApplication_Click(object sender, RoutedEventArgs e)
    {
        int index = ApplicationsList.SelectedIndex;
        if (index < 0)
        {
            return;
        }

        _settings.Applications.RemoveAt(index);
        MarkDirty();
        _loadedApplicationIndex = -1;
        RefreshApplicationsList(Math.Max(0, index - 1));
    }

    private void MoveApplicationUp_Click(object sender, RoutedEventArgs e) => MoveApplication(-1);

    private void MoveApplicationDown_Click(object sender, RoutedEventArgs e) => MoveApplication(1);

    private void MoveApplication(int direction)
    {
        int index = ApplicationsList.SelectedIndex;
        int newIndex = index + direction;
        if (index < 0 || newIndex < 0 || newIndex >= _settings.Applications.Count)
        {
            return;
        }

        ApplicationDefinition application = _settings.Applications[index];
        _settings.Applications.RemoveAt(index);
        _settings.Applications.Insert(newIndex, application);
        MarkDirty();
        _loadedApplicationIndex = -1;
        RefreshApplicationsList(newIndex);
    }

    private void ClearApplicationFields()
    {
        SetEditorText(() =>
        {
            AppNameText.Clear();
            AppIncludeText.Clear();
            AppExcludeText.Clear();
        });
    }

    private void RefreshExclusionsList(int selectedIndex)
    {
        _loadedExclusionIndex = -1;
        ExclusionsList.ItemsSource = null;
        ExclusionsList.ItemsSource = _settings.Exclusions
            .Select(exclusion => exclusion.Include.Count > 0 ? string.Join(" & ", exclusion.Include) : "(empty)")
            .ToList();

        if (_settings.Exclusions.Count == 0)
        {
            ExclusionsList.SelectedIndex = -1;
            SetEditorText(ExclusionIncludeText.Clear);
            return;
        }

        ExclusionsList.SelectedIndex = NormalizeSelection(selectedIndex, _settings.Exclusions.Count);
    }

    private void ExclusionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        CommitExclusionEditor();
        int index = ExclusionsList.SelectedIndex;
        _loadedExclusionIndex = index;
        SetEditorText(() =>
        {
            ExclusionIncludeText.Text = index >= 0 && index < _settings.Exclusions.Count
                ? JoinLines(_settings.Exclusions[index].Include)
                : string.Empty;
        });
    }

    private void AddExclusion_Click(object sender, RoutedEventArgs e)
    {
        List<string> include = ParseLines(ExclusionIncludeText);
        if (include.Count == 0)
        {
            ShowWarning("Include keywords are required.");
            return;
        }

        _settings.Exclusions.Add(new ExclusionDefinition { Include = include });
        MarkDirty();
        _loadedExclusionIndex = -1;
        RefreshExclusionsList(_settings.Exclusions.Count - 1);
    }

    private void UpdateExclusion_Click(object sender, RoutedEventArgs e)
    {
        int index = ExclusionsList.SelectedIndex;
        if (index < 0)
        {
            return;
        }

        List<string> include = ParseLines(ExclusionIncludeText);
        if (include.Count == 0)
        {
            ShowWarning("Include keywords are required.");
            return;
        }

        if (ConfigurationDraftUpdater.ApplyExclusion(_settings, index, include))
        {
            MarkDirty();
        }
        _loadedExclusionIndex = -1;
        RefreshExclusionsList(index);
    }

    private void RemoveExclusion_Click(object sender, RoutedEventArgs e)
    {
        int index = ExclusionsList.SelectedIndex;
        if (index < 0)
        {
            return;
        }

        _settings.Exclusions.RemoveAt(index);
        MarkDirty();
        _loadedExclusionIndex = -1;
        RefreshExclusionsList(Math.Max(0, index - 1));
    }

    private void RefreshCategoriesList(int selectedIndex)
    {
        _loadedCategoryIndex = -1;
        CategoriesList.ItemsSource = null;
        CategoriesList.ItemsSource = _settings.Categories
            .Select(category => string.IsNullOrWhiteSpace(category.Name) ? "(unnamed)" : category.Name)
            .ToList();

        if (_settings.Categories.Count == 0)
        {
            CategoriesList.SelectedIndex = -1;
            ClearCategoryFields();
            return;
        }

        CategoriesList.SelectedIndex = NormalizeSelection(selectedIndex, _settings.Categories.Count);
    }

    private void CategoriesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        CommitCategoryEditor();
        int index = CategoriesList.SelectedIndex;
        _loadedCategoryIndex = index;
        if (index < 0 || index >= _settings.Categories.Count)
        {
            ClearCategoryFields();
            return;
        }

        CategoryDefinition category = _settings.Categories[index];
        SetEditorText(() =>
        {
            CategoryNameText.Text = category.Name ?? string.Empty;
            CategoryIncludeText.Text = JoinLines(category.IncludeApplications);
            CategoryExcludeText.Text = JoinLines(category.ExcludeApplications);
        });
    }

    private void AddCategory_Click(object sender, RoutedEventArgs e)
    {
        string name = CategoryNameText.Text.Trim();
        List<string> include = ParseLines(CategoryIncludeText);
        if (!ValidateNamedRule(name, include, "Category name is required.", "Include applications are required."))
        {
            return;
        }

        _settings.Categories.Add(new CategoryDefinition
        {
            Name = name,
            IncludeApplications = include,
            ExcludeApplications = ParseLines(CategoryExcludeText)
        });
        MarkDirty();
        _loadedCategoryIndex = -1;
        RefreshCategoriesList(_settings.Categories.Count - 1);
    }

    private void UpdateCategory_Click(object sender, RoutedEventArgs e)
    {
        int index = CategoriesList.SelectedIndex;
        if (index < 0)
        {
            return;
        }

        string name = CategoryNameText.Text.Trim();
        List<string> include = ParseLines(CategoryIncludeText);
        if (!ValidateNamedRule(name, include, "Category name is required.", "Include applications are required."))
        {
            return;
        }

        if (ConfigurationDraftUpdater.ApplyCategory(
                _settings, index, name, include, ParseLines(CategoryExcludeText)))
        {
            MarkDirty();
        }
        _loadedCategoryIndex = -1;
        RefreshCategoriesList(index);
    }

    private void RemoveCategory_Click(object sender, RoutedEventArgs e)
    {
        int index = CategoriesList.SelectedIndex;
        if (index < 0)
        {
            return;
        }

        _settings.Categories.RemoveAt(index);
        MarkDirty();
        _loadedCategoryIndex = -1;
        RefreshCategoriesList(Math.Max(0, index - 1));
    }

    private void ClearCategoryFields()
    {
        SetEditorText(() =>
        {
            CategoryNameText.Clear();
            CategoryIncludeText.Clear();
            CategoryExcludeText.Clear();
        });
    }

    private static int NormalizeSelection(int selectedIndex, int count) =>
        selectedIndex < 0 || selectedIndex >= count ? 0 : selectedIndex;

    private static List<string> ParseLines(TextBox textBox) =>
        textBox.Text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();

    private static string JoinLines(IEnumerable<string> values) => string.Join(Environment.NewLine, values);

    private void EditorField_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_suppressEditorEvents)
        {
            MarkDirty();
        }
    }

    private void SetEditorText(Action update)
    {
        _suppressEditorEvents = true;
        try
        {
            update();
        }
        finally
        {
            _suppressEditorEvents = false;
        }
    }

    private void CommitPendingEdits()
    {
        CommitApplicationEditor();
        CommitExclusionEditor();
        CommitCategoryEditor();
    }

    private void CommitApplicationEditor()
    {
        if (ConfigurationDraftUpdater.ApplyApplication(
                _settings,
                _loadedApplicationIndex,
                AppNameText.Text.Trim(),
                ParseLines(AppIncludeText),
                ParseLines(AppExcludeText)))
        {
            MarkDirty();
        }
    }

    private void CommitExclusionEditor()
    {
        if (ConfigurationDraftUpdater.ApplyExclusion(
                _settings,
                _loadedExclusionIndex,
                ParseLines(ExclusionIncludeText)))
        {
            MarkDirty();
        }
    }

    private void CommitCategoryEditor()
    {
        if (ConfigurationDraftUpdater.ApplyCategory(
                _settings,
                _loadedCategoryIndex,
                CategoryNameText.Text.Trim(),
                ParseLines(CategoryIncludeText),
                ParseLines(CategoryExcludeText)))
        {
            MarkDirty();
        }
    }

    private bool ValidateSettingsForSave()
    {
        if (_loadedApplicationIndex < 0 &&
            (!string.IsNullOrWhiteSpace(AppNameText.Text) ||
             ParseLines(AppIncludeText).Count > 0 ||
             ParseLines(AppExcludeText).Count > 0))
        {
            ShowWarning("The application draft has not been added. Use Add New before saving.");
            return false;
        }

        if (_loadedExclusionIndex < 0 && ParseLines(ExclusionIncludeText).Count > 0)
        {
            ShowWarning("The exclusion draft has not been added. Use Add New before saving.");
            return false;
        }

        if (_loadedCategoryIndex < 0 &&
            (!string.IsNullOrWhiteSpace(CategoryNameText.Text) ||
             ParseLines(CategoryIncludeText).Count > 0 ||
             ParseLines(CategoryExcludeText).Count > 0))
        {
            ShowWarning("The category draft has not been added. Use Add New before saving.");
            return false;
        }

        if (_settings.Applications.Any(application =>
                string.IsNullOrWhiteSpace(application.Name) || application.Include.Count == 0))
        {
            ShowWarning("Every application requires a name and at least one include keyword.");
            return false;
        }

        if (_settings.Exclusions.Any(exclusion => exclusion.Include.Count == 0))
        {
            ShowWarning("Every exclusion requires at least one include keyword.");
            return false;
        }

        if (_settings.Categories.Any(category =>
                string.IsNullOrWhiteSpace(category.Name) || category.IncludeApplications.Count == 0))
        {
            ShowWarning("Every category requires a name and at least one included application.");
            return false;
        }

        return true;
    }

    private bool ValidateNamedRule(string name, List<string> include, string missingNameMessage, string missingIncludeMessage)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            ShowWarning(missingNameMessage);
            return false;
        }

        if (include.Count == 0)
        {
            ShowWarning(missingIncludeMessage);
            return false;
        }

        return true;
    }

    private void MarkDirty()
    {
        _isDirty = true;
        SetStatus("Unsaved changes");
    }

    private void SetStatus(string message) => StatusText.Text = message;

    private void UpdatePathText() => ConfigPathTextBox.Text = _currentPath ?? "(not saved yet)";

    private void ShowWarning(string message) =>
        MessageBox.Show(this, message, "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);

    private bool ConfirmDiscardChanges()
    {
        CommitPendingEdits();
        if (!_isDirty)
        {
            return true;
        }

        MessageBoxResult result = MessageBox.Show(this, "You have unsaved changes. Discard them?",
            "Unsaved Changes", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        return result == MessageBoxResult.Yes;
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!ConfirmDiscardChanges())
        {
            e.Cancel = true;
            return;
        }

        _dashboardTimer.Stop();
        if (_ownsLogger && IsLoggerRunning())
        {
            _loggerStopEvent?.Set();
        }
        DisposeLoggerHandles();
    }
}
