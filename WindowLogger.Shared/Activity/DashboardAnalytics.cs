using WindowLogger.Shared.Configuration;

namespace WindowLogger.Shared.Activity;

public sealed record TimelineBlock(
    DateTime Start,
    DateTime End,
    string Status,
    string Application,
    string WindowTitle,
    IReadOnlyList<string> Categories)
{
    public double Minutes => (End - Start).TotalMinutes;
}

public sealed record DashboardBreakdown(
    string Name,
    double ActiveMinutes,
    double IdleMinutes,
    double PreviousMinutes)
{
    public double TotalMinutes => ActiveMinutes + IdleMinutes;
    public double? ChangePercent => PreviousMinutes <= 0
        ? TotalMinutes <= 0 ? 0 : null
        : (TotalMinutes - PreviousMinutes) / PreviousMinutes * 100;
}

public sealed class DashboardSnapshot
{
    public required IReadOnlyList<TimelineBlock> Timeline { get; init; }
    public required IReadOnlyList<TimelineBlock> PreviousTimeline { get; init; }
    public required IReadOnlyList<DashboardBreakdown> Categories { get; init; }
    public required IReadOnlyList<DashboardBreakdown> Applications { get; init; }
    public double ActiveMinutes => Timeline.Where(block => block.Status == "Active").Sum(block => block.Minutes);
    public double IdleMinutes => Timeline.Where(block => block.Status == "Inactive").Sum(block => block.Minutes);
    public double PreviousMinutes => PreviousTimeline.Sum(block => block.Minutes);
}

public static class DashboardAnalytics
{
    public static DashboardSnapshot Build(
        IEnumerable<ActivityRecord> currentRecords,
        IEnumerable<ActivityRecord> previousRecords,
        AppSettings settings,
        DateTime rangeStart,
        DateTime rangeEnd,
        DateTime previousStart,
        DateTime previousEnd,
        string? search = null,
        string? status = null)
    {
        settings.Normalize();
        List<TimelineBlock> current = BuildTimeline(currentRecords, settings, rangeStart, rangeEnd);
        List<TimelineBlock> previous = BuildTimeline(previousRecords, settings, previousStart, previousEnd);
        current = ApplyFilters(current, search, status);
        previous = ApplyFilters(previous, search, status);

        return new DashboardSnapshot
        {
            Timeline = current,
            PreviousTimeline = previous,
            Categories = BuildBreakdown(
                current.SelectMany(block => block.Categories.Select(category => (category, block))),
                previous.SelectMany(block => block.Categories.Select(category => (category, block)))),
            Applications = BuildBreakdown(
                current.Select(block => (block.Application, block)),
                previous.Select(block => (block.Application, block)))
        };
    }

    private static List<TimelineBlock> BuildTimeline(
        IEnumerable<ActivityRecord> records,
        AppSettings settings,
        DateTime rangeStart,
        DateTime rangeEnd)
    {
        var blocks = new List<TimelineBlock>();
        ActivityRecord? currentUsage = null;
        foreach (ActivityRecord record in records)
        {
            switch (record.Kind)
            {
                case ActivityRecordKind.SessionStart:
                    currentUsage = null;
                    break;
                case ActivityRecordKind.SessionEnd:
                    AddBlock(currentUsage, record.Timestamp, settings, rangeStart, rangeEnd, blocks);
                    currentUsage = null;
                    break;
                case ActivityRecordKind.Usage:
                    AddBlock(currentUsage, record.Timestamp, settings, rangeStart, rangeEnd, blocks);
                    currentUsage = record;
                    break;
            }
        }
        AddBlock(currentUsage, rangeEnd, settings, rangeStart, rangeEnd, blocks);
        return blocks;
    }

    private static void AddBlock(
        ActivityRecord? usage,
        DateTime eventEnd,
        AppSettings settings,
        DateTime rangeStart,
        DateTime rangeEnd,
        ICollection<TimelineBlock> blocks)
    {
        if (usage is null || IsExcluded(usage.WindowTitle, settings))
        {
            return;
        }
        DateTime start = usage.Timestamp < rangeStart ? rangeStart : usage.Timestamp;
        DateTime end = eventEnd > rangeEnd ? rangeEnd : eventEnd;
        if (end <= start)
        {
            return;
        }

        string application = MatchApplication(usage.WindowTitle, settings) ?? "Unmatched";
        string[] categories = settings.Categories
            .Where(category => category.IncludeApplications.Contains(application, StringComparer.OrdinalIgnoreCase) &&
                               !category.ExcludeApplications.Contains(application, StringComparer.OrdinalIgnoreCase))
            .Select(category => category.Name)
            .DefaultIfEmpty("Uncategorized")
            .ToArray();
        blocks.Add(new TimelineBlock(start, end, usage.Status, application, usage.WindowTitle, categories));
    }

    private static List<TimelineBlock> ApplyFilters(
        IEnumerable<TimelineBlock> blocks,
        string? search,
        string? status)
    {
        string query = search?.Trim() ?? string.Empty;
        return blocks.Where(block =>
                (string.IsNullOrEmpty(query) ||
                 block.WindowTitle.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                 block.Application.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                 block.Categories.Any(category => category.Contains(query, StringComparison.OrdinalIgnoreCase))) &&
                (string.IsNullOrWhiteSpace(status) || status == "All" || block.Status == status))
            .ToList();
    }

    private static IReadOnlyList<DashboardBreakdown> BuildBreakdown(
        IEnumerable<(string Name, TimelineBlock Block)> current,
        IEnumerable<(string Name, TimelineBlock Block)> previous)
    {
        Dictionary<string, TimelineBlock[]> currentGroups = current
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Block).ToArray(), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, double> previousGroups = previous
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Block.Minutes), StringComparer.OrdinalIgnoreCase);

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
            .ThenBy(item => item.Name)
            .ToList();
    }

    private static bool IsExcluded(string title, AppSettings settings) => settings.Exclusions.Any(exclusion =>
        exclusion.Include.Count > 0 &&
        exclusion.Include.All(pattern => title.Contains(pattern, StringComparison.OrdinalIgnoreCase)));

    private static string? MatchApplication(string title, AppSettings settings) => settings.Applications
        .FirstOrDefault(application =>
            application.Include.Count > 0 &&
            application.Include.All(pattern => title.Contains(pattern, StringComparison.OrdinalIgnoreCase)) &&
            !application.Exclude.Any(pattern => title.Contains(pattern, StringComparison.OrdinalIgnoreCase)))?.Name;
}
