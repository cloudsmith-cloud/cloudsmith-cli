using CloudSmith.Cli.Services;
using Xunit;

namespace CloudSmith.Cli.Tests;

/// <summary>
/// AB#1525 — cs config get/set/list: read/write round-trip for all supported keys.
/// </summary>
public class ConfigTests
{
    private static ConfigService MakeService() =>
        new(Path.Combine(Path.GetTempPath(), $"cs-test-config-{Guid.NewGuid():N}.json"));

    // -------------------------------------------------------------------------
    // set / get round-trips
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("server", "https://example.com")]
    [InlineData("org",    "my-organisation")]
    [InlineData("output", "json")]
    [InlineData("output", "table")]
    public void Set_Then_Get_ReturnsExpectedValue(string key, string value)
    {
        ConfigService svc = MakeService();
        svc.Set(key, value);
        Assert.Equal(value, svc.Get(key));
    }

    [Theory]
    [InlineData("SERVER", "https://upper.example.com")]
    [InlineData("Output", "json")]
    public void Set_IsCaseInsensitive(string key, string value)
    {
        ConfigService svc = MakeService();
        svc.Set(key, value);
        // Get using lowercase
        Assert.Equal(value.ToLowerInvariant().Equals("json") || value.ToLowerInvariant().Equals("table")
            ? value.ToLowerInvariant()
            : value,
            svc.Get(key.ToLowerInvariant()));
    }

    [Fact]
    public void Get_WhenKeyNotSet_ReturnsNull()
    {
        ConfigService svc = MakeService();
        Assert.Null(svc.Get("server"));
    }

    [Fact]
    public void Set_InvalidKey_Throws()
    {
        ConfigService svc = MakeService();
        Assert.Throws<ArgumentException>(() => svc.Set("unknown-key", "value"));
    }

    [Fact]
    public void Get_InvalidKey_Throws()
    {
        ConfigService svc = MakeService();
        Assert.Throws<ArgumentException>(() => svc.Get("does-not-exist"));
    }

    [Fact]
    public void Set_InvalidOutputValue_Throws()
    {
        ConfigService svc = MakeService();
        Assert.Throws<ArgumentException>(() => svc.Set("output", "yaml"));
    }

    // -------------------------------------------------------------------------
    // list (Load)
    // -------------------------------------------------------------------------

    [Fact]
    public void Load_AfterMultipleSets_ReturnsAllValues()
    {
        ConfigService svc = MakeService();

        svc.Set("server", "https://srv.example.com");
        svc.Set("org",    "contoso");
        svc.Set("output", "table");

        CliConfig config = svc.Load();

        Assert.Equal("https://srv.example.com", config.Server);
        Assert.Equal("contoso",                  config.Org);
        Assert.Equal("table",                    config.Output);
    }

    [Fact]
    public void Load_WhenFileAbsent_ReturnsEmptyConfig()
    {
        ConfigService svc = MakeService();
        CliConfig config = svc.Load();

        Assert.Null(config.Server);
        Assert.Null(config.Org);
        Assert.Null(config.Output);
    }

    // -------------------------------------------------------------------------
    // Persistence — values survive re-instantiation
    // -------------------------------------------------------------------------

    [Fact]
    public void Values_Persist_AcrossInstances()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"cs-persist-{Guid.NewGuid():N}.json");
        try
        {
            ConfigService svc1 = new(tempFile);
            svc1.Set("server", "https://persistent.example.com");
            svc1.Set("org",    "cloudsmith-org");

            // Create a new instance pointing at the same file
            ConfigService svc2 = new(tempFile);
            Assert.Equal("https://persistent.example.com", svc2.Get("server"));
            Assert.Equal("cloudsmith-org",                  svc2.Get("org"));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    // -------------------------------------------------------------------------
    // Overwrite — last write wins
    // -------------------------------------------------------------------------

    [Fact]
    public void Set_OverwritesExistingValue()
    {
        ConfigService svc = MakeService();
        svc.Set("server", "https://first.example.com");
        svc.Set("server", "https://second.example.com");

        Assert.Equal("https://second.example.com", svc.Get("server"));
    }
}
