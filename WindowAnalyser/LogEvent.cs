namespace WindowAnalyser;

public enum LogEventKind
{
    Usage,
    SessionStart,
    SessionEnd
}

public sealed record LogEvent(
    DateTime Timestamp,
    string WindowTitle,
    string Status,
    LogEventKind Kind);
