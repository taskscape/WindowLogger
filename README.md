# WindowLogger

WindowLogger is a local Windows activity-tracking tool that records which application window is in the foreground and turns that history into an Excel usage report. It is intended for people who want an evidence-based view of how they spend time across projects, tools, communication, administration, and personal activities.

All collection and analysis happens on the local computer. WindowLogger does not upload activity data to a server.

## Purpose

WindowLogger answers practical questions such as:

- Which applications consumed most of the day?
- How much time was active work versus idle time?
- How was time divided between development, communication, document work, or custom business categories?
- Which window titles are not yet covered by the current classification rules?
- How did application usage differ by day?

It records changes to the foreground window, the executable name, active or inactive status, and explicit logger-session boundaries. The analyser then applies configurable matching rules and produces an `.xlsx` workbook.

> Window titles can contain document names, website titles, customer names, or other sensitive text. Review and protect `WindowLogger.db` and exported CSV files as you would any other personal activity record.

## Benefits

- **Local and private:** no cloud account or external telemetry is required.
- **Low-friction collection:** the tray controller starts, pauses, resumes, and stops logging in the background.
- **Privacy on demand:** pause tracking during sensitive or personal activity without terminating the logger.
- **Accurate session accounting:** shutdown and restart boundaries prevent offline time from being counted as usage.
- **Reliable long-term storage:** SQLite provides indexed date queries, atomic writes, and consistent reports while collection continues.
- **Portable history:** activity can be exported to or restored from the original CSV format.
- **Flexible classification:** applications can be matched by one or more title or executable keywords and assigned to multiple categories.
- **Actionable reports:** Excel output includes category, application, undefined-window, and daily-window views.
- **Easy refinement:** unmatched windows reveal which configuration rules should be added next.
- **Portable deployment:** the installer includes the required .NET runtime.

## Components

| Component | Purpose |
| --- | --- |
| `WindowLoggerTray.exe` | WPF tray controller and recommended entry point. |
| `WindowLogger.exe` | Background foreground-window and inactivity collector. |
| `WindowLoggerConfigGui.exe` | WPF editor for application, exclusion, and category rules. |
| `WindowAnalyser.exe` | SQLite/CSV analyser, portability utility, and Excel report generator. |
| `appsettings.json` | Classification and exclusion rules used by the analyser. |

## Install and use

### 1. Install WindowLogger

