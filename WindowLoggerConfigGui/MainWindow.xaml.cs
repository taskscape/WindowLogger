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
using System.Windows.Media;
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
    private DashboardPeriod _dashboardPeriod = DashboardPeriod.Day;
    private DateTime _dashboardAnchor = DateTime.Today;
    private DateTime _dashboardRangeStart;
    private DateTime _dashboardRangeEnd;
    private ActivityRecord[] _currentAnalyticsRecords = [];
    private ActivityRecord[] _previousAnalyticsRecords = [];
    private DashboardSnapshot? _dashboardSnapshot;
    private bool _controlCenterLoaded;
    private bool _updatingAnalyticsUi;
    private DateTime _previousRangeStart;
    private DateTime _previousRangeEnd;
    private DateTime _dashboardPeriodEnd;
    private int _dashboardTimerTicks;
    private readonly RuleEvaluationEngine _ruleEvaluationEngine = new();
    private RuleEvaluationResult? _lastRuleEvaluation;
    private bool _suppressRuleTesterEvents;

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
        _dashboardTimer.Tick += (_, _) =>
        {
            RefreshDashboard();
            if (++_dashboardTimerTicks % 15 == 0)
            {
                LoadAnalyticsData();
            }
        };
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        TryLoadDefaultConfig();
        LoadUserSettings();
        DetectExistingLogger();
        RefreshDashboard();
        _controlCenterLoaded = true;
        LoadAnalyticsData();
        RefreshRuleTester();
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

    private void DayPeriod_Click(object sender, RoutedEventArgs e) => SetDashboardPeriod(DashboardPeriod.Day);
    private void WeekPeriod_Click(object sender, RoutedEventArgs e) => SetDashboardPeriod(DashboardPeriod.Week);
    private void MonthPeriod_Click(object sender, RoutedEventArgs e) => SetDashboardPeriod(DashboardPeriod.Month);

    private void SetDashboardPeriod(DashboardPeriod period)
    {
        _dashboardPeriod = period;
        _dashboardAnchor = DateTime.Today;
        LoadAnalyticsData();
    }

    private void PreviousPeriod_Click(object sender, RoutedEventArgs e)
    {
        _dashboardAnchor = ShiftPeriod(_dashboardAnchor, -1);
        LoadAnalyticsData();
    }

    private void NextPeriod_Click(object sender, RoutedEventArgs e)
    {
        _dashboardAnchor = ShiftPeriod(_dashboardAnchor, 1);
        LoadAnalyticsData();
    }

    private void CurrentPeriod_Click(object sender, RoutedEventArgs e)
    {
        _dashboardAnchor = DateTime.Today;
        LoadAnalyticsData();
    }

    private DateTime ShiftPeriod(DateTime value, int direction) => _dashboardPeriod switch
    {
        DashboardPeriod.Day => value.AddDays(direction),
        DashboardPeriod.Week => value.AddDays(7 * direction),
        DashboardPeriod.Month => value.AddMonths(direction),
        _ => value
    };

    private void LoadAnalyticsData()
    {
        if (!_controlCenterLoaded)
        {
            return;
        }

        (_dashboardRangeStart, _dashboardPeriodEnd) = GetPeriodRange(_dashboardAnchor, _dashboardPeriod);
        (_previousRangeStart, DateTime previousPeriodEnd) = GetPeriodRange(
            ShiftPeriod(_dashboardRangeStart, -1), _dashboardPeriod);

        DateTime now = DateTime.Now;
        _dashboardRangeEnd = _dashboardRangeStart <= now && now < _dashboardPeriodEnd ? now : _dashboardPeriodEnd;
        if (_dashboardRangeStart > now)
        {
            _dashboardRangeEnd = _dashboardRangeStart;
        }

        TimeSpan elapsed = _dashboardRangeEnd - _dashboardRangeStart;
        _previousRangeEnd = _dashboardRangeEnd < _dashboardPeriodEnd
            ? Min(_previousRangeStart + elapsed, previousPeriodEnd)
            : previousPeriodEnd;

        try
        {
            if (File.Exists(DatabaseFile))
            {
                using var database = new ActivityDatabase(DatabaseFile, readOnly: true);
                _currentAnalyticsRecords = _dashboardRangeEnd > _dashboardRangeStart
                    ? database.ReadRangeWithContext(_dashboardRangeStart, _dashboardRangeEnd).ToArray()
                    : [];
                _previousAnalyticsRecords = _previousRangeEnd > _previousRangeStart
                    ? database.ReadRangeWithContext(_previousRangeStart, _previousRangeEnd).ToArray()
                    : [];
            }
            else
            {
                _currentAnalyticsRecords = [];
                _previousAnalyticsRecords = [];
            }
            ApplyAnalyticsFilters();
        }
        catch (Exception ex)
        {
            SetStatus($"Timeline unavailable: {ex.Message}");
        }
    }

    private static DateTime Min(DateTime first, DateTime second) => first <= second ? first : second;

    private static (DateTime Start, DateTime End) GetPeriodRange(DateTime anchor, DashboardPeriod period)
    {
        DateTime day = anchor.Date;
        return period switch
        {
            DashboardPeriod.Day => (day, day.AddDays(1)),
            DashboardPeriod.Week => GetWeekRange(day),
            DashboardPeriod.Month => (new DateTime(day.Year, day.Month, 1), new DateTime(day.Year, day.Month, 1).AddMonths(1)),
            _ => (day, day.AddDays(1))
        };
    }

    private static (DateTime Start, DateTime End) GetWeekRange(DateTime day)
    {
        int offset = ((int)day.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        DateTime start = day.AddDays(-offset);
        return (start, start.AddDays(7));
    }

    private void AnalyticsFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (_controlCenterLoaded && !_updatingAnalyticsUi)
        {
            ApplyAnalyticsFilters();
        }
    }

    private void ApplyAnalyticsFilters()
    {
        if (_dashboardRangeEnd <= _dashboardRangeStart || _previousRangeEnd <= _previousRangeStart)
        {
            _dashboardSnapshot = new DashboardSnapshot
            {
                Timeline = [], PreviousTimeline = [], Categories = [], Applications = []
            };
        }
        else
        {
            string status = (AnalyticsStatusFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All";
            _dashboardSnapshot = DashboardAnalytics.Build(
                _currentAnalyticsRecords,
                _previousAnalyticsRecords,
                _settings,
                _dashboardRangeStart,
                _dashboardRangeEnd,
                _previousRangeStart,
                _previousRangeEnd,
                AnalyticsSearchText.Text,
                status);
        }

        RenderAnalytics();
    }

    private void RenderAnalytics()
    {
        if (_dashboardSnapshot is null)
        {
            return;
        }

        double total = _dashboardSnapshot.ActiveMinutes + _dashboardSnapshot.IdleMinutes;
        double previous = _dashboardSnapshot.PreviousMinutes;
        AnalyticsActiveText.Text = DashboardBarItem.FormatDuration(_dashboardSnapshot.ActiveMinutes);
        AnalyticsIdleText.Text = DashboardBarItem.FormatDuration(_dashboardSnapshot.IdleMinutes);
        AnalyticsPreviousText.Text = DashboardBarItem.FormatDuration(previous);
        AnalyticsChangeText.Text = previous <= 0
            ? total <= 0 ? "0%" : "New"
            : $"{(total - previous) / previous * 100:+0.#;-0.#;0}%";
        AnalyticsPeriodText.Text = FormatPeriodLabel(_dashboardRangeStart, _dashboardPeriodEnd);
        DayPeriodButton.FontWeight = _dashboardPeriod == DashboardPeriod.Day ? FontWeights.Bold : FontWeights.Normal;
        WeekPeriodButton.FontWeight = _dashboardPeriod == DashboardPeriod.Week ? FontWeights.Bold : FontWeights.Normal;
        MonthPeriodButton.FontWeight = _dashboardPeriod == DashboardPeriod.Month ? FontWeights.Bold : FontWeights.Normal;

        _updatingAnalyticsUi = true;
        try
        {
            string? selectedCategory = (CategoryChartList.SelectedItem as DashboardBarItem)?.Name;
            CategoryChartList.ItemsSource = CreateBarItems(_dashboardSnapshot.Categories);
            CategoryChartList.SelectedItem = CategoryChartList.Items.Cast<DashboardBarItem>()
                .FirstOrDefault(item => item.Name.Equals(selectedCategory, StringComparison.OrdinalIgnoreCase));
            UpdateApplicationsForCategory();
        }
        finally
        {
            _updatingAnalyticsUi = false;
        }
        RenderTimeline();
    }

    private static string FormatPeriodLabel(DateTime start, DateTime end) => end - start <= TimeSpan.FromDays(1)
        ? start.ToString("dddd, d MMMM yyyy")
        : start.Year == end.AddTicks(-1).Year
            ? $"{start:d MMM} – {end.AddDays(-1):d MMM yyyy}"
            : $"{start:d MMM yyyy} – {end.AddDays(-1):d MMM yyyy}";

    private static IReadOnlyList<DashboardBarItem> CreateBarItems(IEnumerable<DashboardBreakdown> breakdowns)
    {
        DashboardBreakdown[] data = breakdowns.ToArray();
        double maximum = data.Select(item => Math.Max(item.TotalMinutes, item.PreviousMinutes)).DefaultIfEmpty(0).Max();
        return data.Select(item => new DashboardBarItem
            {
                Data = item,
                BarWidth = maximum <= 0 ? 0 : Math.Max(2, item.TotalMinutes / maximum * 230)
            })
            .ToList();
    }

    private void CategoryChartList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updatingAnalyticsUi)
        {
            UpdateApplicationsForCategory();
        }
    }

    private void UpdateApplicationsForCategory()
    {
        if (_dashboardSnapshot is null)
        {
            return;
        }
        string? category = (CategoryChartList.SelectedItem as DashboardBarItem)?.Name;
        IEnumerable<TimelineBlock> current = _dashboardSnapshot.Timeline;
        IEnumerable<TimelineBlock> previous = _dashboardSnapshot.PreviousTimeline;
        if (!string.IsNullOrWhiteSpace(category))
        {
            current = current.Where(block => block.Categories.Contains(category, StringComparer.OrdinalIgnoreCase));
            previous = previous.Where(block => block.Categories.Contains(category, StringComparer.OrdinalIgnoreCase));
        }

        string? selectedApplication = (ApplicationChartList.SelectedItem as DashboardBarItem)?.Name;
        ApplicationChartList.ItemsSource = CreateBarItems(Summarize(current, previous, block => block.Application));
        ApplicationChartList.SelectedItem = ApplicationChartList.Items.Cast<DashboardBarItem>()
            .FirstOrDefault(item => item.Name.Equals(selectedApplication, StringComparison.OrdinalIgnoreCase));
        UpdateWindowDrillDown();
    }

    private void ApplicationChartList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updatingAnalyticsUi)
        {
            UpdateWindowDrillDown();
        }
    }

    private void UpdateWindowDrillDown()
    {
        if (_dashboardSnapshot is null)
        {
            return;
        }
        string? category = (CategoryChartList.SelectedItem as DashboardBarItem)?.Name;
        string? application = (ApplicationChartList.SelectedItem as DashboardBarItem)?.Name;
        IEnumerable<TimelineBlock> blocks = _dashboardSnapshot.Timeline;
        if (!string.IsNullOrWhiteSpace(category))
        {
            blocks = blocks.Where(block => block.Categories.Contains(category, StringComparer.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(application))
        {
            blocks = blocks.Where(block => block.Application.Equals(application, StringComparison.OrdinalIgnoreCase));
        }
        WindowTitleList.ItemsSource = blocks
            .GroupBy(block => new { block.WindowTitle, block.Status })
            .Select(group => new
            {
                group.Key.WindowTitle,
                group.Key.Status,
                Minutes = group.Sum(block => block.Minutes)
            })
            .OrderByDescending(item => item.Minutes)
            .Select(item => $"{DashboardBarItem.FormatDuration(item.Minutes),8}  {item.Status,-8}  {item.WindowTitle}")
            .ToList();
    }

    private static IReadOnlyList<DashboardBreakdown> Summarize(
        IEnumerable<TimelineBlock> current,
        IEnumerable<TimelineBlock> previous,
        Func<TimelineBlock, string> keySelector)
    {
        Dictionary<string, TimelineBlock[]> currentGroups = current.GroupBy(keySelector, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, double> previousGroups = previous.GroupBy(keySelector, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(block => block.Minutes), StringComparer.OrdinalIgnoreCase);
        return currentGroups.Keys.Union(previousGroups.Keys, StringComparer.OrdinalIgnoreCase)
            .Select(name =>
            {
                TimelineBlock[] blocks = currentGroups.GetValueOrDefault(name) ?? [];
                return new DashboardBreakdown(
                    name,
                    blocks.Where(block => block.Status == "Active").Sum(block => block.Minutes),
                    blocks.Where(block => block.Status == "Inactive").Sum(block => block.Minutes),
                    previousGroups.GetValueOrDefault(name));
            })
            .OrderByDescending(item => item.TotalMinutes)
            .ToList();
    }

    private void TimelineCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => RenderTimeline();

    private void RenderTimeline()
    {
        TimelineCanvas.Children.Clear();
        if (_dashboardSnapshot is null || TimelineCanvas.ActualWidth <= 0 || _dashboardPeriodEnd <= _dashboardRangeStart)
        {
            return;
        }

        double width = TimelineCanvas.ActualWidth;
        double periodTicks = (_dashboardPeriodEnd - _dashboardRangeStart).Ticks;
        foreach (DateTime tick in GetTimelineTicks())
        {
            double x = (tick - _dashboardRangeStart).Ticks / periodTicks * width;
            var line = new System.Windows.Shapes.Line
                { X1 = x, X2 = x, Y1 = 0, Y2 = 76, Stroke = Brushes.LightGray, StrokeThickness = 1 };
            TimelineCanvas.Children.Add(line);
            var label = new TextBlock { Text = FormatTimelineTick(tick), FontSize = 10, Foreground = Brushes.DimGray };
            Canvas.SetLeft(label, Math.Min(x + 2, Math.Max(0, width - 44)));
            Canvas.SetTop(label, 2);
            TimelineCanvas.Children.Add(label);
        }

        foreach (TimelineBlock block in _dashboardSnapshot.Timeline)
        {
            double left = (block.Start - _dashboardRangeStart).Ticks / periodTicks * width;
            double blockWidth = Math.Max(2, (block.End - block.Start).Ticks / periodTicks * width);
            var border = new Border
            {
                Width = blockWidth,
                Height = 42,
                Background = block.Status == "Active"
                    ? new SolidColorBrush(Color.FromRgb(67, 133, 208))
                    : new SolidColorBrush(Color.FromRgb(224, 162, 58)),
                ToolTip = $"{block.Start:g} – {block.End:t}\n{block.Status} • {block.Application}\n{block.WindowTitle}"
            };
            Canvas.SetLeft(border, left);
            Canvas.SetTop(border, 24);
            TimelineCanvas.Children.Add(border);
        }
    }

    private IEnumerable<DateTime> GetTimelineTicks()
    {
        TimeSpan step = _dashboardPeriod switch
        {
            DashboardPeriod.Day => TimeSpan.FromHours(6),
            DashboardPeriod.Week => TimeSpan.FromDays(1),
            DashboardPeriod.Month => TimeSpan.FromDays(Math.Max(1, DateTime.DaysInMonth(_dashboardRangeStart.Year, _dashboardRangeStart.Month) / 6)),
            _ => TimeSpan.FromHours(6)
        };
        for (DateTime tick = _dashboardRangeStart; tick < _dashboardPeriodEnd; tick += step)
        {
            yield return tick;
        }
    }

    private string FormatTimelineTick(DateTime value) => _dashboardPeriod == DashboardPeriod.Day
        ? value.ToString("HH:mm")
        : value.ToString("ddd d");

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

    private void RefreshRuleTester()
    {
        if (!_controlCenterLoaded)
        {
            return;
        }

        try
        {
            _suppressRuleTesterEvents = true;
            if (File.Exists(DatabaseFile))
            {
                using var database = new ActivityDatabase(DatabaseFile, readOnly: true);
                RecentRuleSamples.ItemsSource = database.ReadRecent(100)
                    .Where(record => record.Kind == ActivityRecordKind.Usage)
                    .Select(record => record.WindowTitle)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(50)
                    .ToList();
            }
            else
            {
                RecentRuleSamples.ItemsSource = Array.Empty<string>();
            }
            RefreshRuleTestsGrid();
        }
        catch (Exception ex)
        {
            SetStatus($"Rule tester could not load recent activity: {ex.Message}");
        }
        finally
        {
            _suppressRuleTesterEvents = false;
        }
    }

    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_controlCenterLoaded && ReferenceEquals(e.OriginalSource, MainTabs) && ReferenceEquals(MainTabs.SelectedItem, RuleTesterTab))
        {
            RefreshRuleTester();
            EvaluateRulePreview();
        }
    }

    private void RuleTesterInput_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_suppressRuleTesterEvents && _controlCenterLoaded && LiveRulePreviewCheck.IsChecked == true)
        {
            EvaluateRulePreview();
        }
    }

    private void LiveRulePreview_Changed(object sender, RoutedEventArgs e)
    {
        if (!_suppressRuleTesterEvents && _controlCenterLoaded && LiveRulePreviewCheck.IsChecked == true)
        {
            EvaluateRulePreview();
        }
    }

    private void TestRules_Click(object sender, RoutedEventArgs e) => EvaluateRulePreview();

    private void RecentRuleSamples_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressRuleTesterEvents || RecentRuleSamples.SelectedItem is not string captured)
        {
            return;
        }
        (string title, string executable) = SplitCapturedValue(captured);
        _suppressRuleTesterEvents = true;
        RuleWindowTitleText.Text = title;
        RuleExecutableText.Text = executable;
        _suppressRuleTesterEvents = false;
        EvaluateRulePreview();
    }

    private static (string WindowTitle, string Executable) SplitCapturedValue(string captured)
    {
        int separator = captured.LastIndexOf(" [", StringComparison.Ordinal);
        return separator >= 0 && captured.EndsWith(']')
            ? (captured[..separator], captured[(separator + 2)..^1])
            : (captured, string.Empty);
    }

    private void EvaluateRulePreview()
    {
        string title = RuleWindowTitleText.Text;
        string executable = RuleExecutableText.Text;
        string captured = ActivityCapturedValue.Format(title, executable);
        CapturedPreviewText.Text = string.IsNullOrWhiteSpace(captured)
            ? "Enter a window title or executable to test."
            : captured;
        if (string.IsNullOrWhiteSpace(captured))
        {
            _lastRuleEvaluation = null;
            RuleOutcomeText.Text = "No sample entered";
            RuleCategoriesText.Text = string.Empty;
            RuleTraceList.ItemsSource = null;
            RuleWarningsList.ItemsSource = null;
            OpenMatchedApplicationButton.IsEnabled = false;
            OpenMatchedCategoryButton.IsEnabled = false;
            return;
        }

        AppSettings previewSettings = CreatePreviewSettings();
        _lastRuleEvaluation = _ruleEvaluationEngine.Evaluate(title, executable, previewSettings);
        RuleOutcomeText.Text = _lastRuleEvaluation.IsExcluded
            ? $"Excluded by rule #{_lastRuleEvaluation.MatchedExclusionIndex + 1}"
            : _lastRuleEvaluation.MatchedApplication is null
                ? "No application matched"
                : $"Application: {_lastRuleEvaluation.MatchedApplication}";
        RuleCategoriesText.Text = _lastRuleEvaluation.Categories.Count == 0
            ? "Categories: none"
            : $"Categories: {string.Join(", ", _lastRuleEvaluation.Categories)}";
        RuleTraceList.ItemsSource = _lastRuleEvaluation.Trace
            .Select(item => new RuleTraceViewModel { Trace = item })
            .ToList();
        RuleWarningsList.ItemsSource = _lastRuleEvaluation.Warnings.Count == 0
            ? new[] { "No warnings for this sample." }
            : _lastRuleEvaluation.Warnings;
        OpenMatchedApplicationButton.IsEnabled = _lastRuleEvaluation.MatchedApplicationIndex is not null;
        OpenMatchedCategoryButton.IsEnabled = _lastRuleEvaluation.MatchedCategoryIndices.Count > 0;
    }

    private AppSettings CreatePreviewSettings()
    {
        AppSettings preview = AppSettingsJson.Deserialize(AppSettingsJson.Serialize(_settings));
        if (_loadedApplicationIndex >= 0 && _loadedApplicationIndex < preview.Applications.Count)
        {
            ApplicationDefinition application = preview.Applications[_loadedApplicationIndex];
            application.Name = AppNameText.Text.Trim();
            application.Include = ParseLines(AppIncludeText);
            application.Exclude = ParseLines(AppExcludeText);
        }
        if (_loadedExclusionIndex >= 0 && _loadedExclusionIndex < preview.Exclusions.Count)
        {
            preview.Exclusions[_loadedExclusionIndex].Include = ParseLines(ExclusionIncludeText);
        }
        if (_loadedCategoryIndex >= 0 && _loadedCategoryIndex < preview.Categories.Count)
        {
            CategoryDefinition category = preview.Categories[_loadedCategoryIndex];
            category.Name = CategoryNameText.Text.Trim();
            category.IncludeApplications = ParseLines(CategoryIncludeText);
            category.ExcludeApplications = ParseLines(CategoryExcludeText);
        }
        return preview;
    }

    private void OpenMatchedApplication_Click(object sender, RoutedEventArgs e)
    {
        if (_lastRuleEvaluation?.MatchedApplicationIndex is not int index)
        {
            return;
        }
        MainTabs.SelectedItem = ApplicationsTab;
        ApplicationsList.SelectedIndex = index;
        ApplicationsList.ScrollIntoView(ApplicationsList.SelectedItem);
    }

    private void OpenMatchedCategory_Click(object sender, RoutedEventArgs e)
    {
        if (_lastRuleEvaluation?.MatchedCategoryIndices.FirstOrDefault(-1) is not int index || index < 0)
        {
            return;
        }
        MainTabs.SelectedItem = CategoriesTab;
        CategoriesList.SelectedIndex = index;
        CategoriesList.ScrollIntoView(CategoriesList.SelectedItem);
    }

    private void CreateRuleFromSample_Click(object sender, RoutedEventArgs e)
    {
        string pattern = !string.IsNullOrWhiteSpace(RuleExecutableText.Text)
            ? RuleExecutableText.Text.Trim()
            : RuleWindowTitleText.Text.Trim();
        if (string.IsNullOrWhiteSpace(pattern))
        {
            ShowWarning("Enter or select a sample before creating a rule.");
            return;
        }
        string name = !string.IsNullOrWhiteSpace(RuleExecutableText.Text)
            ? Path.GetFileNameWithoutExtension(RuleExecutableText.Text.Trim())
            : "New Application";
        _settings.Applications.Add(new ApplicationDefinition { Name = name, Include = [pattern] });
        MarkDirty();
        _loadedApplicationIndex = -1;
        RefreshApplicationsList(_settings.Applications.Count - 1);
        MainTabs.SelectedItem = ApplicationsTab;
    }

    private void CopyCapturedValue_Click(object sender, RoutedEventArgs e)
    {
        string captured = ActivityCapturedValue.Format(RuleWindowTitleText.Text, RuleExecutableText.Text);
        if (!string.IsNullOrWhiteSpace(captured))
        {
            Clipboard.SetText(captured);
            SetStatus("Captured value copied to the clipboard.");
        }
    }

    private void AddRuleTest_Click(object sender, RoutedEventArgs e)
    {
        EvaluateRulePreview();
        if (_lastRuleEvaluation is null)
        {
            return;
        }
        _settings.RuleTests.Add(new RuleTestCase
        {
            Name = $"Test {_settings.RuleTests.Count + 1}",
            WindowTitle = RuleWindowTitleText.Text,
            ExecutableName = RuleExecutableText.Text,
            ExpectedApplication = _lastRuleEvaluation.MatchedApplication ?? string.Empty,
            ExpectedCategories = string.Join(", ", _lastRuleEvaluation.Categories),
            ExpectedExcluded = _lastRuleEvaluation.IsExcluded,
            LastResult = "Not run"
        });
        MarkDirty();
        RefreshRuleTestsGrid();
        RuleTestsGrid.SelectedIndex = _settings.RuleTests.Count - 1;
    }

    private void RemoveRuleTest_Click(object sender, RoutedEventArgs e)
    {
        int index = RuleTestsGrid.SelectedIndex;
        if (index < 0 || index >= _settings.RuleTests.Count)
        {
            return;
        }
        _settings.RuleTests.RemoveAt(index);
        MarkDirty();
        RefreshRuleTestsGrid();
    }

    private void RunRuleTests_Click(object sender, RoutedEventArgs e)
    {
        RuleTestsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        RuleTestsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        int failures = EvaluateRuleTests(CreatePreviewSettings());
        RefreshRuleTestsGrid();
        SetStatus(failures == 0
            ? $"All {_settings.RuleTests.Count} rule tests passed."
            : $"{failures} of {_settings.RuleTests.Count} rule tests failed.");
    }

    private int EvaluateRuleTests(AppSettings settings)
    {
        int failures = 0;
        foreach (RuleTestCase test in _settings.RuleTests)
        {
            RuleEvaluationResult actual = _ruleEvaluationEngine.Evaluate(
                test.WindowTitle, test.ExecutableName, settings);
            string[] expectedCategories = test.ExpectedCategories
                .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            bool categoriesMatch = new HashSet<string>(expectedCategories, StringComparer.OrdinalIgnoreCase)
                .SetEquals(actual.Categories);
            bool passed = test.ExpectedExcluded == actual.IsExcluded &&
                string.Equals(test.ExpectedApplication, actual.MatchedApplication ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase) && categoriesMatch;
            test.LastResult = passed
                ? "Passed"
                : $"Failed: {(actual.IsExcluded ? "excluded" : actual.MatchedApplication ?? "unmatched")} / {string.Join(", ", actual.Categories)}";
            if (!passed)
            {
                failures++;
            }
        }
        return failures;
    }

    private void RefreshRuleTestsGrid()
    {
        RuleTestsGrid.ItemsSource = null;
        RuleTestsGrid.ItemsSource = _settings.RuleTests;
    }

    private void RuleTestsGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e) => MarkDirty();

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
            LoadAnalyticsData();
            RefreshRuleTester();
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
            ApplyAnalyticsFilters();
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
            if (_controlCenterLoaded && LiveRulePreviewCheck.IsChecked == true)
            {
                EvaluateRulePreview();
            }
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

        RuleTestsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        RuleTestsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        int failedTests = EvaluateRuleTests(_settings);
        RefreshRuleTestsGrid();
        if (failedTests > 0)
        {
            MessageBoxResult result = MessageBox.Show(this,
                $"{failedTests} of {_settings.RuleTests.Count} saved rule tests failed. Save anyway?",
                "Rule Test Failures", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            return result == MessageBoxResult.Yes;
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
