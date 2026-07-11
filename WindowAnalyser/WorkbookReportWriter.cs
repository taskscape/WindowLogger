using ClosedXML.Excel;

namespace WindowAnalyser;

public sealed class WorkbookReportWriter
{
    public void Write(string outputPath, AnalysisResult result)
    {
        using var workbook = new XLWorkbook();
        WriteUsageSheet(workbook, "Categories", "Category", result.Categories);
        WriteUsageSheet(workbook, "Applications", "Application", result.Applications);
        WriteUsageSheet(workbook, "Undefined Applications", "Application", result.UndefinedApplications);
        WriteWindowsSheet(workbook, result.Windows);
        workbook.SaveAs(outputPath);
    }

    private static void WriteUsageSheet(
        XLWorkbook workbook,
        string sheetName,
        string nameHeader,
        IEnumerable<UsageSummary> rows)
    {
        IXLWorksheet worksheet = workbook.Worksheets.Add(sheetName);
        WriteHeaders(worksheet, [nameHeader, "Status", "Time Spent (Minutes)", "Time Spent (Hours)"]);

        int rowNumber = 2;
        foreach (UsageSummary row in rows)
        {
            worksheet.Cell(rowNumber, 1).Value = row.Name;
            worksheet.Cell(rowNumber, 2).Value = row.Status;
            worksheet.Cell(rowNumber, 3).Value = row.Minutes;
            worksheet.Cell(rowNumber, 4).Value = Math.Round(row.Minutes / 60.0, 2);
            rowNumber++;
        }

        FinishSheet(worksheet, rowNumber - 1, 4);
    }

    private static void WriteWindowsSheet(XLWorkbook workbook, IEnumerable<DailyWindowSummary> rows)
    {
        IXLWorksheet worksheet = workbook.Worksheets.Add("Windows");
        WriteHeaders(worksheet, ["Date", "Window", "Status", "Time Spent (Minutes)", "Time Spent (Hours)"]);

        int rowNumber = 2;
        foreach (DailyWindowSummary row in rows)
        {
            worksheet.Cell(rowNumber, 1).Value = row.Date;
            worksheet.Cell(rowNumber, 1).Style.DateFormat.Format = "yyyy-MM-dd";
            worksheet.Cell(rowNumber, 2).Value = row.Window;
            worksheet.Cell(rowNumber, 3).Value = row.Status;
            worksheet.Cell(rowNumber, 4).Value = row.Minutes;
            worksheet.Cell(rowNumber, 5).Value = Math.Round(row.Minutes / 60.0, 2);
            rowNumber++;
        }

        FinishSheet(worksheet, rowNumber - 1, 5);
    }

    private static void WriteHeaders(IXLWorksheet worksheet, IReadOnlyList<string> headers)
    {
        for (int index = 0; index < headers.Count; index++)
        {
            worksheet.Cell(1, index + 1).Value = headers[index];
            worksheet.Cell(1, index + 1).Style.Font.Bold = true;
        }
    }

    private static void FinishSheet(IXLWorksheet worksheet, int lastRow, int lastColumn)
    {
        IXLRange range = worksheet.Range(1, 1, Math.Max(1, lastRow), lastColumn);
        range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        worksheet.Columns().AdjustToContents();
    }
}
