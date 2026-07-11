using WindowLogger.Shared.Configuration;

namespace WindowAnalyser;

public static class ConfigurationLoader
{
    public static AppSettings Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The analyser configuration file was not found.", path);
        }

        try
        {
            return AppSettingsJson.Deserialize(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            throw new InvalidDataException($"The analyser configuration is invalid: {ex.Message}", ex);
        }
    }
}
