using WindowLogger.Shared.Activity;
using WindowLogger.Shared.Configuration;

namespace WindowLogger.Tests;

public sealed class DashboardAnalyticsTests
{
    [Test]
    public void Range_context_preserves_activity_crossing_the_start_boundary()
    {
        string path = Path.Combine(Path.GetTempPath(), $"WindowLogger-dashboard-{Guid.NewGuid():N}.db");
        try
        {
            DateTime day = new(2026, 7, 10);
            using var database = new ActivityDatabase(path);
            database.Append(ActivityRecord.Create(day.AddHours(8), string.Empty, "SessionStart"));
            database.Append(ActivityRecord.Create(day.AddHours(8.5), "Editor [code.exe]", "Active"));
            database.Append(ActivityRecord.Create(day.AddHours(9.25), "Browser [browser.exe]", "Active"));
            database.Append(ActivityRecord.Create(day.AddHours(10), string.Empty, "SessionEnd"));

            List<ActivityRecord> records = database.ReadRangeWithContext(day.AddHours(9), day.AddHours(10)).ToList();

            Assert.Multiple(() =>
            {
                Assert.That(records.First().WindowTitle, Is.EqualTo("Editor [code.exe]"));
                Assert.That(records.First().Timestamp, Is.EqualTo(day.AddHours(8.5)));
                Assert.That(records.Last().WindowTitle, Is.EqualTo("Browser [browser.exe]"));
                Assert.That(records.Count, Is.EqualTo(2));
            });
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Test]
    public void Dashboard_builds_timeline_comparison_filters_and_hierarchy()
    {
        DateTime day = new(2026, 7, 10);
        AppSettings settings = CreateSettings();
        ActivityRecord[] current =
        [
            ActivityRecord.Create(day.AddMinutes(-10), "Editor [code.exe]", "Active"),
            ActivityRecord.Create(day.AddMinutes(10), "Browser - Docs [browser.exe]", "Active"),
            ActivityRecord.Create(day.AddMinutes(20), "Browser - Docs [browser.exe]", "Inactive"),
            ActivityRecord.Create(day.AddMinutes(30), string.Empty, "SessionEnd")
        ];
        ActivityRecord[] previous =
        [
            ActivityRecord.Create(day.AddDays(-1), "Editor [code.exe]", "Active"),
            ActivityRecord.Create(day.AddDays(-1).AddMinutes(15), string.Empty, "SessionEnd")
        ];

        DashboardSnapshot snapshot = DashboardAnalytics.Build(
            current, previous, settings,
            day, day.AddMinutes(30), day.AddDays(-1), day.AddDays(-1).AddMinutes(30));
        DashboardSnapshot filtered = DashboardAnalytics.Build(
            current, previous, settings,
            day, day.AddMinutes(30), day.AddDays(-1), day.AddDays(-1).AddMinutes(30),
            search: "Browser", status: "Active");

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Timeline.Count, Is.EqualTo(3));
            Assert.That(snapshot.ActiveMinutes, Is.EqualTo(20));
            Assert.That(snapshot.IdleMinutes, Is.EqualTo(10));
            Assert.That(snapshot.PreviousMinutes, Is.EqualTo(15));
            Assert.That(snapshot.Categories.Single(item => item.Name == "Development").TotalMinutes, Is.EqualTo(10));
            Assert.That(snapshot.Categories.Single(item => item.Name == "Research").TotalMinutes, Is.EqualTo(20));
            Assert.That(snapshot.Applications.Single(item => item.Name == "Browser").IdleMinutes, Is.EqualTo(10));
            Assert.That(filtered.Timeline.Count, Is.EqualTo(1));
            Assert.That(filtered.ActiveMinutes, Is.EqualTo(10));
        });
    }

    [Test]
    public void Time_selection_clips_durations_and_rebuilds_current_and_previous_breakdowns()
    {
        DateTime day = new(2026, 11, 2);
        ActivityRecord[] current =
        [
            ActivityRecord.Create(day.AddHours(8), "Editor [code.exe]", "Active"),
            ActivityRecord.Create(day.AddHours(9), "Browser [browser.exe]", "Active"),
            ActivityRecord.Create(day.AddHours(10), "Browser [browser.exe]", "Inactive"),
            ActivityRecord.Create(day.AddHours(11), "Editor [code.exe]", "Active"),
            ActivityRecord.Create(day.AddHours(12), string.Empty, "SessionEnd")
        ];
        ActivityRecord[] previous =
        [
            ActivityRecord.Create(day.AddDays(-1).AddHours(8), "Editor [code.exe]", "Active"),
            ActivityRecord.Create(day.AddDays(-1).AddHours(10), "Browser [browser.exe]", "Inactive"),
            ActivityRecord.Create(day.AddDays(-1).AddHours(12), string.Empty, "SessionEnd")
        ];
        DashboardSnapshot snapshot = DashboardAnalytics.Build(current, previous, CreateSettings(),
            day, day.AddDays(1), day.AddDays(-1), day);
        DashboardSnapshot selected = DashboardAnalytics.SelectTimeRange(snapshot,
            day.AddHours(8.5), day.AddHours(10.5), day.AddDays(-1).AddHours(8.5), day.AddDays(-1).AddHours(10.5));

        Assert.Multiple(() =>
        {
            Assert.That(selected.Timeline.First().Start, Is.EqualTo(day.AddHours(8.5)));
            Assert.That(selected.Timeline.Last().End, Is.EqualTo(day.AddHours(10.5)));
            Assert.That(selected.ActiveMinutes, Is.EqualTo(90));
            Assert.That(selected.IdleMinutes, Is.EqualTo(30));
            Assert.That(selected.PreviousMinutes, Is.EqualTo(120));
            Assert.That(selected.Categories.Single(item => item.Name == "Development").TotalMinutes, Is.EqualTo(30));
            Assert.That(selected.Categories.Single(item => item.Name == "Development").PreviousMinutes, Is.EqualTo(90));
            Assert.That(selected.Applications.Single(item => item.Name == "Browser").TotalMinutes, Is.EqualTo(90));
            Assert.That(selected.Applications.Single(item => item.Name == "Browser").PreviousMinutes, Is.EqualTo(30));
            Assert.That(snapshot.Timeline.First().Start, Is.EqualTo(day.AddHours(8)));
            Assert.That(snapshot.ActiveMinutes + snapshot.IdleMinutes, Is.EqualTo(240));
        });

        DashboardSnapshot boundary = DashboardAnalytics.SelectTimeRange(snapshot,
            day.AddHours(9), day.AddHours(10), day.AddDays(-1).AddHours(9), day.AddDays(-1).AddHours(10));
        Assert.That(boundary.Timeline.Single().Application, Is.EqualTo("Browser"));
        Assert.That(boundary.IdleMinutes, Is.Zero, "An activity beginning at the end boundary is outside the selection.");

        DashboardSnapshot empty = DashboardAnalytics.SelectTimeRange(snapshot,
            day.AddHours(18), day.AddHours(19), day.AddDays(-1).AddHours(18), day.AddDays(-1).AddHours(19));
        Assert.That(empty.Timeline, Is.Empty);
        Assert.That(empty.Categories, Is.Empty);
        Assert.That(empty.Applications, Is.Empty);
    }

    private static AppSettings CreateSettings() => new()
    {
        Applications =
        [
            new ApplicationDefinition { Name = "Editor", Include = ["code.exe"] },
            new ApplicationDefinition { Name = "Browser", Include = ["browser.exe"] }
        ],
        Categories =
        [
            new CategoryDefinition { Name = "Development", IncludeApplications = ["Editor"] },
            new CategoryDefinition { Name = "Research", IncludeApplications = ["Browser"] }
        ]
    };

    private static void DeleteDatabase(string path)
    {
        foreach (string candidate in new[] { path, $"{path}-wal", $"{path}-shm" })
        {
            File.Delete(candidate);
        }
    }
}
