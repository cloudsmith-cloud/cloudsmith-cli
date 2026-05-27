using System.Text.Json;
using System.Text.Json.Serialization;

namespace CloudSmith.Cli.Services;

public sealed class CliConfig
{
    [JsonPropertyName("server")]
    public string? Server { get; set; }

    [JsonPropertyName("org")]
    public string? Org { get; set; }

    [JsonPropertyName("output")]
    public string? Output { get; set; }
}

public interface IConfigService
{
    CliConfig Load();
    void Save(CliConfig config);
    string? Get(string key);
    void Set(string key, string value);
    string ConfigFilePath { get; }
}

public sealed class ConfigService : IConfigService
{
    private static readonly HashSet<string> SupportedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "server", "org", "output"
    };

    public string ConfigFilePath { get; }

    public ConfigService() : this(GetDefaultConfigPath()) { }

    public ConfigService(string configFilePath)
    {
        ConfigFilePath = configFilePath;
    }

    public static string GetDefaultConfigPath()
    {
        string baseDir = Environment.OSVersion.Platform == PlatformID.Win32NT
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cloudsmith")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cloudsmith");

        return Path.Combine(baseDir, "config.json");
    }

    public CliConfig Load()
    {
        if (!File.Exists(ConfigFilePath))
            return new CliConfig();

        try
        {
            string json = File.ReadAllText(ConfigFilePath);
            return JsonSerializer.Deserialize<CliConfig>(json) ?? new CliConfig();
        }
        catch
        {
            return new CliConfig();
        }
    }

    public void Save(CliConfig config)
    {
        string dir = Path.GetDirectoryName(ConfigFilePath)!;
        Directory.CreateDirectory(dir);

        string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigFilePath, json);
    }

    public string? Get(string key)
    {
        ValidateKey(key);
        CliConfig config = Load();
        return key.ToLowerInvariant() switch
        {
            "server" => config.Server,
            "org"    => config.Org,
            "output" => config.Output,
            _ => null
        };
    }

    public void Set(string key, string value)
    {
        ValidateKey(key);

        if (key.Equals("output", StringComparison.OrdinalIgnoreCase)
            && !new[] { "table", "json" }.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Invalid value for 'output'. Supported values: table, json.");
        }

        CliConfig config = Load();
        switch (key.ToLowerInvariant())
        {
            case "server": config.Server = value; break;
            case "org":    config.Org    = value; break;
            case "output": config.Output = value.ToLowerInvariant(); break;
        }
        Save(config);
    }

    private static void ValidateKey(string key)
    {
        if (!SupportedKeys.Contains(key))
            throw new ArgumentException($"Unknown config key '{key}'. Supported keys: {string.Join(", ", SupportedKeys)}.");
    }
}
