using WindowAnalyser;
using WindowLogger.Shared.Configuration;

namespace WindowLogger.Tests;

public sealed class AnalysisEngineTests
{
    [Test]
    public void SessionMarkers_prevent_downtime_from_being_counted()
    {
        DateTime firstDay = new(2026, 7, 10, 0, 0, 0);
        DateTime secondDay = firstDay.AddDays(1);
        LogEvent[] events =
        [
            Start(firstDay.AddHours(16).AddMinutes(59)),
            Usage(firstDay.AddHours(17), "First session [one.exe]"),
            End(firstDay.AddHours(17).AddMinutes(30)),
            Start(secondDay.AddHours(8).AddMinutes(59)),
            Usage(secondDay.AddHours(9), "Second session [two.exe]"),
            End(secondDay.AddHours(9).AddMinutes(10))
        ];

        AnalysisResult result = new AnalysisEngine(new AppSettings()).Analyze(events);

        Assert.Multiple(() =>
        {
            Assert.That(result.UndefinedApplications.Single(row => row.Name.StartsWith("First")).Minutes, Is.EqualTo(30));
            Assert.That(result.UndefinedApplications.Single(row => row.Name.StartsWith("Second")).Minutes, Is.EqualTo(10));
            Assert.That(result.UndefinedApplications.Sum(row => row.Minutes), Is.EqualTo(40));
        });
    }

    [Test]
    public void Interval_crossing_midnight_is_split_between_days()
    {
        DateTime day = new(2026, 7, 10, 0, 0, 0);
        LogEvent[] events =
        [
            Start(day.AddHours(23).AddMinutes(54)),
            Usage(day.AddHours(23).AddMinutes(55), "Document [test.exe]"),
            End(day.AddDays(1).AddMinutes(15))
        ];

        AnalysisResult result = new AnalysisEngine(new AppSettings()).Analyze(events);

        Assert.Multiple(() =>
        {
            Assert.That(result.Windows.Single(row => row.Date == day).Minutes, Is.EqualTo(5));
            Assert.That(result.Windows.Single(row => row.Date == day.AddDays(1)).Minutes, Is.EqualTo(15));
        });
    }

    private static LogEvent Start(DateTime timestamp) =>
        new(timestamp, string.Empty, "SessionStart", LogEventKind.SessionStart);

    private static LogEvent End(DateTime timestamp) =>
        new(timestamp, string.Empty, "SessionEnd", LogEventKind.SessionEnd);

    private static LogEvent Usage(DateTime timestamp, string title) =>
        new(timestamp, title, "Active", LogEventKind.Usage);
}
