using System.Text.Json;

namespace WindowLogger.Shared.Configuration;

public sealed class AppSettings
{
    public List<ApplicationDefinition> Applications { get; set; } = [];
    public List<ExclusionDefinition> Exclusions { get; set; } = [];
    public List<CategoryDefinition> Categories { get; set; } = [];
    public List<RuleTestCase> RuleTests { get; set; } = [];

    public void Normalize()
    {
        Applications ??= [];
        Exclusions ??= [];
        Categories ??= [];
        RuleTests ??= [];

        foreach (ApplicationDefinition application in Applications)
        {
            application.Name ??= string.Empty;
            application.Include ??= [];
            application.Exclude ??= [];
        }

        foreach (ExclusionDefinition exclusion in Exclusions)
        {
            exclusion.Include ??= [];
        }

        foreach (CategoryDefinition category in Categories)
        {
            category.Name ??= string.Empty;
            category.IncludeApplications ??= [];
            category.ExcludeApplications ??= [];
        }

        foreach (RuleTestCase test in RuleTests)
        {
            test.Name ??= string.Empty;
            test.WindowTitle ??= string.Empty;
            test.ExecutableName ??= string.Empty;
            test.ExpectedApplication ??= string.Empty;
            test.ExpectedCategories ??= string.Empty;
        }
    }
}

public sealed class ApplicationDefinition
{
    public string Name { get; set; } = string.Empty;
    public List<string> Include { get; set; } = [];
    public List<string> Exclude { get; set; } = [];
}

public sealed class ExclusionDefinition
{
    public List<string> Include { get; set; } = [];
}

public sealed class CategoryDefinition
{
    public string Name { get; set; } = string.Empty;
    public List<string> IncludeApplications { get; set; } = [];
    public List<string> ExcludeApplications { get; set; } = [];
}

public sealed class RuleTestCase
{
    public string Name { get; set; } = string.Empty;
    public string WindowTitle { get; set; } = string.Empty;
    public string ExecutableName { get; set; } = string.Empty;
    public string ExpectedApplication { get; set; } = string.Empty;
    public string ExpectedCategories { get; set; } = string.Empty;
    public bool ExpectedExcluded { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string LastResult { get; set; } = "Not run";
}

public static class AppSettingsJson
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true
    };

    public static AppSettings Deserialize(string json)
    {
        AppSettings settings = JsonSerializer.Deserialize<AppSettings>(json, ReadOptions)
            ?? throw new JsonException("Configuration is empty.");
        settings.Normalize();
        return settings;
    }

    public static string Serialize(AppSettings settings)
    {
        settings.Normalize();
        return JsonSerializer.Serialize(settings, WriteOptions);
    }
}
