using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using CloudSmith.Cli.Commands;
using CloudSmith.Cli.Services;

// ---------------------------------------------------------------------------
// Services
// ---------------------------------------------------------------------------
IConfigService configService  = new ConfigService();
ITokenCacheService tokenCache = new TokenCacheService();
IAuthService authService      = new AuthService(tokenCache, configService);

// ---------------------------------------------------------------------------
// Root command
// ---------------------------------------------------------------------------
RootCommand root = new("cs — CloudSmith command-line interface.");

// Add global options
root.AddGlobalOption(CommandBase.OutputOption);
root.AddGlobalOption(CommandBase.ServerOption);
root.AddGlobalOption(CommandBase.VerboseOption);

// ---------------------------------------------------------------------------
// Sub-commands
// ---------------------------------------------------------------------------
root.AddCommand(AuthCommands.Build(authService, configService));
root.AddCommand(ConfigCommands.Build(configService));
root.AddCommand(ClusterCommands.Build(configService, authService));
root.AddCommand(DeployCommands.Build(configService, authService));
root.AddCommand(ModuleCommands.Build(configService, authService));
root.AddCommand(AgentCommands.Build(configService, authService));
root.AddCommand(PlatformCommands.Build(configService, authService));
root.AddCommand(WatchCommand.Build(configService, authService));

// ---------------------------------------------------------------------------
// Pipeline with defaults (--help, --version, error handling)
// ---------------------------------------------------------------------------
Parser parser = new CommandLineBuilder(root)
    .UseDefaults()   // wires --help, --version, error reporting
    .Build();

return await parser.InvokeAsync(args);
