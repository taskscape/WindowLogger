using System.Globalization;
using System.Text;
using Microsoft.VisualBasic.FileIO;

namespace WindowLogger.Shared.Activity;

public static class ActivityCsv
{
    private const string TimestampFormat = "yyyy-MM-dd HH:mm:ss";

    public static IEnumerable<ActivityRecord> Read(string path, Action<string>? warningSink = null)
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
                Warn(warningSink, $"Skipping malformed CSV record near line {ex.LineNumber}: {ex.Message}");
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
                Warn(warningSink, $"Skipping record near line {parser.LineNumber}: expected 3 columns, found {fields.Length}.");
                continue;
            }

            if (!DateTime.TryParseExact(
                    fields[0].Trim(),
                    TimestampFormat,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateTime timestamp))
            {
                Warn(warningSink, $"Skipping record near line {parser.LineNumber}: invalid timestamp.");
                continue;
            }

            ActivityRecord record;
            try
            {
                record = ActivityRecord.Create(timestamp, fields[1].Trim(), fields[2]);
            }
            catch (ArgumentException ex)
            {
                Warn(warningSink, $"Skipping record near line {parser.LineNumber}: {ex.Message}");
                continue;
            }

            yield return record;
        }
    }

    public static void Write(string path, IEnumerable<ActivityRecord> records)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var writer = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.WriteLine("Timestamp,WindowTitle,Status");
        foreach (ActivityRecord record in records)
        {
            writer.WriteLine(string.Join(",",
                Escape(record.Timestamp.ToString(TimestampFormat, CultureInfo.InvariantCulture)),
                Escape(record.WindowTitle),
                Escape(record.Status)));
        }
    }

    private static string Escape(string value) =>
        value.IndexOfAny([',', '"', '\r', '\n']) < 0
            ? value
            : $"\"{value.Replace("\"", "\"\"")}\"";

    private static void Warn(Action<string>? warningSink, string message) =>
        warningSink?.Invoke($"Warning: {message}");
}
