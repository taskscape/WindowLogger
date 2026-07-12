using WindowLogger.Shared.Activity;

namespace WindowAnalyser;

internal static class LogEventMapper
{
    public static LogEvent Map(ActivityRecord record) => new(
        record.Timestamp,
        record.WindowTitle,
        record.Status,
        record.Kind switch
        {
            ActivityRecordKind.SessionStart => LogEventKind.SessionStart,
            ActivityRecordKind.SessionEnd => LogEventKind.SessionEnd,
            _ => LogEventKind.Usage
        });
}
