using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using ClosedXML.Excel;
using System.Globalization;

class Program
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

        // Read and parse CSV
        var timeEntries = File.ReadAllLines(inputFile)
            .Skip(1) // Skip header if exists
            .Select(line =>
            {
                var parts = line.Split(',');
                return new
                {
                    DateTime = DateTime.ParseExact(parts[0].Trim(), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    WindowTitle = parts[1].Trim()
                };
            })
            .OrderBy(x => x.DateTime)
            .ToList();

        // Group by date and application
        var dailyAppUsage = timeEntries
            .GroupBy(x => new { Date = x.DateTime.Date, App = ExtractAppName(x.WindowTitle) })
            .Select(g => new
            {
                Date = g.Key.Date,
                Application = g.Key.App,
                TimeSpentMinutes = CalculateTimeSpent(g.Select(x => x.DateTime).ToList())
            })
            .OrderBy(x => x.Date)
            .ThenByDescending(x => x.TimeSpentMinutes);

        // Create Excel workbook
        using (var workbook = new XLWorkbook())
        {
            var worksheet = workbook.Worksheets.Add("App Usage");

            // Add headers
            worksheet.Cell(1, 1).Value = "Date";
            worksheet.Cell(1, 2).Value = "Application";
            worksheet.Cell(1, 3).Value = "Time Spent (Minutes)";
            worksheet.Cell(1, 4).Value = "Time Spent (Hours)";

            // Add data
            int row = 2;
            foreach (var entry in dailyAppUsage)
            {
                worksheet.Cell(row, 1).Value = entry.Date.ToString("yyyy-MM-dd");
                worksheet.Cell(row, 2).Value = entry.Application;
                worksheet.Cell(row, 3).Value = entry.TimeSpentMinutes;
                worksheet.Cell(row, 4).Value = Math.Round(entry.TimeSpentMinutes / 60.0, 2);
                row++;
            }

            // Format worksheet
            var range = worksheet.Range(1, 1, row - 1, 4);
            range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            worksheet.Columns().AdjustToContents();

            // Save workbook
            workbook.SaveAs(outputFile);
        }

        Console.WriteLine($"Analysis complete. Output saved to {outputFile}");
    }

    static string ExtractAppName(string windowTitle)
    {
        // Simple extraction - you might want to enhance this based on your window title format
        return windowTitle.Split('-', '—').Last().Trim();
    }

    static double CalculateTimeSpent(List<DateTime> timestamps)
    {
        if (timestamps.Count < 2)
            return 0;

        double totalMinutes = 0;
        for (int i = 0; i < timestamps.Count - 1; i++)
        {
            var diff = timestamps[i + 1] - timestamps[i];
            // Only count intervals less than 5 minutes as active time
            if (diff.TotalMinutes < 5)
            {
                totalMinutes += diff.TotalMinutes;
            }
        }

        return Math.Round(totalMinutes, 2);
    }
}
