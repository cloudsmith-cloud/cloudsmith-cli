using System.CommandLine;
using System.Text.Json;
using CloudSmith.Cli.Output;
using CloudSmith.Cli.Services;

namespace CloudSmith.Cli.Commands.Platform;

/// <summary>
/// AB#1951 — cs platform update check
/// Calls GET /api/v1/platform/update/check and displays version/update status.
/// </summary>
public static class PlatformUpdateCheckCommand
{
    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    public static Command Build(IConfigService configService, IAuthService authService)
    {
        Command check = new("check", "Check whether a platform update is available.");

        check.SetHandler(
            async (string? serverOverride, string output) =>
            {
                string server = serverOverride ?? configService.Get("server") ?? "https://localhost";
                int exitCode = await RunAsync(server, authService, output);
                Environment.Exit(exitCode);
            },
            CommandBase.ServerOption,
            CommandBase.OutputOption);

        return check;
    }

    /// <summary>
    /// Testable core: GET /api/v1/platform/update/check.
    /// Returns 0 = up to date, 1 = update available or error.
    /// </summary>
    internal static async Task<int> RunAsync(
        string server,
        IAuthService authService,
        string output,
        Func<HttpClient>? httpClientFactory = null)
    {
        using HttpClient http = httpClientFactory is not null
            ? httpClientFactory()
            : ClusterCommands.MakeClient(server, authService);

        try
        {
            HttpResponseMessage response =
                await http.GetAsync("api/v1/platform/update/check");
            string body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                OutputWriter.WriteError($"API error {(int)response.StatusCode}: {body}");
                return 1;
            }

            if (output.Equals("json", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine(body);
                return 0;
            }

            PlatformUpdateCheckResponse? status =
                JsonSerializer.Deserialize<PlatformUpdateCheckResponse>(body, JsonOpts);

            if (status is null)
            {
                OutputWriter.WriteError("Unexpected empty response from server.");
                return 1;
            }

            if (status.UpdateAvailable)
            {
                Console.WriteLine("CloudSmith Platform Update Check");
                OutputWriter.WriteTable(output, [
                    ("Current version",  status.CurrentVersion ?? ""),
                    ("Latest version",   status.LatestVersion  ?? ""),
                    ("Update available", "Yes"),
                    ("Release notes",    status.ReleaseNotes   ?? "")
                ], "Property", "Value");
                return 1;
            }

            Console.WriteLine($"CloudSmith is up to date (v{status.CurrentVersion}).");
            return 0;
        }
        catch (Exception ex)
        {
            OutputWriter.WriteError($"Request failed: {ex.Message}");
            return 1;
        }
    }
}

/// <summary>
/// Response model for GET /api/v1/platform/update/check (AB#1951).
/// </summary>
public sealed record PlatformUpdateCheckResponse(
    string? CurrentVersion,
    string? LatestVersion,
    bool    UpdateAvailable,
    string? ReleaseNotes
);
