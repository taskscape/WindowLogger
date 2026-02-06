﻿using System.Data;
using System.Globalization;
using ClosedXML.Excel;
using Newtonsoft.Json;

namespace WindowAnalyser;

public class AppSettings
{
    public List<ApplicationDefinition> Applications { get; set; } = [];
    public List<ExclusionDefinition> Exclusions { get; set; } = [];
    public List<CategoryDefinition> Categories { get; set; } = [];
}

public class ApplicationDefinition
{
    public string Name { get; set; } = string.Empty;
    public List<string> Include { get; set; } = [];
    public List<string> Exclude { get; set; } = [];
}

public class ExclusionDefinition
{
    public List<string> Include { get; set; } = [];
}

public class CategoryDefinition
{
    public string Name { get; set; } = string.Empty;
    public List<string> IncludeApplications { get; set; } = [];
    public List<string> ExcludeApplications { get; set; } = [];
}

public class TimeEntry
{
    public DateTime DateTime { get; init; }
    public TimeSpan Duration { get; init; } = new(0);
    public required string WindowTitle { get; init; }
    public required string Status { get; init; }
}

internal static class Program
{
    static void Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.WriteLine("Usage: program.exe <input.csv> <output.xlsx>");
            return;
        }

        string inputFile = args[0];
        string outputFile = args[1];

        AppSettings settings;
        try
        {
            settings = JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText("appsettings.json")) ?? new AppSettings();
        }
        catch (Exception)
        {
            Console.WriteLine("Error reading appsettings.json. Using default settings.");
            settings = new AppSettings();
        }

        // Read and parse CSV.
        // Assumes CSV rows in the format:
        // yyyy-MM-dd HH:mm:ss,Window Title,Active/Inactive
        var allLines = File.ReadAllLines(inputFile).Skip(1).ToList(); // Skip header if exists
        var timeEntries = new List<(DateTime DateTime, string WindowTitle, string Status)>();
        
        for (int lineIndex = 0; lineIndex < allLines.Count; lineIndex++)
        {
            string line = allLines[lineIndex];
            string[] parts = line.Split(',');
            if (parts.Length < 3)
            {
                Console.WriteLine($"Warning: Skipping line {lineIndex + 2} (invalid format, expected 3 columns): {line}");
                continue;
            }
            
            if (!DateTime.TryParseExact(parts[0].Trim(), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dateTime))
            {
                Console.WriteLine($"Warning: Skipping line {lineIndex + 2} (invalid date format): {line}");
                continue;
            }
            
            timeEntries.Add((dateTime, parts[1].Trim(), parts[2].Trim()));
        }
        
        timeEntries = timeEntries.OrderBy(x => x.DateTime).ToList();

        List<TimeEntry> entries = [];

        for (int i = 0; i < timeEntries.Count; i++)
        {
            if (IsExcluded(timeEntries[i].WindowTitle, settings.Exclusions))
            {
                continue;
            }

            if (i >= timeEntries.Count - 1)
            {
                entries.Add(new TimeEntry
                {
                    DateTime = timeEntries[i].DateTime,
                    Duration = new TimeSpan(0),
                    WindowTitle = timeEntries[i].WindowTitle,
                    Status = timeEntries[i].Status
                });
            }
            else
            {
                entries.Add(new TimeEntry
                {
                    DateTime = timeEntries[i].DateTime,
                    Duration = new TimeSpan(Math.Abs(timeEntries[i + 1].DateTime.Ticks - timeEntries[i].DateTime.Ticks)),
                    WindowTitle = timeEntries[i].WindowTitle,
                    Status = timeEntries[i].Status
                });
            }
            
        }

        // Group by Date, Application (extracted from WindowTitle), and Status. (WINDOWS tab)
        var dailyAppUsage = entries
            .GroupBy(x => new
            {
                Date = x.DateTime.Date,
                App = ExtractAppName(x.WindowTitle),
                Status = x.Status
            })
            .Select(g => new
            {
                Date = g.Key.Date,
                Application = g.Key.App,
                Status = g.Key.Status,
                TimeSpentMinutes = CalculateTimeSpent(g.Select(x => x.Duration).ToList())
            })
            .OrderBy(x => x.Date)
            .ThenByDescending(x => x.TimeSpentMinutes);

        var categorizedEntries = entries.Select(entry =>
        {
            string? appName = MatchApplication(entry.WindowTitle, settings.Applications);
            if (appName == null)
            {
                return null;
            }
            List<string> categories = GetCategories(appName, settings.Categories);
            return new
            {
                entry.Duration,
                entry.Status,
                AppName = appName,
                Categories = categories,
                WindowTitle = entry.WindowTitle
            };
        }).Where(x=>x!=null).ToList();

        var groupedApps = categorizedEntries
            .GroupBy(x => new { x!.AppName, x.Status })
            .Select(g => new
            {
                Application = g.Key.AppName,
                Status = g.Key.Status,
                TimeSpentMinutes = CalculateTimeSpent(g.Select(x => x!.Duration).ToList())
            })
            .OrderByDescending(x => x.TimeSpentMinutes)
            .ToList();

        var groupedCategories = categorizedEntries
            .SelectMany(x => x!.Categories.Select(cat => new { Category = cat, x.Duration, x.Status }))
            .GroupBy(x => new { x.Category, x.Status })
            .Select(g => new
            {
                Category = g.Key.Category,
                Status = g.Key.Status,
                TimeSpentMinutes = CalculateTimeSpent(g.Select(x => x.Duration).ToList())
            })
            .OrderByDescending(x => x.TimeSpentMinutes)
            .ToList();

        var otherWindows = entries.Select(entry =>
        {
            string? appName = MatchApplication(entry.WindowTitle, settings.Applications);
            if (appName != null) return null;

            appName = entry.WindowTitle;
            List<string> categories = GetCategories(appName, settings.Categories);
            return new
            {
                entry.Duration,
                entry.Status,
                AppName = appName,
                Categories = categories,
                WindowTitle = entry.WindowTitle
            };
        }).Where(x => x != null).ToList()
            .GroupBy(x => new { x!.WindowTitle, x.Status })
            .Select(g => new
            {
                Window = g.Key.WindowTitle,
                Status = g.Key.Status,
                TimeSpentMinutes = CalculateTimeSpent(g.Select(x => x!.Duration).ToList())
            })
            .OrderByDescending(x => x.TimeSpentMinutes)
            .ToList();

        using (XLWorkbook workbook = new XLWorkbook())
        {
            WriteWorksheet(workbook, "Categories", groupedCategories.Select(x => new object[] { x.Category, x.Status, x.TimeSpentMinutes, Math.Round(x.TimeSpentMinutes / 60.0, 2) }));
            WriteWorksheet(workbook, "Applications", groupedApps.Select(x => new object[] { x.Application, x.Status, x.TimeSpentMinutes, Math.Round(x.TimeSpentMinutes / 60.0, 2) }));
            WriteWorksheet(workbook, "Undefined Applications", otherWindows.Select(x => new object[] { x.Window, x.Status, x.TimeSpentMinutes, Math.Round(x.TimeSpentMinutes / 60.0, 2) }));
            WriteWorksheet(workbook, "Windows", dailyAppUsage.Select(x => new object[] { x.Date, x.Application, x.Status, x.TimeSpentMinutes, Math.Round(x.TimeSpentMinutes / 60.0, 2) }), true);
            workbook.SaveAs(outputFile);
        }

        Console.WriteLine($"Analysis complete. Output saved to {outputFile}");
    }

    private static bool IsExcluded(string title, List<ExclusionDefinition> exclusions)
    {
        return exclusions.Any(ex =>
            ex.Include.All(phrase =>
                title.IndexOf(phrase, StringComparison.OrdinalIgnoreCase) >= 0));
    }

    private static string? MatchApplication(string title, List<ApplicationDefinition> apps)
    {
        foreach (ApplicationDefinition app in apps)
        {
            bool includes = app.Include.All(phrase =>
                title.IndexOf(phrase, StringComparison.OrdinalIgnoreCase) >= 0);

            bool excludes = app.Exclude?.Any(phrase =>
                title.IndexOf(phrase, StringComparison.OrdinalIgnoreCase) >= 0) ?? false;

            if (includes && !excludes)
                return app.Name;
        }
        return null;
    }

    static List<string> GetCategories(string appName, List<CategoryDefinition> categories)
    {
        List<string> matched = [];
        foreach (CategoryDefinition category in categories)
        {
            bool included = category.IncludeApplications?.Any(a =>
                appName.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0) ?? false;

            bool excluded = category.ExcludeApplications?.Any(a =>
                appName.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0) ?? false;

            if (included && !excluded)
                matched.Add(category.Name);
        }
        return matched;
    }

    private static string ExtractAppName(string windowTitle) //TODO improve this
    {
        // Simple extraction - you might want to enhance this based on your window title format
#pragma warning disable S3220 // Method calls should not resolve ambiguously to overloads with "params"
        return windowTitle.Split('-', '—').Last().Trim();
#pragma warning restore S3220 // Method calls should not resolve ambiguously to overloads with "params"
    }

    private static double CalculateTimeSpent(List<TimeSpan> timestamps)
    {
        if (timestamps.Count < 2)
            return 0;

        double totalMinutes = timestamps.Sum(t => t.TotalMinutes);
        return Math.Round(totalMinutes, 2);
    }
    private static void WriteWorksheet(XLWorkbook workbook, string sheetName, IEnumerable<object[]> rows, bool DisplayDate = false)
    {
        IXLWorksheet worksheet = workbook.Worksheets.Add(sheetName);

        int dateCol = 0;
        if(DisplayDate)
        {
            worksheet.Cell(1, 1).Value = "Date";
            dateCol = 1;
        }

        if (sheetName.Contains("Category"))
        {
            worksheet.Cell(1, 1 + dateCol).Value = sheetName.Contains("Window") ? "Window" : "Category";
        }
        else
        {
            worksheet.Cell(1, 1 + dateCol).Value = sheetName.Contains("Window") ? "Window" : "Application";
        }

        worksheet.Cell(1, 2 + dateCol).Value = "Status";
        worksheet.Cell(1, 3 + dateCol).Value = "Time Spent (Minutes)";
        worksheet.Cell(1, 4 + dateCol).Value = "Time Spent (Hours)";

        int row = 2;
        foreach (object[] dataRow in rows)
        {
            for (int col = 0; col < dataRow.Length; col++)
            {
                if (dataRow[col] is double)
                    worksheet.Cell(row, col + 1).Value = Math.Round((double)dataRow[col], 2);
                else worksheet.Cell(row, col + 1).Value = dataRow[col] is DateTime ? (XLCellValue)((DateTime)dataRow[col]).ToString("yyyy-MM-dd") : (XLCellValue)dataRow[col].ToString();
            }
            row++;
        }

        IXLRange range = worksheet.Range(1, 1, row - 1, 4 + +dateCol);
        range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        worksheet.Columns().AdjustToContents();
    }
}
