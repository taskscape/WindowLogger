# Window Logger

Window Logger is a productivity tracking tool that monitors active windows and analyzes time spent across applications and categories. The system consists of a suite of .NET applications (Logger, Analyser, Tray Controller, Config GUI) that work together to provide insights into computer usage patterns.

---

## Overview

The system runs discretely in the system tray and consists of four components:

2. **WindowLogger** - A background process that monitors and logs active windows.
3. **WindowLoggerTray** (Controller) - A system tray app that manages the background logger and provides quick access to actions.
4. **WindowLoggerConfigGui** - A visual editor for configuration rules.
5. **WindowAnalyser** - Analyzes logs and generates detailed Excel reports.


---

## Quick Start

### Step 1: Build 

Build both applications using Visual Studio or the .NET CLI:

```bash
dotnet build WindowLogger.sln
```

### Step 2: Run the Logger

Navigate to the WindowLogger output directory and run:

```bash
cd WindowLogger/bin/Debug/net10.0
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
- Data is saved to `WindowLogger.csv` in the same directory as the executable
- Press **Enter** to stop logging

### Alternative Step 2: Run the Controller and run the Logger

Navigate to the Tray application's output directory and launch the executable.

```powershell
cd WindowLoggerTray/bin/Debug/net10.0-windows
.\WindowLoggerTray.exe
```
*(Note: The path might vary slightly depending on your configuration, e.g., Release mode)*

Or from the project directory:

```bash
cd WindowLoggerTray
dotnet run
```

**What happens:**
- An icon appears in your System Tray (near the clock).
- Right-click the icon to control the application.

---

## Using the Controller

Once **WindowLoggerTray** is running, right-click the tray icon to access the menu:

- **Start Logging**: Launches `WindowLogger.exe` in the background (hidden).
- **Stop Logging**: Safely stops the background logger process.
- **Generate Report & Open**: Runs analysis and opens the Excel report automatically.
- **Edit Configuration**:
    - **GUI**: Opens the visual editor (`WindowLoggerConfigGui.exe`).
    - **JSON**: Opens the raw `appsettings.json` file.
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

### Alternatively - Use the Controller Application

You can generate the `Report.xslx` directly from the **WindowLoggerTray** by clicking "Generate Report & Open".

---

## Data Storage

### Log File

- **File Name:** WindowLogger.csv
- **Location:** Created in the same directory where the application runs.
- **Format:** Timestamp,Window Title [Executable],Status

### Configuration File

- **File Name:** appsettings.json
- **Location:** Created in the same directory. Defines how windows are grouped into Applications and Categories.

### Report.xslx

WindowLoggerTray generates **Report.xslx** file when you use the `Generate Report Open` option. It uses data from the **WindowLogger.csv** file.



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
### Log File Format

The CSV file contains three columns:

```text
Timestamp,Window Title [Executable],Status
```

**Example Content:**

```csv
2026-01-18 09:15:30,Visual Studio 2022 [devenv.exe],Active
2026-01-18 09:20:45,Google Chrome - GitHub [chrome.exe],Active
2026-01-18 09:25:10,Slack - Development Team [slack.exe],Active
2026-01-18 09:30:00,Slack - Development Team [slack.exe],Inactive
2026-01-18 09:31:15,Visual Studio 2022 [devenv.exe],Active
```

**Status Values:**

- `Active` - User is actively using the computer
- `Inactive` - No user input detected for 1+ minute (configurable)

### Configuration File Location

**File Name:** `appsettings.json`  
**Location:** Same directory as `WindowAnalyser.exe`

The configuration file must be in:

```test
WindowAnalyser/bin/Debug/net9.0/appsettings.json
```

The project is already configured to copy this file to the output directory automatically.

---

## Report Details

The generated Excel workbook contains 4 worksheets:

1. **Categories**: Aggregates time by your defined categories (e.g., "Productivity", "Social").
2. **Applications**: Aggregates time by defined application names.
3. **Undefined Applications**: Shows windows that didn't match any rule (useful for refining config).
4. **Windows**: Daily breakdown of all raw window activity.

**Time Columns:** The report includes precise time tracking:
Time Spent (Hours) | Time Spent (Minutes)

---

## WindowLogger - Data Collection

### Features

- **Real-time Window Tracking**: Monitors the foreground window every 100ms
- **Executable Detection**: Records both window title and process name
- **Inactivity Detection**: Automatically detects when user is idle (default: 1 minute)
- **Continuous Logging**: Appends to CSV file - safe to stop/restart

### How It Works

1. Polls the foreground window every 100 milliseconds
2. Detects window changes and logs them with timestamp
3. Monitors user input (keyboard/mouse) to detect inactivity
4. When inactive for 1 minute, logs "Inactive" status
5. When activity resumes, logs "Active" status with new window

### Customization

Edit constants in `WindowLogger/Program.cs`:

```csharp
private const double MinutesToInactive = 1;  // Inactivity threshold in minutes
```

Change timer interval (default: 100ms):

```csharp
_timer = new Timer(100);  // Polling interval in milliseconds
```

### Running as a Background Service

**Option 1: Start Minimized**

- Create a shortcut to `WindowLogger.exe`
- Right-click → Properties → Run: Minimized

**Option 2: Windows Task Scheduler**

- Schedule to run at login
- Run whether user is logged on or not
- Configure to restart on failure

---

## WindowAnalyser - Data Analysis

### Command Line Usage

```bash
WindowAnalyser.exe <input-csv-file> <output-xlsx-file>
```

**Parameters:**

- `input-csv-file` - Path to the CSV log file created by WindowLogger
- `output-xlsx-file` - Desired path/name for the Excel report

**Examples:**

```bash
# Absolute paths
WindowAnalyser.exe C:\Logs\WindowLogger.csv C:\Reports\weekly_report.xlsx

