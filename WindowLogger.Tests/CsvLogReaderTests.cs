using WindowAnalyser;

namespace WindowLogger.Tests;

public sealed class CsvLogReaderTests
{
    [Test]
    public void Quoted_title_with_comma_is_preserved()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path,
                "Timestamp,WindowTitle,Status\n" +
                "2026-07-10 09:00:00,,SessionStart\n" +
                "2026-07-10 09:01:00,\"Document, Part 1 [test.exe]\",Active\n" +
                "2026-07-10 09:02:00,,SessionEnd\n");

            List<LogEvent> events = new CsvLogReader().Read(path).ToList();

            Assert.That(events.Single(item => item.Kind == LogEventKind.Usage).WindowTitle,
                Is.EqualTo("Document, Part 1 [test.exe]"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
