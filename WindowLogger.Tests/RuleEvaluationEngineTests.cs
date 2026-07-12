using WindowAnalyser;
using WindowLogger.Shared.Activity;
using WindowLogger.Shared.Configuration;

namespace WindowLogger.Tests;

public sealed class RuleEvaluationEngineTests
{
    private readonly RuleEvaluationEngine _engine = new();

    [Test]
    public void Global_exclusion_stops_application_and_category_evaluation()
    {
        AppSettings settings = CreateSettings();
        settings.Exclusions.Add(new ExclusionDefinition { Include = ["Private", "browser.exe"] });

        RuleEvaluationResult result = _engine.Evaluate("Private browsing", "browser.exe", settings);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsExcluded, Is.True);
            Assert.That(result.MatchedExclusionIndex, Is.Zero);
            Assert.That(result.MatchedApplication, Is.Null);
            Assert.That(result.Categories, Is.Empty);
        });
    }

    [Test]
    public void First_matching_application_wins_and_shadowed_rule_is_traced()
    {
        AppSettings settings = CreateSettings();
        settings.Applications.Insert(0, new ApplicationDefinition { Name = "Broad Browser", Include = ["browser.exe"] });

        RuleEvaluationResult result = _engine.Evaluate("Docs", "browser.exe", settings);

        Assert.Multiple(() =>
        {
            Assert.That(result.MatchedApplication, Is.EqualTo("Broad Browser"));
            Assert.That(result.Warnings.Any(message => message.Contains("shadowed", StringComparison.OrdinalIgnoreCase)), Is.True);
            Assert.That(result.Trace.Any(item => item.Outcome.Contains("earlier rule", StringComparison.OrdinalIgnoreCase)), Is.True);
        });
    }

    [Test]
    public void All_includes_and_no_matching_excludes_are_required_case_insensitively()
    {
        var settings = new AppSettings
        {
            Applications =
            [
                new ApplicationDefinition
                {
                    Name = "Documentation",
                    Include = ["BROWSER.EXE", "docs"],
                    Exclude = ["personal"]
                }
            ]
        };

        RuleEvaluationResult accepted = _engine.Evaluate("Project Docs", "browser.exe", settings);
        RuleEvaluationResult rejected = _engine.Evaluate("Personal Docs", "browser.exe", settings);

        Assert.Multiple(() =>
        {
            Assert.That(accepted.MatchedApplication, Is.EqualTo("Documentation"));
            Assert.That(rejected.MatchedApplication, Is.Null);
            Assert.That(rejected.Trace.Single(item => item.RuleType == "Application").Outcome,
                Does.Contain("exclude"));
        });
    }

    [Test]
    public void Multiple_categories_and_category_exclusions_are_reported()
    {
        AppSettings settings = CreateSettings();
        settings.Categories.Add(new CategoryDefinition
        {
            Name = "Blocked",
            IncludeApplications = ["Browser"],
            ExcludeApplications = ["browser"]
        });

        RuleEvaluationResult result = _engine.Evaluate("Research", "browser.exe", settings);

        Assert.Multiple(() =>
        {
            Assert.That(result.Categories, Is.EquivalentTo(new[] { "Research", "Work" }));
            Assert.That(result.Categories, Does.Not.Contain("Blocked"));
            Assert.That(result.Trace.Single(item => item.RuleName == "Blocked").Outcome,
                Does.Contain("excluded"));
        });
    }

    [Test]
    public void Analyser_and_dashboard_use_the_same_shared_evaluation_result()
    {
        DateTime day = new(2026, 7, 10, 9, 0, 0);
        AppSettings settings = CreateSettings();
        ActivityRecord[] records =
        [
            ActivityRecord.Create(day, string.Empty, "SessionStart"),
            ActivityRecord.Create(day, "Research [browser.exe]", "Active"),
            ActivityRecord.Create(day.AddMinutes(10), string.Empty, "SessionEnd")
        ];

        AnalysisResult report = new AnalysisEngine(settings).Analyze(records.Select(LogEventMapperForTest));
        DashboardSnapshot dashboard = DashboardAnalytics.Build(
            records, [], settings, day, day.AddMinutes(10), day.AddDays(-1), day.AddDays(-1).AddMinutes(10));

        Assert.Multiple(() =>
        {
            Assert.That(report.Applications.Single().Name, Is.EqualTo("Browser"));
            Assert.That(report.Categories.Select(item => item.Name), Is.EquivalentTo(new[] { "Research", "Work" }));
            Assert.That(dashboard.Applications.Single().Name, Is.EqualTo("Browser"));
            Assert.That(dashboard.Categories.Select(item => item.Name), Is.EquivalentTo(new[] { "Research", "Work" }));
        });
    }

    [Test]
    public void Rule_test_cases_round_trip_with_configuration()
    {
        AppSettings settings = CreateSettings();
        settings.RuleTests.Add(new RuleTestCase
        {
            Name = "Unicode and punctuation",
            WindowTitle = "Zażółć, dokument [1]",
            ExecutableName = "browser.exe",
            ExpectedApplication = "Browser",
            ExpectedCategories = "Research, Work"
        });

        AppSettings restored = AppSettingsJson.Deserialize(AppSettingsJson.Serialize(settings));

        Assert.Multiple(() =>
        {
            Assert.That(restored.RuleTests.Single().Name, Is.EqualTo("Unicode and punctuation"));
            Assert.That(restored.RuleTests.Single().ExpectedCategories, Is.EqualTo("Research, Work"));
            Assert.That(ActivityCapturedValue.Format("Zażółć, dokument [1]", "browser.exe"),
                Is.EqualTo("Zażółć, dokument [1] [browser.exe]"));
        });
    }

    private static LogEvent LogEventMapperForTest(ActivityRecord record) => new(
        record.Timestamp,
        record.WindowTitle,
        record.Status,
        record.Kind switch
        {
            ActivityRecordKind.SessionStart => LogEventKind.SessionStart,
            ActivityRecordKind.SessionEnd => LogEventKind.SessionEnd,
            _ => LogEventKind.Usage
        });

    private static AppSettings CreateSettings() => new()
    {
        Applications =
        [
            new ApplicationDefinition { Name = "Browser", Include = ["browser.exe"] }
        ],
        Categories =
        [
            new CategoryDefinition { Name = "Research", IncludeApplications = ["Browser"] },
            new CategoryDefinition { Name = "Work", IncludeApplications = ["browser"] }
        ]
    };
}
