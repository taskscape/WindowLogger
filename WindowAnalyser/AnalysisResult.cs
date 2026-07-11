namespace WindowAnalyser;

public sealed record UsageSummary(string Name, string Status, double Minutes);

public sealed record DailyWindowSummary(DateTime Date, string Window, string Status, double Minutes);

public sealed class AnalysisResult
{
    public required IReadOnlyList<UsageSummary> Categories { get; init; }
    public required IReadOnlyList<UsageSummary> Applications { get; init; }
    public required IReadOnlyList<UsageSummary> UndefinedApplications { get; init; }
    public required IReadOnlyList<DailyWindowSummary> Windows { get; init; }
}
