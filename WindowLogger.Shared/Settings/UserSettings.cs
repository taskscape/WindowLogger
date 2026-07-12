using System.Text;
using System.Text.Json;

namespace WindowLogger.Shared.Settings;

public sealed class UserSettings
{
    public int InactivityThresholdSeconds { get; set; } = 60;
    public int PollingIntervalMilliseconds { get; set; } = 500;
    public bool StoreWindowTitles { get; set; } = true;
    public bool StartTrackingPaused { get; set; }
    public int RetentionDays { get; set; } = 365;
    public int CrashRecoveryMaxMinutes { get; set; } = 5;
    public string ReportFolder { get; set; } = string.Empty;
    public bool StartTrackingOnLaunch { get; set; } = true;
    public bool LaunchAtWindowsStartup { get; set; }

    public void Normalize()
    {
        InactivityThresholdSeconds = Math.Clamp(InactivityThresholdSeconds, 5, 24 * 60 * 60);
        PollingIntervalMilliseconds = Math.Clamp(PollingIntervalMilliseconds, 100, 60_000);
        RetentionDays = Math.Clamp(RetentionDays, 0, 36500);
        CrashRecoveryMaxMinutes = Math.Clamp(CrashRecoveryMaxMinutes, 1, 24 * 60);
        ReportFolder ??= string.Empty;
    }
}

public static class UserSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    public static UserSettings Load(string path)
    {
        if (!File.Exists(path))
        {
            return new UserSettings();
        }

        UserSettings settings = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException("The user settings file is empty.");
        settings.Normalize();
        return settings;
    }

    public static void Save(string path, UserSettings settings)
    {
        settings.Normalize();
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = $"{fullPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, Options),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
