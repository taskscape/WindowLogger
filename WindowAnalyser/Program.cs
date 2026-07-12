using System.Globalization;
using WindowLogger.Shared.Activity;
using WindowLogger.Shared.Configuration;

namespace WindowAnalyser;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && args[0].Equals("export", StringComparison.OrdinalIgnoreCase))
            {
                return ExportCsv(args);
            }

            if (args.Length > 0 && args[0].Equals("import", StringComparison.OrdinalIgnoreCase))
            {
                return ImportCsv(args);
            }

            return GenerateReport(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Operation failed: {ex.Message}");
            return 2;
        }
    }

    private static int GenerateReport(string[] args)
    {
        if (args.Length != 2)
        {
            PrintUsage();
            return 1;
        }

        string settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        AppSettings settings = ConfigurationLoader.Load(settingsPath);
        IEnumerable<LogEvent> events = IsDatabasePath(args[0])
            ? new SqliteLogReader().Read(args[0])
            : new CsvLogReader(Console.Error.WriteLine).Read(args[0]);

        AnalysisResult result = new AnalysisEngine(settings).Analyze(events);
        new WorkbookReportWriter().Write(args[1], result);

        Console.WriteLine($"Analysis complete. Output saved to {args[1]}");
        return 0;
    }

    private static int ExportCsv(string[] args)
    {
        if (args.Length < 3)
        {
            PrintUsage();
            return 1;
        }

        (DateTime? from, DateTime? to) = ParseDateRange(args, 3);
        using var database = new ActivityDatabase(args[1], readOnly: true);
        ActivityCsv.Write(args[2], database.Read(from, to));
        Console.WriteLine($"CSV export saved to {args[2]}");
        return 0;
    }

    private static int ImportCsv(string[] args)
    {
        if (args.Length is < 3 or > 4 ||
            (args.Length == 4 && !args[3].Equals("--replace", StringComparison.OrdinalIgnoreCase)))
        {
            PrintUsage();
            return 1;
        }

        bool replace = args.Length == 4;
        using var database = new ActivityDatabase(args[2]);
        int count = database.Import(ActivityCsv.Read(args[1], Console.Error.WriteLine), replace);
        Console.WriteLine($"Imported {count} records into {args[2]}.");
        return 0;
    }

    private static (DateTime? From, DateTime? To) ParseDateRange(string[] args, int startIndex)
    {
        DateTime? from = null;
        DateTime? to = null;
        for (int index = startIndex; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length)
            {
                throw new ArgumentException($"Missing value for {args[index]}.");
            }

            DateTime value = DateTime.Parse(args[index + 1], CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces);
            if (args[index].Equals("--from", StringComparison.OrdinalIgnoreCase))
            {
                from = value;
            }
            else if (args[index].Equals("--to", StringComparison.OrdinalIgnoreCase))
            {
                to = value;
            }
            else
            {
                throw new ArgumentException($"Unknown option '{args[index]}'.");
            }
        }

        if (from >= to)
        {
            throw new ArgumentException("--from must be earlier than --to.");
        }

        return (from, to);
    }

    private static bool IsDatabasePath(string path) =>
        Path.GetExtension(path).Equals(".db", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(path).Equals(".sqlite", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(path).Equals(".sqlite3", StringComparison.OrdinalIgnoreCase);

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  WindowAnalyser.exe <input.db|input.csv> <output.xlsx>");
        Console.Error.WriteLine("  WindowAnalyser.exe export <input.db> <output.csv> [--from <date>] [--to <exclusive-date>]");
        Console.Error.WriteLine("  WindowAnalyser.exe import <input.csv> <output.db> [--replace]");
    }
}
