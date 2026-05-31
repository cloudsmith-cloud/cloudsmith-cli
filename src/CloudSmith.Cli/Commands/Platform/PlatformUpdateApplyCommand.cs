using System.CommandLine;
using System.Text;
using System.Text.Json;
using CloudSmith.Cli.Output;
using CloudSmith.Cli.Services;

namespace CloudSmith.Cli.Commands.Platform;

/// <summary>
/// AB#1951 — cs platform update apply
/// Calls POST /api/v1/platform/update/apply and displays the queued job ID.
/// </summary>
public static class PlatformUpdateApplyCommand
{
    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    public static Command Build(IConfigService configService, IAuthService authService)
    {
        Command apply = new("apply", "Apply the latest available platform update.");

        Option<bool> yesOpt = new(["--yes", "-y"], "Skip confirmation prompt.");
        apply.AddOption(yesOpt);

        apply.SetHandler(
            async (bool yes, string? serverOverride, string output) =>
            {
                string server = serverOverride ?? configService.Get("server") ?? "https://localhost";
                int exitCode = await RunAsync(server, authService, output, yes);
                Environment.Exit(exitCode);
            },
            yesOpt,
            CommandBase.ServerOption,
            CommandBase.OutputOption);

        return apply;
    }

    /// <summary>
    /// Testable core: POST /api/v1/platform/update/apply.
    /// Returns 0 = queued successfully, 1 = error.
    /// </summary>
    internal static async Task<int> RunAsync(
        string server,
        IAuthService authService,
        string output,
        bool yes,
        Func<HttpClient>? httpClientFactory = null)
    {
        if (!yes)
        {
            Console.Write("Apply platform update? [y/N] ");
            string? answer = Console.ReadLine();
            if (!string.Equals(answer?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
            {
                OutputWriter.WriteMessage(output, "Aborted.");
                return 0;
            }
        }

        using HttpClient http = httpClientFactory is not null
            ? httpClientFactory()
            : ClusterCommands.MakeClient(server, authService);

        try
        {
            string json = JsonSerializer.Serialize(new { });
            using StringContent content = new(json, Encoding.UTF8, "application/json");

            HttpResponseMessage response =
                await http.PostAsync("api/v1/platform/update/apply", content);
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

            PlatformUpdateApplyResponse? applyResp =
                JsonSerializer.Deserialize<PlatformUpdateApplyResponse>(body, JsonOpts);

            if (applyResp is not null)
            {
                Console.WriteLine($"Update queued. Job ID: {applyResp.JobId}");
                Console.WriteLine("Run 'cs platform update check' in a few minutes to confirm.");
            }
            else
            {
                Console.WriteLine("Update queued.");
                Console.WriteLine("Run 'cs platform update check' in a few minutes to confirm.");
            }

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
/// Response model for POST /api/v1/platform/update/apply (AB#1951).
/// </summary>
public sealed record PlatformUpdateApplyResponse(
    string? JobId,
    string? Status,
    string? Message
);
