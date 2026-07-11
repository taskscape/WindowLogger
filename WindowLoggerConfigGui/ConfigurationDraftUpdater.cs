using System.Collections.Generic;
using System.Linq;
using WindowLogger.Shared.Configuration;

namespace WindowLoggerConfigGui;

public static class ConfigurationDraftUpdater
{
    public static bool ApplyApplication(
        AppSettings settings,
        int index,
        string name,
        IReadOnlyList<string> include,
        IReadOnlyList<string> exclude)
    {
        if (index < 0 || index >= settings.Applications.Count)
        {
            return false;
        }

        ApplicationDefinition application = settings.Applications[index];
        bool changed = application.Name != name ||
            !application.Include.SequenceEqual(include) ||
            !application.Exclude.SequenceEqual(exclude);
        if (!changed)
        {
            return false;
        }

        application.Name = name;
        application.Include = [.. include];
        application.Exclude = [.. exclude];
        return true;
    }

    public static bool ApplyExclusion(AppSettings settings, int index, IReadOnlyList<string> include)
    {
        if (index < 0 || index >= settings.Exclusions.Count)
        {
            return false;
        }

        ExclusionDefinition exclusion = settings.Exclusions[index];
        if (exclusion.Include.SequenceEqual(include))
        {
            return false;
        }

        exclusion.Include = [.. include];
        return true;
    }

    public static bool ApplyCategory(
        AppSettings settings,
        int index,
        string name,
        IReadOnlyList<string> include,
        IReadOnlyList<string> exclude)
    {
        if (index < 0 || index >= settings.Categories.Count)
        {
            return false;
        }

        CategoryDefinition category = settings.Categories[index];
        bool changed = category.Name != name ||
            !category.IncludeApplications.SequenceEqual(include) ||
            !category.ExcludeApplications.SequenceEqual(exclude);
        if (!changed)
        {
            return false;
        }

        category.Name = name;
        category.IncludeApplications = [.. include];
        category.ExcludeApplications = [.. exclude];
        return true;
    }
}
