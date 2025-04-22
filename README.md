# Window Logger

Window logger is a simple tool that analyses the windows and programs that the current user is working with. The system consists of two applications

## Window logger

The application that runs continuously and monitors active windows in the system, producing a textual log of active windows

## Window analyser

The application that produces an excel workbook using logs created by the window logger application

### Configuration

This file defines how application window titles should be interpreted, grouped into applications and categories, and which entries should be excluded from analysis. The configuration allows better control over the output Excel report by specifying rules for classification and filtering.

#### Example `appsettings.json`

```json
{
  "applications": [
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


#### Applications

The `applications` section defines how specific window titles are grouped into logical applications.

- `name`: Logical name of the application.
- `include`: List of keywords that must appear in the window title.
- `exclude`: (Optional) Keywords that must **not** appear in the window title.

**Notes:**
- All checks are **case-insensitive**.
- Multiple entries can use the same application `name`, allowing grouping of similar tools (e.g., Chrome, Firefox, and Edge under "Browser").
- Exclusion filters allow for more precise control over overlaps.

#### Exclusions

The `exclusions` section defines window titles that should be completely ignored.

- Each exclusion rule must contain all listed `include` keywords for a match.
- If a window title matches an exclusion rule, its usage is **completely removed** from the analysis (as if it never existed).

**Example:**  
If a window contains `"Firefox"`, `"ebay"`, and `"cart"`, it will be excluded from the final report.

#### Categories

The `categories` section defines higher-level groupings of applications.

- `name`: Category name shown in the report.
- `includeApplications`: Application names (from the `applications` section) to include in this category.
- `excludeApplications`: (Optional) Applications to exclude from the category.

**Rules:**
- If an application is included in multiple categories, its time is **counted separately for each**.
- You can use multiple categories with overlapping or distinct applications.
