# Window Logger

Window Logger helps you understand how you spend time on your PC by logging the active window and producing human-friendly Excel reports. The tooling now includes a background logger, a tray app, a small GUI for configuring rules, and an analyzer that produces richer reports (including seconds).

---

## Overview

Components:

- `WindowLogger` — background logger that records the active window and activity status to a CSV.
- `WindowAnalyser` — analyzes the CSV and generates an Excel workbook report (multiple sheets).
- `WindowLoggerTray` — lightweight tray app to start/stop the logger and access the GUI.
- `WindowLoggerConfigGui` — small WinForms GUI to edit application/category rules.

All components build from the same solution and a `CompleteApp` output folder is provided for distribution.

---

## Quick Start

1) Build everything from the solution root:

```bash
dotnet build WindowLogger.sln
```

2) Run the logger directly (debug build):

```powershell
cd WindowLogger/bin/Debug/net9.0
.\WindowLogger.exe
```

Or use the tray app / GUI from `CompleteApp`:

```powershell
cd CompleteApp
.\WindowLoggerTray.exe    # starts tray
.\WindowLoggerConfigGui.exe  # opens configuration GUI
```

3) Generate a report using the analyzer (the CSV filename used by the apps is `WindowLogger.csv`):

```powershell
dotnet run --project WindowAnalyser/WindowAnalyser.csproj -- CompleteApp/WindowLogger.csv CompleteApp/Report.xlsx
```

The analyzer writes `CompleteApp/Report.xlsx` — the default report filename used in packaging.

---

## Data & Files

- Log file: `WindowLogger.csv` (created next to the executable). Each row has:

  Timestamp,Window Title [Executable],Status

  Example:

  2026-01-18 09:15:30,Visual Studio 2022 [devenv.exe],Active

- Config: `appsettings.json` must be next to `WindowAnalyser.exe` (it is copied to output during build).

---

## What’s new / Notes

- The generated Excel report now includes a seconds column and by default orders time columns as: Hours, Minutes, Seconds — this makes totals and human-readable summaries easier to scan.
- `CompleteApp` is a convenience output that contains all built artifacts (EXEs, DLLs, `Report.xlsx`, etc.) for distribution.

---

## WindowAnalyser — Report Details

The produced workbook contains these worksheets:

- `Categories` — aggregates by category
- `Applications` — aggregates by application name (from your rules)
- `Undefined Applications` — windows not matched by any rule (useful to refine rules)
- `Windows` — daily breakdown

Time columns and ordering: Hours | Minutes | Seconds (seconds shown as an integer column).

If you prefer a MM:SS formatted column instead, that can be added as an option.

---

## Configuration (appsettings.json)

Control how window titles map to logical application names and categories. Minimal structure:

```json
{
  "applications": [ ... ],
  "exclusions": [ ... ],
  "categories": [ ... ]
}
```

Tips:

- Put the most specific rules first — matching is "first win".
- Use executable names (`devenv.exe`, `chrome.exe`) for precise matches.

---

## Commands you’ll use frequently

Generate the default packaged report:

```powershell
dotnet run --project WindowAnalyser/WindowAnalyser.csproj -- CompleteApp/WindowLogger.csv CompleteApp/Report.xlsx
```

Publish the tray and GUI into `CompleteApp` (for distribution):

```powershell
dotnet publish WindowLoggerTray/WindowLoggerTray.csproj -c Release -o CompleteApp
dotnet publish WindowLoggerConfigGui/WindowLoggerConfigGui.csproj -c Release -o CompleteApp
```

---

## Troubleshooting

- "Error reading appsettings.json": check `appsettings.json` exists and is valid JSON; the analyzer will fall back to defaults if missing.
- "Report.xlsx locked": close the file in Excel before regenerating — the analyzer overwrites the output file.
- CSV format error: ensure each row contains at least `Timestamp,Window Title,Status`.

---

## License

This project is open source and available under the repository license.
