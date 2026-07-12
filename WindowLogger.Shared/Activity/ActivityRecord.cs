namespace WindowLogger.Shared.Activity;

public enum ActivityRecordKind
{
    Usage,
    SessionStart,
    SessionEnd
}

public sealed record ActivityRecord(
    DateTime Timestamp,
    string WindowTitle,
    string Status,
    ActivityRecordKind Kind)
{
    public static ActivityRecord Create(DateTime timestamp, string windowTitle, string status)
    {
        string normalizedStatus = status.Trim() switch
        {
            var value when value.Equals("Active", StringComparison.OrdinalIgnoreCase) => "Active",
            var value when value.Equals("Inactive", StringComparison.OrdinalIgnoreCase) => "Inactive",
            var value when value.Equals("SessionStart", StringComparison.OrdinalIgnoreCase) => "SessionStart",
            var value when value.Equals("SessionEnd", StringComparison.OrdinalIgnoreCase) => "SessionEnd",
            _ => throw new ArgumentException($"Unknown activity status '{status}'.", nameof(status))
        };

        ActivityRecordKind kind = normalizedStatus switch
        {
            "SessionStart" => ActivityRecordKind.SessionStart,
            "SessionEnd" => ActivityRecordKind.SessionEnd,
            _ => ActivityRecordKind.Usage
        };

        return new ActivityRecord(timestamp, windowTitle, normalizedStatus, kind);
    }
}