Download `WindowLogger-Setup-<version>.exe` from [GitHub Releases](https://github.com/taskscape/WindowLogger/releases) and run it.

The installer:

- installs the self-contained application under `%LOCALAPPDATA%\Programs\WindowLogger`;
- optionally starts the tray controller whenever the current user signs in;
- offers to launch the tray controller when setup finishes.

### 2. Start collecting activity

Find the WindowLogger icon in the Windows notification area, right-click it, and choose **Start Logging**. The logger runs without a visible console and writes activity atomically to `WindowLogger.db`.

Choose **Pause Logging** before sensitive or personal activity. While paused, the logger process stays available but does not inspect or record foreground windows. Choose **Resume Logging** to continue collection. Pause and resume create separate session boundaries, so the paused interval is excluded from reports.

Choose **Stop Logging** before moving or backing up the database itself. Normal shutdown records a session boundary so time between logger runs is not included in reports.

### 3. Configure application and category rules

From the tray menu, choose **Edit Configuration (GUI)**.

- **Applications** map window-title or executable keywords to a friendly application name.
- **Exclusions** remove matching windows from all calculations.
- **Categories** group named applications into broader areas such as Development, Communication, or Finance.
- Application rules are evaluated in order, so place specific rules before broad fallback rules.

Use **Undefined Applications** in a generated report to discover titles that need new rules. Advanced users can choose **Edit Configuration (JSON)** instead.

### 4. Generate a report

Choose **Generate Report & Open** from the tray menu. WindowLogger reads a consistent SQLite snapshot—even while collection continues—creates `Report.xlsx`, and opens it with the system's associated spreadsheet application.

The workbook contains:

1. **Categories** — total active and inactive time per configured category.
2. **Applications** — total time per matched application.
3. **Undefined Applications** — unmatched titles for configuration refinement.
4. **Windows** — daily window-level totals.

### 5. Manage collected data

Choose **Export Activity to CSV** for a portable backup or **Import Activity from CSV** to replace the current history. Import temporarily stops collection and restores its previous running or paused state afterward.

Choose **Clear Collected Data** to stop the logger, remove the current SQLite database, and restart collection if it was previously active. If tracking was paused, it restarts in the paused state. This action cannot be undone.

## Data locations

For an installer-based deployment, the application files and writable data are stored together by default:

```text
%LOCALAPPDATA%\Programs\WindowLogger\
├── WindowLogger.db
├── appsettings.json
├── Report.xlsx
└── application executables and runtime files
```

| File | Contents |
| --- | --- |
| `WindowLogger.db` | Indexed SQLite history containing foreground-window, status, and session events. |
| `appsettings.json` | Application matching, exclusions, and categories. |
| `Report.xlsx` | Most recently generated analysis workbook. |

## Build from source

Requirements: Windows and the .NET 10 SDK.

```powershell
dotnet build WindowLogger.sln --configuration Release
dotnet test WindowLogger.sln --configuration Release
dotnet run --project WindowLoggerTray\WindowLoggerTray.csproj
```

To build the installer locally, install Inno Setup 6 and run:

```powershell
.\Installer\Build-Installer.ps1 -Version 1.2.3
```

See [Installer/README.md](Installer/README.md) for release-tag and packaging details.

---

## Manual Usage (Command Line)

If you prefer to run components manually without the Tray Controller:

**Running the Logger:**
```bash
dotnet WindowLogger.dll
```

**Running the Analyser:**
```bash
dotnet WindowAnalyser.dll WindowLogger.db Report.xlsx
```

The logger automatically imports a legacy `WindowLogger.csv` when it creates an empty database, then renames the source to `WindowLogger.migrated-<timestamp>.csv` so it cannot be imported twice.

### Portable CSV format

CSV imports and exports contain three columns:

```text
Timestamp,Window Title [Executable],Status
```

**Example Content:**

```csv
2026-01-18 09:15:00,,SessionStart
2026-01-18 09:15:30,Visual Studio 2022 [devenv.exe],Active
2026-01-18 09:20:45,Google Chrome - GitHub [chrome.exe],Active
2026-01-18 09:25:10,Slack - Development Team [slack.exe],Active
2026-01-18 09:30:00,Slack - Development Team [slack.exe],Inactive
2026-01-18 09:31:15,Visual Studio 2022 [devenv.exe],Active
2026-01-18 10:00:00,,SessionEnd
```

**Status Values:**

- `Active` - User is actively using the computer
- `Inactive` - No user input detected for 1+ minute (configurable)
- `SessionStart` - A new logger process began; no time is attributed across this boundary
- `SessionEnd` - The logger stopped normally and closes the final usage interval

### Configuration File Location

**File Name:** `appsettings.json`  
**Location:** Same directory as `WindowAnalyser.exe`

The configuration file must be in:

```text
WindowAnalyser/bin/Debug/net10.0/appsettings.json
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

- **Real-time Window Tracking**: Monitors the foreground window every 500ms
- **Executable Detection**: Records both window title and process name
- **Inactivity Detection**: Automatically detects when user is idle (default: 1 minute)
- **Durable Logging**: Writes atomic SQLite transactions in WAL mode, allowing reports and exports during collection

### How It Works

1. Polls the foreground window every 500 milliseconds
2. Detects window changes and logs them with timestamp
3. Monitors user input (keyboard/mouse) to detect inactivity
4. When inactive for 1 minute, logs "Inactive" status
5. When activity resumes, logs "Active" status with new window

### Customization

Edit constants in `WindowLogger/Program.cs`:

```csharp
private static readonly TimeSpan InactivityThreshold = TimeSpan.FromMinutes(1);
```

Change the polling interval (default: 500ms):

```csharp
private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
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
WindowAnalyser.exe <input-db-or-csv-file> <output-xlsx-file>
WindowAnalyser.exe export <input-db-file> <output-csv-file> [--from <date>] [--to <exclusive-date>]
WindowAnalyser.exe import <input-csv-file> <output-db-file> [--replace]
```

**Parameters:**

- `input-db-or-csv-file` - Path to the SQLite database or a portable legacy CSV export
- `output-xlsx-file` - Desired path/name for the Excel report
- `--from` / `--to` - Optional indexed export range; the lower bound is inclusive and upper bound exclusive
- `--replace` - Atomically replaces the destination database contents during import

**Examples:**

```bash
# Absolute paths
WindowAnalyser.exe C:\Logs\WindowLogger.db C:\Reports\weekly_report.xlsx

# Relative paths
WindowAnalyser.exe WindowLogger.db report.xlsx

# Portable backup and restore
WindowAnalyser.exe export WindowLogger.db backup.csv
WindowAnalyser.exe import backup.csv restored.db --replace

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
- **.NET Runtime**: .NET 10.0 or higher
- **Desktop UI**: Windows Presentation Foundation (WPF)
- **Excel Viewer**: Microsoft Excel or compatible spreadsheet application

### Dependencies

**WindowLogger:**

- Microsoft.Data.Sqlite and SQLitePCLRaw for durable embedded storage

**WindowAnalyser:**

- ClosedXML (0.105.0-rc) - for Excel generation
- Microsoft.Data.Sqlite - for concurrent SQLite snapshot reads and CSV portability operations
- System.Text.Json (included with .NET) - for configuration parsing

---

## Tips & Best Practices

### Data Collection

1. **Run Continuously**: Leave WindowLogger running all day for accurate tracking
2. **Regular Analysis**: Analyze logs daily or weekly to identify patterns
3. **Backup Activity**: Use **Export Activity to CSV** for portable backups while collection continues
4. **Retain History**: Keep the SQLite database for efficient long histories and use date-bounded CLI exports when needed

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

### CSV import warnings

**Cause:** Log file is corrupted or has an unexpected format  
**Solution:**

1. Check that the CSV file was exported by WindowLogger or uses its three-column legacy format
2. Ensure the file hasn't been manually edited with incorrect formatting
3. Verify the file isn't empty

### No data in Categories or Applications tabs

**Cause:** No windows matched your application rules  
**Solution:**

1. Check the "Undefined Applications" tab to see what windows were logged
2. Add application rules to `appsettings.json` matching those window titles
3. Re-run the analysis

### Activity database grows too large

**Cause:** Running continuously for extended periods  
**Solution:**

1. Export the desired date ranges to CSV for archival
2. Keep a backup before clearing collected data
3. Use **Clear Collected Data** to start a new database while preserving the logger's running or paused state

---

## License

This project is open source and available under the repository license.
