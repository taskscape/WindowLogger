using WindowLogger.Shared.Configuration;

namespace WindowAnalyser;

public sealed class AnalysisEngine
{
    private readonly AppSettings _settings;
    private readonly Dictionary<UsageKey, TimeSpan> _applications = [];
    private readonly Dictionary<UsageKey, TimeSpan> _categories = [];
    private readonly Dictionary<UsageKey, TimeSpan> _undefinedApplications = [];
    private readonly Dictionary<DailyWindowKey, TimeSpan> _windows = [];
    private readonly Dictionary<string, RuleEvaluationResult> _evaluations = new(StringComparer.Ordinal);
    private readonly RuleEvaluationEngine _ruleEvaluator = new();

    public AnalysisEngine(AppSettings settings)
    {
        settings.Normalize();
        _settings = settings;
    }

    public AnalysisResult Analyze(IEnumerable<LogEvent> events)
    {
        _applications.Clear();
        _categories.Clear();
        _undefinedApplications.Clear();
        _windows.Clear();
        _evaluations.Clear();

        LogEvent? currentUsage = null;
        DateTime? previousTimestamp = null;

        foreach (LogEvent logEvent in events)
        {
            if (previousTimestamp.HasValue && logEvent.Timestamp < previousTimestamp.Value)
            {
                throw new InvalidDataException("Log records must be ordered chronologically.");
            }
            previousTimestamp = logEvent.Timestamp;

            switch (logEvent.Kind)
            {
                case LogEventKind.SessionStart:
                    currentUsage = null;
                    break;

                case LogEventKind.SessionEnd:
                    if (currentUsage is not null)
                    {
                        AddInterval(currentUsage, logEvent.Timestamp);
                    }
                    currentUsage = null;
                    break;

                case LogEventKind.Usage:
                    if (currentUsage is not null)
                    {
                        AddInterval(currentUsage, logEvent.Timestamp);
                    }
                    currentUsage = logEvent;
                    break;
            }
        }

        return new AnalysisResult
        {
            Categories = CreateUsageSummaries(_categories),
            Applications = CreateUsageSummaries(_applications),
            UndefinedApplications = CreateUsageSummaries(_undefinedApplications),
            Windows = _windows
                .Select(pair => new DailyWindowSummary(
                    pair.Key.Date,
                    pair.Key.Window,
                    pair.Key.Status,
                    RoundMinutes(pair.Value)))
                .OrderBy(summary => summary.Date)
                .ThenByDescending(summary => summary.Minutes)
                .ToList()
        };
    }

    private void AddInterval(LogEvent source, DateTime end)
    {
        RuleEvaluationResult evaluation = Evaluate(source.WindowTitle);
        if (end <= source.Timestamp || evaluation.IsExcluded)
        {
            return;
        }

        DateTime segmentStart = source.Timestamp;
        while (segmentStart < end)
        {
            DateTime segmentEnd = segmentStart.Date < end.Date
                ? segmentStart.Date.AddDays(1)
                : end;
            AddSegment(source, evaluation, segmentStart, segmentEnd - segmentStart);
            segmentStart = segmentEnd;
        }
    }

    private void AddSegment(
        LogEvent source,
        RuleEvaluationResult evaluation,
        DateTime segmentStart,
        TimeSpan duration)
    {
        Add(_windows, new DailyWindowKey(
            segmentStart.Date,
            ExtractAppName(source.WindowTitle),
            source.Status), duration);

        string? applicationName = evaluation.MatchedApplication;
        if (applicationName is null)
        {
            Add(_undefinedApplications, new UsageKey(source.WindowTitle, source.Status), duration);
            return;
        }

        Add(_applications, new UsageKey(applicationName, source.Status), duration);
        foreach (string category in evaluation.Categories)
        {
            Add(_categories, new UsageKey(category, source.Status), duration);
        }
    }

    private RuleEvaluationResult Evaluate(string title)
    {
        if (!_evaluations.TryGetValue(title, out RuleEvaluationResult? evaluation))
        {
            evaluation = _ruleEvaluator.EvaluateCapturedValue(title, _settings);
            _evaluations.Add(title, evaluation);
        }
        return evaluation;
    }

    private static string ExtractAppName(string windowTitle) => windowTitle.Split('-', '—').Last().Trim();

    private static void Add<TKey>(Dictionary<TKey, TimeSpan> totals, TKey key, TimeSpan duration)
        where TKey : notnull
    {
        totals[key] = totals.GetValueOrDefault(key) + duration;
    }

    private static IReadOnlyList<UsageSummary> CreateUsageSummaries(Dictionary<UsageKey, TimeSpan> totals) => totals
        .Select(pair => new UsageSummary(pair.Key.Name, pair.Key.Status, RoundMinutes(pair.Value)))
        .OrderByDescending(summary => summary.Minutes)
        .ToList();

    private static double RoundMinutes(TimeSpan duration) => Math.Round(duration.TotalMinutes, 2);

    private readonly record struct UsageKey(string Name, string Status);
    private readonly record struct DailyWindowKey(DateTime Date, string Window, string Status);
}
