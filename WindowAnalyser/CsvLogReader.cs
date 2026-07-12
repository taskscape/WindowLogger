using WindowLogger.Shared.Activity;

namespace WindowAnalyser;

public sealed class CsvLogReader
{
    private readonly Action<string>? _warningSink;

    public CsvLogReader(Action<string>? warningSink = null)
    {
        _warningSink = warningSink;
    }

    public IEnumerable<LogEvent> Read(string path)
    {
        foreach (ActivityRecord record in ActivityCsv.Read(path, _warningSink))
        {
            yield return LogEventMapper.Map(record);
        }
    }
}
