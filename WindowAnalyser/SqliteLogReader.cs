using WindowLogger.Shared.Activity;

namespace WindowAnalyser;

public sealed class SqliteLogReader
{
    public IEnumerable<LogEvent> Read(string path, DateTime? from = null, DateTime? to = null)
    {
        using var database = new ActivityDatabase(path, readOnly: true);
        foreach (ActivityRecord record in database.Read(from, to))
        {
            yield return LogEventMapper.Map(record);
        }
    }
}
