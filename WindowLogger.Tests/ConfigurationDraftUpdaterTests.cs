using WindowLogger.Shared.Configuration;
using WindowLoggerConfigGui;

namespace WindowLogger.Tests;

public sealed class ConfigurationDraftUpdaterTests
{
    [Test]
    public void Pending_application_edit_is_applied_before_serialization()
    {
        var settings = new AppSettings
        {
            Applications =
            [
                new ApplicationDefinition
                {
                    Name = "Old name",
                    Include = ["old.exe"]
                }
            ]
        };

        bool changed = ConfigurationDraftUpdater.ApplyApplication(
            settings,
            0,
            "New name",
            ["new.exe"],
            []);
        string json = AppSettingsJson.Serialize(settings);

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True);
            Assert.That(settings.Applications[0].Name, Is.EqualTo("New name"));
            Assert.That(json, Does.Contain("New name"));
            Assert.That(json, Does.Contain("new.exe"));
            Assert.That(json, Does.Not.Contain("Old name"));
        });
    }
}
