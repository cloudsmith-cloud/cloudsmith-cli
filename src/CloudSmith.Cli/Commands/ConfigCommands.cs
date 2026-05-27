using System.CommandLine;
using CloudSmith.Cli.Output;
using CloudSmith.Cli.Services;

namespace CloudSmith.Cli.Commands;

public static class ConfigCommands
{
    public static Command Build(IConfigService configService)
    {
        Command config = new("config", "Read and write CLI configuration.");

        config.AddCommand(BuildGet(configService));
        config.AddCommand(BuildSet(configService));
        config.AddCommand(BuildList(configService));

        return config;
    }

    // -------------------------------------------------------------------------
    // cs config get <key>
    // -------------------------------------------------------------------------
    private static Command BuildGet(IConfigService configService)
    {
        Command get = new("get", "Read a configuration value.");

        Argument<string> keyArg = new("key", "Configuration key to read (server|org|output).");
        get.AddArgument(keyArg);

        get.SetHandler((string key, string output) =>
        {
            try
            {
                string? value = configService.Get(key);

                if (output.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    OutputWriter.WriteJson(new { key, value });
                }
                else
                {
                    if (value is null)
                        OutputWriter.WriteMessage(output, $"{key} is not set.");
                    else
                        OutputWriter.WriteTable(output, [(key, value)], "Key", "Value");
                }
            }
            catch (ArgumentException ex)
            {
                OutputWriter.WriteError(ex.Message);
                Environment.Exit(3);
            }
        }, keyArg, CommandBase.OutputOption);

        return get;
    }

    // -------------------------------------------------------------------------
    // cs config set <key> <value>
    // -------------------------------------------------------------------------
    private static Command BuildSet(IConfigService configService)
    {
        Command set = new("set", "Write a configuration value.");

        Argument<string> keyArg   = new("key",   "Configuration key (server|org|output).");
        Argument<string> valueArg = new("value", "Value to set.");

        set.AddArgument(keyArg);
        set.AddArgument(valueArg);

        set.SetHandler((string key, string value, string output) =>
        {
            try
            {
                configService.Set(key, value);

                if (output.Equals("json", StringComparison.OrdinalIgnoreCase))
                    OutputWriter.WriteJson(new { key, value, message = "Configuration updated." });
                else
                    OutputWriter.WriteMessage(output, $"Set {key} = {value}");
            }
            catch (ArgumentException ex)
            {
                OutputWriter.WriteError(ex.Message);
                Environment.Exit(3);
            }
        }, keyArg, valueArg, CommandBase.OutputOption);

        return set;
    }

    // -------------------------------------------------------------------------
    // cs config list
    // -------------------------------------------------------------------------
    private static Command BuildList(IConfigService configService)
    {
        Command list = new("list", "List all configuration values.");

        list.SetHandler((string output) =>
        {
            CliConfig cfg = configService.Load();

            if (output.Equals("json", StringComparison.OrdinalIgnoreCase))
            {
                OutputWriter.WriteJson(new
                {
                    server = cfg.Server,
                    org    = cfg.Org,
                    output = cfg.Output
                });
            }
            else
            {
                OutputWriter.WriteTable(output, [
                    ("server", cfg.Server ?? "(not set)"),
                    ("org",    cfg.Org    ?? "(not set)"),
                    ("output", cfg.Output ?? "(not set)")
                ], "Key", "Value");
            }
        }, CommandBase.OutputOption);

        return list;
    }
}