# Relative paths
WindowAnalyser.exe WindowLogger.csv report.xlsx

# Analyze multiple periods
WindowAnalyser.exe logs\january.csv reports\january_analysis.xlsx
WindowAnalyser.exe logs\february.csv reports\february_analysis.xlsx
```

### Generated Excel Report

The output workbook contains **4 worksheets**:

#### 1. Categories Tab
Groups time by configured categories (e.g., "Productivity", "Development")

| Category | Status | Time Spent (Minutes) | Time Spent (Hours) |
|----------|--------|---------------------|-------------------|
| Development | Active | 240.5 | 4.01 |
| Communication | Active | 95.2 | 1.59 |
| Productivity | Inactive | 15.0 | 0.25 |

#### 2. Applications Tab
Groups time by configured application names

| Application | Status | Time Spent (Minutes) | Time Spent (Hours) |
|-------------|--------|---------------------|-------------------|
| Visual Studio | Active | 180.3 | 3.01 |
| Browser | Active | 120.5 | 2.01 |
| Slack | Active | 45.2 | 0.75 |

#### 3. Undefined Applications Tab
Shows windows that **didn't match** any application rule - use this to identify applications you should add to your configuration

| Window | Status | Time Spent (Minutes) | Time Spent (Hours) |
|--------|--------|---------------------|-------------------|
| Calculator [calc.exe] | Active | 25.5 | 0.43 |
| Notepad++ [notepad++.exe] | Active | 18.2 | 0.30 |

#### 4. Windows Tab
Daily breakdown of all window activity

| Date | Window | Status | Time Spent (Minutes) | Time Spent (Hours) |
|------|--------|--------|---------------------|-------------------|
| 2026-01-18 | Visual Studio | Active | 120.5 | 2.01 |
| 2026-01-18 | Chrome | Active | 65.3 | 1.09 |
| 2026-01-19 | Visual Studio | Active | 180.0 | 3.00 |

### Analysis Workflow

1. **First Run** - Without `appsettings.json`
   ```bash
   WindowAnalyser.exe WindowLogger.csv initial_report.xlsx
   ```
   - All windows appear in "Undefined Applications" tab
   - No categories are generated

2. **Review Undefined Applications**
   - Open the Excel report
   - Check the "Undefined Applications" tab
   - Identify which windows you want to track

3. **Configure Application Rules**
   - Edit `appsettings.json` in the WindowAnalyser directory
   - Add application definitions based on window titles
   - Define categories for grouping

4. **Re-run Analysis**
   ```bash
   WindowAnalyser.exe WindowLogger.csv categorized_report.xlsx
   ```
   - Applications now appear in "Applications" tab
   - Categories are populated
   - "Undefined Applications" shows only unmatched windows

5. **Iterate**
   - Continue refining rules until most windows are categorized
   - Run analysis periodically (daily/weekly) to track trends

---

## Configuration

The `appsettings.json` file controls how window titles are classified.

### Configuration File Structure

```json
{
  "applications": [ ... ],
  "exclusions": [ ... ],
  "categories": [ ... ]
}
```

### Applications Section

Define how window titles map to logical application names.

**Properties:**

- `name` - Logical application name (appears in report)
- `include` - Keywords that **must all** appear in the window title
- `exclude` - Keywords that **must not** appear (optional)

**Example:**

```json
{
  "name": "Browser",
  "include": [ "Firefox" ],
  "exclude": [ "taskbeat" ]
}
```

**Matching Rules:**

- All `include` keywords must be present (AND logic)
- Any `exclude` keyword disqualifies the match
- Matching is **case-insensitive**
- **First match wins** - order matters!

**Best Practices:**

1. **Specific rules first, generic rules last:**
```json
{
  "applications": [
    {
      "name": "Notepad - MyTask",
      "include": [ "Notepad", "MyTask.txt" ],
      "exclude": []
    },
    {
      "name": "Notepad",
      "include": [ "Notepad" ],
      "exclude": [ "MyTask.txt" ]
    }
  ]
}
```

2. **Use executable names for precision:**
```json
{
  "name": "Visual Studio",
  "include": [ "Visual Studio", "devenv.exe" ],
  "exclude": []
}
```

3. **Group similar browsers:**
```json
[
  {
    "name": "Browser",
    "include": [ "Firefox" ],
    "exclude": []
  },
  {
    "name": "Browser",
    "include": [ "Chrome" ],
    "exclude": []
  },
  {
    "name": "Browser",
    "include": [ "Edge" ],
    "exclude": []
  }
]
```

### Exclusions Section

Define window titles to **completely remove** from analysis.

**Example:**
```json
{
  "exclusions": [
    {
      "include": [ "Firefox", "ebay", "cart" ]
    },
    {
      "include": [ "private", "browsing" ]
    }
  ]
}
```

When a window matches an exclusion rule:

- It's removed from **all** reports
- Time is not counted anywhere
- It's as if the window never existed

**Use Cases:**

- Exclude personal browsing during work hours
- Filter out sensitive/private windows
- Remove testing/debugging windows

### Categories Section

Group applications into higher-level categories for aggregate reporting.

**Properties:**

- `name` - Category name (appears in report)
- `includeApplications` - Application names to include
- `excludeApplications` - Applications to exclude (optional)

**Example:**
```json
{
  "categories": [
    {
      "name": "Development",
      "includeApplications": [ "Visual Studio", "VS Code", "Terminal" ],
      "excludeApplications": []
    },
    {
      "name": "Productivity",
      "includeApplications": [ "Notepad", "TaskBeat" ],
      "excludeApplications": [ "Notepad - MyTask" ]
    },
    {
      "name": "Communication",
      "includeApplications": [ "Slack", "Teams", "Outlook" ],
      "excludeApplications": []
    }
  ]
}
```

**Important:**

- Applications can belong to **multiple categories**
- Time is counted separately for each category (can exceed 100%)
- Use `excludeApplications` to create exceptions within broader groups

### Complete Example Configuration

```json
{
  "applications": [
    {
      "name": "Visual Studio",
      "include": [ "Visual Studio" ],
      "exclude": []
    },
    {
      "name": "VS Code",
      "include": [ "Visual Studio Code" ],
      "exclude": []
    },
    {
      "name": "Browser",
      "include": [ "Firefox" ],
      "exclude": [ "taskbeat" ]
    },
    {
      "name": "TaskBeat",
      "include": [ "Firefox", "taskbeat" ],
      "exclude": []
    },
    {
      "name": "Slack",
      "include": [ "Slack" ],
      "exclude": []
    },
    {
      "name": "Notepad - MyTask",
      "include": [ "Notepad", "MyTask.txt" ],
      "exclude": []
    },
    {
      "name": "Notepad",
      "include": [ "Notepad" ],
      "exclude": [ "MyTask.txt" ]
    }
  ],
  "exclusions": [
    {
      "include": [ "Firefox", "ebay", "cart" ]
    }
  ],
  "categories": [
    {
      "name": "Development",
      "includeApplications": [ "Visual Studio", "VS Code" ],
      "excludeApplications": []
    },
    {
      "name": "Communication",
      "includeApplications": [ "Slack" ],
      "excludeApplications": []
    },
    {
      "name": "Productivity",
      "includeApplications": [ "Notepad", "TaskBeat" ],
      "excludeApplications": [ "Notepad - MyTask" ]
    },
    {
      "name": "MyTask",
      "includeApplications": [ "Notepad - MyTask" ],
      "excludeApplications": []
    }
  ]
}
```

---

## Requirements

- **Operating System**: Windows (uses Win32 APIs for window tracking)
- **.NET Runtime**: .NET 9.0 or higher
- **Excel Viewer**: Microsoft Excel or compatible spreadsheet application

### Dependencies

**WindowLogger:**

- No external dependencies (uses built-in Windows APIs)

**WindowAnalyser:**

- ClosedXML (0.105.0-rc) - for Excel generation
- Newtonsoft.Json (13.0.3) - for configuration parsing

---

## Tips & Best Practices

### Data Collection

1. **Run Continuously**: Leave WindowLogger running all day for accurate tracking
2. **Regular Analysis**: Analyze logs daily or weekly to identify patterns
3. **Backup Logs**: Copy CSV files regularly - they contain your complete history
4. **Multiple Logs**: Create separate logs for different projects or time periods by moving/renaming the CSV file

### Configuration

1. **Start Simple**: Begin with broad application categories
2. **Refine Gradually**: Add more specific rules as you identify patterns
3. **Use Undefined Tab**: Regularly check "Undefined Applications" to find unmatched windows
4. **Test Rules**: Re-run analysis after configuration changes to verify rules work as expected
5. **Document Rules**: Add comments (not supported in JSON, but keep notes separately) about why specific rules exist

### Analysis

1. **Compare Periods**: Generate reports for different time periods to track changes
2. **Focus on Active Time**: Filter by "Active" status to see productive time
3. **Review Inactive Patterns**: High inactive time may indicate distractions or away-from-desk time
4. **Category Overlaps**: Remember that categories can overlap - total category time may exceed 100%

---

## Troubleshooting

### "Error reading appsettings.json"

**Cause:** Configuration file is missing or has invalid JSON  
**Solution:**

1. Ensure `appsettings.json` exists in the same directory as `WindowAnalyser.exe`
2. Validate JSON syntax using a JSON validator
3. The analyzer will use default settings (no grouping) if the file is invalid

### "CSV format error: each row must have at least 3 columns"

**Cause:** Log file is corrupted or has an unexpected format  
**Solution:**

1. Check that the CSV file was created by WindowLogger
2. Ensure the file hasn't been manually edited with incorrect formatting
3. Verify the file isn't empty

### No data in Categories or Applications tabs

**Cause:** No windows matched your application rules  
**Solution:**

1. Check the "Undefined Applications" tab to see what windows were logged
2. Add application rules to `appsettings.json` matching those window titles
3. Re-run the analysis

### Log file grows too large

**Cause:** Running continuously for extended periods  
**Solution:**

1. Archive old logs regularly (e.g., monthly)
2. Analyze and move the CSV file to a different location
3. WindowLogger will create a new `WindowLogger.csv` automatically

---

## License

This project is open source and available under the repository license.
