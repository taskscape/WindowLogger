using System.Collections.Generic;

namespace WindowLoggerConfigGui
{
    public class AppSettings
    {
        public List<ApplicationDefinition> Applications { get; set; } = new List<ApplicationDefinition>();
        public List<ExclusionDefinition> Exclusions { get; set; } = new List<ExclusionDefinition>();
        public List<CategoryDefinition> Categories { get; set; } = new List<CategoryDefinition>();
    }

    public class ApplicationDefinition
    {
        public string Name { get; set; } = string.Empty;
        public List<string> Include { get; set; } = new List<string>();
        public List<string> Exclude { get; set; } = new List<string>();
    }

    public class ExclusionDefinition
    {
        public List<string> Include { get; set; } = new List<string>();
    }

    public class CategoryDefinition
    {
        public string Name { get; set; } = string.Empty;
        public List<string> IncludeApplications { get; set; } = new List<string>();
        public List<string> ExcludeApplications { get; set; } = new List<string>();
    }
}