namespace WindowLogger.Shared.Configuration;

public sealed record RulePatternResult(string Pattern, bool Matched, string Kind);

public sealed record RuleEvaluationTrace(
    string RuleType,
    int RuleIndex,
    string RuleName,
    IReadOnlyList<RulePatternResult> Patterns,
    string Outcome);

public sealed record RuleEvaluationResult(
    string CapturedValue,
    bool IsExcluded,
    int? MatchedExclusionIndex,
    string? MatchedApplication,
    int? MatchedApplicationIndex,
    IReadOnlyList<string> Categories,
    IReadOnlyList<int> MatchedCategoryIndices,
    IReadOnlyList<RuleEvaluationTrace> Trace,
    IReadOnlyList<string> Warnings);

public static class ActivityCapturedValue
{
    public static string Format(string windowTitle, string? executableName)
    {
        string title = windowTitle.Trim();
        string executable = executableName?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(executable))
        {
            return title;
        }
        return string.IsNullOrEmpty(title) ? $"[{executable}]" : $"{title} [{executable}]";
    }
}

public sealed class RuleEvaluationEngine
{
    public RuleEvaluationResult Evaluate(
        string windowTitle,
        string? executableName,
        AppSettings settings) => EvaluateCapturedValue(
            ActivityCapturedValue.Format(windowTitle, executableName), settings);

    public RuleEvaluationResult EvaluateCapturedValue(string capturedValue, AppSettings settings)
    {
        settings.Normalize();
        var trace = new List<RuleEvaluationTrace>();
        var warnings = new List<string>();
        int? matchedExclusion = null;

        for (int index = 0; index < settings.Exclusions.Count; index++)
        {
            ExclusionDefinition exclusion = settings.Exclusions[index];
            RulePatternResult[] patterns = exclusion.Include
                .Select(pattern => new RulePatternResult(
                    pattern,
                    capturedValue.Contains(pattern, StringComparison.OrdinalIgnoreCase),
                    "Include"))
                .ToArray();
            bool matched = patterns.Length > 0 && patterns.All(pattern => pattern.Matched);
            trace.Add(new("Exclusion", index, $"Exclusion #{index + 1}", patterns,
                matched ? "Matched; activity is excluded." : "Did not match."));
            if (matchedExclusion is null && matched)
            {
                matchedExclusion = index;
            }
        }

        if (matchedExclusion is not null)
        {
            warnings.Add("A global exclusion matched, so application and category rules are not applied.");
            return new RuleEvaluationResult(
                capturedValue, true, matchedExclusion, null, null, [], [], trace, warnings);
        }

        int? matchedApplicationIndex = null;
        string? matchedApplication = null;
        for (int index = 0; index < settings.Applications.Count; index++)
        {
            ApplicationDefinition application = settings.Applications[index];
            string[] conflicts = application.Include.Intersect(
                application.Exclude, StringComparer.OrdinalIgnoreCase).ToArray();
            if (conflicts.Length > 0)
            {
                warnings.Add($"Application '{application.Name}' includes and excludes: {string.Join(", ", conflicts)}.");
            }
            RulePatternResult[] includes = application.Include
                .Select(pattern => new RulePatternResult(
                    pattern,
                    capturedValue.Contains(pattern, StringComparison.OrdinalIgnoreCase),
                    "Include"))
                .ToArray();
            RulePatternResult[] excludes = application.Exclude
                .Select(pattern => new RulePatternResult(
                    pattern,
                    capturedValue.Contains(pattern, StringComparison.OrdinalIgnoreCase),
                    "Exclude"))
                .ToArray();
            bool included = includes.Length > 0 && includes.All(pattern => pattern.Matched);
            bool rejected = excludes.Any(pattern => pattern.Matched);
            bool matches = included && !rejected;
            string outcome;
            if (matches && matchedApplicationIndex is null)
            {
                matchedApplicationIndex = index;
                matchedApplication = application.Name;
                outcome = "Matched; selected as the first matching application.";
            }
            else if (matches)
            {
                outcome = $"Would match, but earlier rule '{matchedApplication}' has precedence.";
                warnings.Add($"Application '{application.Name}' also matches but is shadowed by '{matchedApplication}'.");
            }
            else if (rejected)
            {
                outcome = "Rejected by an exclude pattern.";
            }
            else
            {
                outcome = "One or more include patterns did not match.";
            }
            trace.Add(new("Application", index, application.Name, [.. includes, .. excludes], outcome));
        }

        var categories = new List<string>();
        var categoryIndices = new List<int>();
        if (matchedApplication is not null)
        {
            for (int index = 0; index < settings.Categories.Count; index++)
            {
                CategoryDefinition category = settings.Categories[index];
                string[] conflicts = category.IncludeApplications.Intersect(
                    category.ExcludeApplications, StringComparer.OrdinalIgnoreCase).ToArray();
                if (conflicts.Length > 0)
                {
                    warnings.Add($"Category '{category.Name}' includes and excludes: {string.Join(", ", conflicts)}.");
                }
                RulePatternResult[] includes = category.IncludeApplications
                    .Select(name => new RulePatternResult(
                        name,
                        name.Equals(matchedApplication, StringComparison.OrdinalIgnoreCase),
                        "Include application"))
                    .ToArray();
                RulePatternResult[] excludes = category.ExcludeApplications
                    .Select(name => new RulePatternResult(
                        name,
                        name.Equals(matchedApplication, StringComparison.OrdinalIgnoreCase),
                        "Exclude application"))
                    .ToArray();
                bool included = includes.Any(pattern => pattern.Matched);
                bool rejected = excludes.Any(pattern => pattern.Matched);
                bool matches = included && !rejected;
                if (matches)
                {
                    categories.Add(category.Name);
                    categoryIndices.Add(index);
                }
                trace.Add(new("Category", index, category.Name, [.. includes, .. excludes],
                    matches ? "Application assigned to this category."
                    : rejected ? "Application explicitly excluded from this category."
                    : "Application not included in this category."));
            }
            if (categories.Count == 0)
            {
                warnings.Add($"Application '{matchedApplication}' is not assigned to any category.");
            }
        }
        else
        {
            warnings.Add("No application rule matched this captured value.");
        }

        return new RuleEvaluationResult(
            capturedValue,
            false,
            null,
            matchedApplication,
            matchedApplicationIndex,
            categories,
            categoryIndices,
            trace,
            warnings);
    }
}
