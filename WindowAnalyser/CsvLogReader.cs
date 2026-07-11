using System.Globalization;
using System.Text;
using Microsoft.VisualBasic.FileIO;

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
        using var parser = new TextFieldParser(path, Encoding.UTF8, detectEncoding: true)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");

        bool firstRecord = true;
        while (!parser.EndOfData)
        {
            string[]? fields;
            try
            {
                fields = parser.ReadFields();
            }
            catch (MalformedLineException ex)
            {
                Warn($"Skipping malformed CSV record near line {ex.LineNumber}: {ex.Message}");
                continue;
            }

            if (fields is null || fields.Length == 0)
            {
                continue;
            }

            if (firstRecord)
            {
                firstRecord = false;
                if (fields.Length >= 3 &&
                    string.Equals(fields[0].TrimStart('\uFEFF'), "Timestamp", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            if (fields.Length != 3)
            {
                Warn($"Skipping record near line {parser.LineNumber}: expected 3 columns, found {fields.Length}.");
                continue;
            }

            if (!DateTime.TryParseExact(
                    fields[0].Trim(),
                    "yyyy-MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateTime timestamp))
            {
                Warn($"Skipping record near line {parser.LineNumber}: invalid timestamp.");
                continue;
            }

            string status = fields[2].Trim();
            LogEventKind? kind = status.ToUpperInvariant() switch
            {
                "ACTIVE" or "INACTIVE" => LogEventKind.Usage,
                "SESSIONSTART" => LogEventKind.SessionStart,
                "SESSIONEND" => LogEventKind.SessionEnd,
                _ => null
            };
            if (kind is null)
            {
                Warn($"Skipping record near line {parser.LineNumber}: unknown status '{status}'.");
                continue;
            }

            string normalizedStatus = kind == LogEventKind.Usage
                ? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(status.ToLowerInvariant())
                : status;
            yield return new LogEvent(timestamp, fields[1].Trim(), normalizedStatus, kind.Value);
        }
    }

    private void Warn(string message) => _warningSink?.Invoke($"Warning: {message}");
}
