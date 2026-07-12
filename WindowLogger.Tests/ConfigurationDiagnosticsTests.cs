using WindowLogger.Shared.Configuration;

namespace WindowLogger.Tests;

public sealed class ConfigurationDiagnosticsTests
{
    [Test]
    public void Reports_duplicates_shadowing_conflicts_empty_categories_and_never_matches()
    {
        var settings = new AppSettings
        {
            Applications =
            [
                new ApplicationDefinition { Name = "Browser", Include = ["Chrome"] },
                new ApplicationDefinition { Name = "Browser", Include = ["Chrome", "Docs"] },
                new ApplicationDefinition { Name = "Conflict", Include = ["Secret"], Exclude = ["secret"] },
                new ApplicationDefinition { Name = "Unused", Include = ["Never Seen"] }
            ],
            Categories =
            [
                new CategoryDefinition { Name = "Empty" },
                new CategoryDefinition
                {
                    Name = "Conflict Category",
                    IncludeApplications = ["Browser"],
                    ExcludeApplications = ["browser"]
                }
            ]
        };

        IReadOnlyList<RuleDiagnostic> diagnostics = ConfigurationDiagnostics.Analyze(
            settings, ["Chrome - Home [chrome.exe]"]);

        Assert.That(diagnostics.Select(item => item.Code), Does.Contain("DUPLICATE_APPLICATION"));
        Assert.That(diagnostics.Select(item => item.Code), Does.Contain("UNREACHABLE_RULE"));
        Assert.That(diagnostics.Select(item => item.Code), Does.Contain("CONFLICTING_PATTERN"));
        Assert.That(diagnostics.Select(item => item.Code), Does.Contain("EMPTY_CATEGORY"));
        Assert.That(diagnostics.Select(item => item.Code), Does.Contain("CONFLICTING_CATEGORY"));
        Assert.That(diagnostics.Any(item => item.Code == "NEVER_MATCHED" && item.Message.Contains("Unused")), Is.True);
    }
}
