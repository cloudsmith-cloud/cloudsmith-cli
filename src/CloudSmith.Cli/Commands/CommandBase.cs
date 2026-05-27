using System.CommandLine;

namespace CloudSmith.Cli.Commands;

/// <summary>
/// Base class for all CloudSmith CLI commands. Provides shared options and output helpers.
/// </summary>
public abstract class CommandBase : Command
{
    protected CommandBase(string name, string description)
        : base(name, description)
    {
    }

    /// <summary>
    /// Global output format option (table|json). Added to the root command so all sub-commands inherit it.
    /// </summary>
    public static Option<string> OutputOption { get; } = new Option<string>(
        aliases: ["--output", "-o"],
        description: "Output format: table (default) or json.",
        getDefaultValue: () => "table")
    {
        ArgumentHelpName = "format"
    };

    /// <summary>
    /// Global server override option. Added to the root command so all sub-commands inherit it.
    /// </summary>
    public static Option<string?> ServerOption { get; } = new Option<string?>(
        aliases: ["--server"],
        description: "Override the configured server URL.")
    {
        ArgumentHelpName = "url"
    };

    /// <summary>
    /// Verbose output option.
    /// </summary>
    public static Option<bool> VerboseOption { get; } = new Option<bool>(
        aliases: ["--verbose", "-v"],
        description: "Enable verbose output.");
}
