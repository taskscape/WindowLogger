using WindowAnalyser;

namespace WindowLogger.Tests;

public sealed class ConfigurationLoaderTests
{
    [Test]
    public void Malformed_configuration_fails_instead_of_using_empty_settings()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "{not-json");
            Assert.That(() => ConfigurationLoader.Load(path), Throws.TypeOf<InvalidDataException>());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
