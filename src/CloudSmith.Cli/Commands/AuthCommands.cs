using System.CommandLine;
using CloudSmith.Cli.Output;
using CloudSmith.Cli.Services;

namespace CloudSmith.Cli.Commands;

public static class AuthCommands
{
    public static Command Build(IAuthService authService, IConfigService configService)
    {
        Command auth = new("auth", "Authenticate with a CloudSmith server.");

        auth.AddCommand(BuildLogin(authService, configService));
        auth.AddCommand(BuildLogout(authService));

        return auth;
    }

    // -------------------------------------------------------------------------
    // cs auth login
    // -------------------------------------------------------------------------
    private static Command BuildLogin(IAuthService authService, IConfigService configService)
    {
        Command login = new("login", "Log in via device-code flow and cache credentials.");

        // Use global options inherited from root (--server, --output, --verbose)
        login.SetHandler(async (string? serverOverride, string output) =>
        {
            string server = serverOverride
                ?? configService.Get("server")
                ?? "https://localhost";

            try
            {
                CachedToken token = await authService.LoginAsync(server);

                if (output.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    OutputWriter.WriteJson(new
                    {
                        upn     = token.Upn,
                        expires = token.ExpiresOn.ToString("O"),
                        server  = token.Server
                    });
                }
                else
                {
                    OutputWriter.WriteTable(output, [
                        ("Logged in as", token.Upn ?? "unknown"),
                        ("Expires",      token.ExpiresOn.ToLocalTime().ToString("g")),
                        ("Server",       token.Server ?? server)
                    ]);
                }
            }
            catch (OperationCanceledException)
            {
                OutputWriter.WriteError("Login cancelled.");
                Environment.Exit(2);
            }
            catch (Exception ex)
            {
                OutputWriter.WriteError($"Login failed: {ex.Message}");
                Environment.Exit(2);
            }
        }, CommandBase.ServerOption, CommandBase.OutputOption);

        return login;
    }

    // -------------------------------------------------------------------------
    // cs auth logout
    // -------------------------------------------------------------------------
    private static Command BuildLogout(IAuthService authService)
    {
        Command logout = new("logout", "Revoke your token and clear local credentials.");

        logout.SetHandler(async (string output) =>
        {
            try
            {
                await authService.LogoutAsync();

                if (output.Equals("json", StringComparison.OrdinalIgnoreCase))
                    OutputWriter.WriteJson(new { message = "Logged out successfully." });
                else
                    OutputWriter.WriteMessage(output, "Logged out successfully.");
            }
            catch (Exception ex)
            {
                OutputWriter.WriteError($"Logout failed: {ex.Message}");
                Environment.Exit(2);
            }
        }, CommandBase.OutputOption);

        return logout;
    }
}
