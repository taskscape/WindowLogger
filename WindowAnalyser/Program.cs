using WindowLogger.Shared.Configuration;

namespace WindowAnalyser;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: WindowAnalyser.exe <input.csv> <output.xlsx>");
            return 1;
        }

        try
        {
            string settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            AppSettings settings = ConfigurationLoader.Load(settingsPath);

            var reader = new CsvLogReader(message => Console.Error.WriteLine(message));
            var engine = new AnalysisEngine(settings);
            AnalysisResult result = engine.Analyze(reader.Read(args[0]));
            new WorkbookReportWriter().Write(args[1], result);

            Console.WriteLine($"Analysis complete. Output saved to {args[1]}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Analysis failed: {ex.Message}");
            return 2;
        }
    }
}
