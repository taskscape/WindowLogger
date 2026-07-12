using WindowLogger.Shared.Settings;

namespace WindowLogger.Tests;

public sealed class UserSettingsTests
{
    [Test]
    public void Settings_round_trip_and_invalid_values_are_normalized()
    {
        string path = Path.Combine(Path.GetTempPath(), $"WindowLogger-settings-{Guid.NewGuid():N}.json");
        try
        {
            var source = new UserSettings
            {
                InactivityThresholdSeconds = 1,
                PollingIntervalMilliseconds = 10,
                RetentionDays = -4,
                CrashRecoveryMaxMinutes = 0,
                StoreWindowTitles = false,
                ReportFolder = @"C:\Reports"
            };

            UserSettingsStore.Save(path, source);
            UserSettings restored = UserSettingsStore.Load(path);

            Assert.Multiple(() =>
            {
                Assert.That(restored.InactivityThresholdSeconds, Is.EqualTo(5));
                Assert.That(restored.PollingIntervalMilliseconds, Is.EqualTo(100));
                Assert.That(restored.RetentionDays, Is.Zero);
                Assert.That(restored.CrashRecoveryMaxMinutes, Is.EqualTo(1));
                Assert.That(restored.StoreWindowTitles, Is.False);
                Assert.That(restored.ReportFolder, Is.EqualTo(@"C:\Reports"));
            });
        }
        finally
        {
            File.Delete(path);
        }
    }
}
