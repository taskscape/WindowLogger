namespace WindowLogger.Shared.Configuration;

public sealed record RuleDiagnostic(string Severity, string Code, string Message);

public static class ConfigurationDiagnostics
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    public static IReadOnlyList<RuleDiagnostic> Analyze(
        AppSettings settings,
        IEnumerable<string>? observedWindowTitles = null)
    {
        settings.Normalize();
        var diagnostics = new List<RuleDiagnostic>();

        foreach (IGrouping<string, ApplicationDefinition> group in settings.Applications
                     .Where(application => !string.IsNullOrWhiteSpace(application.Name))
                     .GroupBy(application => application.Name, Comparer)
                     .Where(group => group.Count() > 1))
        {
            diagnostics.Add(new("Error", "DUPLICATE_APPLICATION",
                $"Application name '{group.Key}' is defined {group.Count()} times."));
        }

        for (int index = 0; index < settings.Applications.Count; index++)
        {
            ApplicationDefinition application = settings.Applications[index];
            string displayName = string.IsNullOrWhiteSpace(application.Name) ? $"rule #{index + 1}" : application.Name;
            string[] conflicts = application.Include.Intersect(application.Exclude, Comparer).ToArray();
            if (conflicts.Length > 0)
            {
                diagnostics.Add(new("Error", "CONFLICTING_PATTERN",
                    $"Application '{displayName}' includes and excludes the same pattern(s): {string.Join(", ", conflicts)}."));
            }

            for (int earlierIndex = 0; earlierIndex < index; earlierIndex++)
            {
                ApplicationDefinition earlier = settings.Applications[earlierIndex];
                bool shadows = earlier.Include.Count > 0 &&
                    earlier.Exclude.Count == 0 &&
                    earlier.Include.All(pattern => application.Include.Contains(pattern, Comparer));
                if (shadows)
                {
                    diagnostics.Add(new("Warning", "UNREACHABLE_RULE",
                        $"Application '{displayName}' is unreachable because earlier rule '{earlier.Name}' matches every title it could match."));
                    break;
                }
            }
        }

        foreach (IGrouping<string, ApplicationDefinition> group in settings.Applications.GroupBy(
                     application => Signature(application.Include, application.Exclude), Comparer).Where(group => group.Count() > 1))
        {
            diagnostics.Add(new("Warning", "DUPLICATE_DEFINITION",
                $"Applications {string.Join(", ", group.Select(application => $"'{application.Name}'"))} use identical match patterns."));
        }

        foreach (CategoryDefinition category in settings.Categories)
        {
            if (category.IncludeApplications.Count == 0)
            {
                diagnostics.Add(new("Warning", "EMPTY_CATEGORY", $"Category '{category.Name}' contains no applications."));
            }

            string[] conflicts = category.IncludeApplications.Intersect(category.ExcludeApplications, Comparer).ToArray();
            if (conflicts.Length > 0)
            {
                diagnostics.Add(new("Error", "CONFLICTING_CATEGORY",
                    $"Category '{category.Name}' includes and excludes: {string.Join(", ", conflicts)}."));
            }
        }

        if (observedWindowTitles is not null)
        {
            string[] titles = observedWindowTitles.Where(title => !string.IsNullOrWhiteSpace(title)).Distinct().ToArray();
            foreach (ApplicationDefinition application in settings.Applications)
            {
                bool matched = titles.Any(title =>
                    application.Include.Count > 0 &&
                    application.Include.All(pattern => title.Contains(pattern, StringComparison.OrdinalIgnoreCase)) &&
                    !application.Exclude.Any(pattern => title.Contains(pattern, StringComparison.OrdinalIgnoreCase)));
                if (!matched)
                {
                    diagnostics.Add(new("Info", "NEVER_MATCHED",
                        $"Application '{application.Name}' has never matched the available activity history."));
                }
            }
        }

        return diagnostics;
    }

    private static string Signature(IEnumerable<string> include, IEnumerable<string> exclude) =>
        $"I:{string.Join('|', include.OrderBy(value => value, Comparer))};E:{string.Join('|', exclude.OrderBy(value => value, Comparer))}";
}
