# Window Logger

Window Logger is a productivity tracking tool that monitors active windows and analyzes time spent across applications and categories. The system consists of a suite of .NET applications (Logger, Analyser, Tray Controller, Config GUI) that work together to provide insights into computer usage patterns.

---

## Overview

The system runs discretely in the system tray and consists of four components:

1. **WindowLoggerTray** (Controller) - A system tray app that manages the background logger and provides quick access to actions.
2. **WindowLogger** - A background process that monitors and logs active windows.
3. **WindowAnalyser** - Analyzes logs and generates detailed Excel reports.
4. **WindowLoggerConfigGui** - A visual editor for configuration rules.

---

## Quick Start (DLL Mode)

### Step 1: Build 

Build both applications using Visual Studio or the .NET CLI:

```bash
dotnet build WindowLogger.sln
```

### Step 2: Run the Logger

Navigate to the WindowLogger output directory and run:

```bash
cd WindowLogger/bin/Debug/net9.0
WindowLogger.exe
```

Or from the project directory:

```bash
cd WindowLogger
dotnet run
```

**What happens:**

- The logger starts monitoring your active windows every 100ms
- Console displays each window change
- Data is saved to `window_log.csv` in the same directory as the executable
- Press **Enter** to stop logging

### Step 3: Run the Controller

Navigate to the created folder and start the tray application using the dotnet runtime:

```bash
cd App
dotnet WindowLoggerTray.dll
```

**What happens:**
- An icon appears in your System Tray (near the clock).
- Right-click the icon to control the application.

## Using the Controller

Once WindowLoggerTray is running, right-click the tray icon to access the menu:

- **Start Logging**: Launches WindowLogger.dll in the background (hidden).
- **Stop Logging**: Safely stops the background logger process.
- **Generate Report & Open**: Runs analysis on collected data and opens the Excel report automatically.
- **Edit Configuration**:
    - **GUI**: Opens the visual editor (WindowLoggerConfigGui.dll).
    - **JSON**: Opens the raw appsettings.json file.
- **Clear Collected Data**: Deletes the current log file to start fresh.

### Step 3: Analyze the Data

Navigate to the WindowAnalyser output directory and run:

```bash
cd WindowAnalyser/bin/Debug/net9.0
WindowAnalyser.exe WindowLogger.csv output.xlsx
```

Or from the project directory:

```bash
cd WindowAnalyser
dotnet run -- <path-to-csv> <output-xlsx-path>
```

**Example:**

```bash
dotnet run -- ../../WindowLogger/bin/Debug/net9.0/WindowLogger.csv weekly_report.xlsx
```

---

## Data Storage

### Log File

- **File Name:** WindowLogger.csv
- **Location:** Created in the same directory where the application runs.
- **Format:** Timestamp,Window Title [Executable],Status

### Configuration File

- **File Name:** appsettings.json
- **Location:** Created in the same directory. Defines how windows are grouped into Applications and Categories.

---

## Manual Usage (Command Line)

If you prefer to run components manually without the Tray Controller:

**Running the Logger:**
```bash
dotnet WindowLogger.dll
```

**Running the Analyser:**
```bash
dotnet WindowAnalyser.dll WindowLogger.csv Report.xlsx
```

---

## Report Details

The generated Excel workbook contains 4 worksheets:

1. **Categories**: Aggregates time by your defined categories (e.g., "Productivity", "Social").
2. **Applications**: Aggregates time by defined application names.
3. **Undefined Applications**: Shows windows that didn't match any rule (useful for refining config).
4. **Windows**: Daily breakdown of all raw window activity.

**Time Columns:** The report includes precise time tracking:
Time Spent (Hours) | Time Spent (Minutes) | Time Spent (Seconds)

---

## Configuration (appsettings.json)

The configuration file controls how window titles are classified.

### Structure Example

```json
{
  "applications": [
    {
      "name": "Browser",
      "include": [ "Firefox", "Chrome", "Edge" ],
      "exclude": [ "taskbeat" ]
    },
    {
      "name": "Visual Studio",
      "include": [ "Visual Studio", "devenv.exe" ],
      "exclude": []
    },
    {
      "name": "TaskBeat",
      "include": [ "Firefox", "taskbeat" ],
      "exclude": []
    }
  ],
  "categories": [
    {
      "name": "Development",
      "includeApplications": [ "Visual Studio", "VS Code" ],
      "excludeApplications": []
    },
    {
      "name": "Productivity",
      "includeApplications": [ "Notepad", "TaskBeat" ],
      "excludeApplications": []
    }
  ],
  "exclusions": [
    {
      "include": [ "private", "incognito" ]
    }
  ]
}
```

### Matching Logic
1. **Applications**: Matches if ALL include keywords are present and NONE of exclude keywords are present. Order matters (first match wins).
2. **Categories**: Groups defined Applications together.
3. **Exclusions**: Windows matching these rules are completely ignored (not logged in report).

---

## Requirements

- **.NET Runtime**: .NET 9.0 / 10.0 or higher.
- **Excel Viewer**: Microsoft Excel, LibreOffice, or compatible software.

---

## Troubleshooting

### "No log file found to analyze"
**Solution:** Start the logging via the Tray icon and wait a few seconds before generating a report.

### "Report file was not created"
**Solution:** Ensure WindowLogger.csv exists and is not open in another program (like Excel) while generating a new report.

### Report columns are empty
**Solution:** Check the "Undefined Applications" tab in the Excel report to see the raw names, then update your appsettings.json to include them.

### Log file grows too large
**Solution:** Archive old logs regularly (e.g., monthly) and use the "Clear Collected Data" option in the Tray menu to start fresh.

---

## License

This project is open source.
