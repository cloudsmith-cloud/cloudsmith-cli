using System.CommandLine;
using CloudSmith.Cli.Commands.Platform;
using CloudSmith.Cli.Services;

namespace CloudSmith.Cli.Commands;

/// <summary>
/// AB#1955 — cs platform command group.
/// </summary>
public static class PlatformCommands
{
    public static Command Build(IConfigService configService, IAuthService authService)
    {
        Command platform = new("platform", "Manage the CloudSmith platform itself.");

        platform.AddCommand(PlatformUpdateCommands.Build(configService, authService));

        return platform;
    }
}
