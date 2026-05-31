using System.CommandLine;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CloudSmith.Cli.Commands;
using CloudSmith.Cli.Output;
using CloudSmith.Cli.Services;

namespace CloudSmith.Cli.Commands.Platform;

/// <summary>
/// AB#1955 — cs platform update [--check | --apply | --rollback] [--yes]
/// </summary>
public static class PlatformUpdateCommands
{
    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    private static readonly HashSet<string> TerminalEvents =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "platform:update:complete",
            "platform:update:failed"
        };

    // -------------------------------------------------------------------------
    // Public factory — builds "update" sub-command under "platform"
    // -------------------------------------------------------------------------

    public static Command Build(IConfigService configService, IAuthService authService)
    {
        Command update = new("update", "Check, apply, or roll back a platform update.");

        // AB#1951 — named subcommands: cs platform update check / apply
        update.AddCommand(PlatformUpdateCheckCommand.Build(configService, authService));
        update.AddCommand(PlatformUpdateApplyCommand.Build(configService, authService));

        Option<bool> checkOpt    = new("--check",    "Check for available updates (default).");
        Option<bool> applyOpt    = new("--apply",    "Download and apply the latest update.");
        Option<bool> rollbackOpt = new("--rollback", "Roll back to the previous platform version.");
        Option<bool> yesOpt      = new(["--yes", "-y"], "Skip confirmation prompts.");

        update.AddOption(checkOpt);
        update.AddOption(applyOpt);
        update.AddOption(rollbackOpt);
        update.AddOption(yesOpt);

        update.SetHandler(
            async (bool check, bool apply, bool rollback, bool yes,
                   string? serverOverride, string output) =>
            {
                string server = serverOverride ?? configService.Get("server") ?? "https://localhost";

                // Default behaviour: --check when no mode flag is supplied
                if (!apply && !rollback)
                    check = true;

                if (check)
                {
                    int exitCode = await RunCheckAsync(server, authService, output);
                    Environment.Exit(exitCode);
                    return;
                }

                if (apply)
                {
                    int exitCode = await RunApplyAsync(
                        server, authService, output, yes, rollback: false);
                    Environment.Exit(exitCode);
                    return;
                }

                if (rollback)
                {
                    int exitCode = await RunApplyAsync(
                        server, authService, output, yes, rollback: true);
                    Environment.Exit(exitCode);
                }
            },
            checkOpt, applyOpt, rollbackOpt, yesOpt,
            CommandBase.ServerOption, CommandBase.OutputOption);

        return update;
    }

    // -------------------------------------------------------------------------
    // --check: GET /api/v1/platform/updates/check
    // -------------------------------------------------------------------------

    internal static async Task<int> RunCheckAsync(
        string server, IAuthService authService, string output,
        Func<HttpClient>? httpClientFactory = null)
    {
        using HttpClient http = httpClientFactory is not null
            ? httpClientFactory()
            : ClusterCommands.MakeClient(server, authService);

        try
        {
            HttpResponseMessage response =
                await http.GetAsync("api/v1/platform/updates/check");
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

            PlatformUpdateStatus? status =
                JsonSerializer.Deserialize<PlatformUpdateStatus>(body, JsonOpts);

            if (status is null)
            {
                OutputWriter.WriteError("Unexpected empty response from server.");
                return 1;
            }

            if (status.UpdateAvailable)
            {
                OutputWriter.WriteTable(output, [
                    ("Current version",  status.CurrentVersion),
                    ("Latest version",   status.LatestVersion),
                    ("Update available", "Yes")
                ], "Property", "Value");

                // Exit 1 = update available (useful for scripting)
                return 1;
            }
            else
            {
                OutputWriter.WriteTable(output, [
                    ("Current version", status.CurrentVersion),
                    ("Latest version",  status.LatestVersion),
                    ("Up to date.",     "")
                ], "Property", "Value");

                return 0;
            }
        }
        catch (Exception ex)
        {
            OutputWriter.WriteError($"Request failed: {ex.Message}");
            return 1;
        }
    }

    // -------------------------------------------------------------------------
    // --apply / --rollback: PUT /api/v1/platform/updates/apply
    // -------------------------------------------------------------------------

    internal static async Task<int> RunApplyAsync(
        string server, IAuthService authService, string output, bool yes, bool rollback,
        Func<HttpClient>? httpClientFactory = null,
        CancellationToken cancellationToken = default)
    {
        string action = rollback ? "rollback" : "apply";

        if (!yes)
        {
            string prompt = rollback
                ? "Roll back to the previous platform version? [y/N] "
                : "Apply platform update? [y/N] ";
            Console.Write(prompt);
            string? answer = Console.ReadLine();
            if (!string.Equals(answer?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
            {
                OutputWriter.WriteMessage(output, "Aborted.");
                return 0;
            }
        }

        // First call --check to get the target version for display
        string targetVersion = "latest";
        try
        {
            using HttpClient checkHttp = httpClientFactory is not null
                ? httpClientFactory()
                : ClusterCommands.MakeClient(server, authService);

            HttpResponseMessage checkResponse =
                await checkHttp.GetAsync("api/v1/platform/updates/check", cancellationToken);

            if (checkResponse.IsSuccessStatusCode)
            {
                string checkBody = await checkResponse.Content.ReadAsStringAsync(cancellationToken);
                PlatformUpdateStatus? status =
                    JsonSerializer.Deserialize<PlatformUpdateStatus>(checkBody, JsonOpts);

                if (status is not null)
                    targetVersion = status.LatestVersion;
            }
        }
        catch
        {
            // Non-fatal: display "latest" if check fails
        }

        if (!output.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            string verb = rollback ? "Rolling back platform..." : $"Applying update to {targetVersion}...";
            Console.WriteLine($"{verb} (press Ctrl+C to detach, update will continue)");
        }

        using HttpClient http = httpClientFactory is not null
            ? httpClientFactory()
            : ClusterCommands.MakeClient(server, authService);

        try
        {
            var payload = new { rollback };
            string json = JsonSerializer.Serialize(payload);
            using StringContent content = new(json, Encoding.UTF8, "application/json");

            HttpResponseMessage response =
                await http.PutAsync("api/v1/platform/updates/apply", content, cancellationToken);
            string body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound ||
                !response.IsSuccessStatusCode && (int)response.StatusCode < 500)
            {
                // Treat 4xx as a hard error (bad request, auth, etc.)
                OutputWriter.WriteError($"API error {(int)response.StatusCode}: {body}");
                return 1;
            }

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

            // Parse the 202 Accepted body to get the update ID for streaming
            PlatformUpdateResponse? updateResp =
                JsonSerializer.Deserialize<PlatformUpdateResponse>(body, JsonOpts);

            if (updateResp is null)
            {
                OutputWriter.WriteMessage(output, $"Platform {action} queued.");
                return 0;
            }

            // Stream progress via SSE
            return await StreamUpdateProgressAsync(
                server, updateResp.UpdateId.ToString(), authService,
                targetVersion, rollback, cancellationToken, httpClientFactory);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            OutputWriter.WriteError("Detached from update stream. Update continues on the server.");
            return 0;
        }
        catch (Exception ex)
        {
            OutputWriter.WriteError($"Request failed: {ex.Message}");
            return 1;
        }
    }

    // -------------------------------------------------------------------------
    // SSE streaming for platform update progress
    // -------------------------------------------------------------------------

    private static async Task<int> StreamUpdateProgressAsync(
        string server,
        string updateId,
        IAuthService authService,
        string targetVersion,
        bool rollback,
        CancellationToken cancellationToken,
        Func<HttpClient>? httpClientFactory)
    {
        string sseUrl =
            $"{server.TrimEnd('/')}/api/v1/platform/updates/{Uri.EscapeDataString(updateId)}/stream";

        using HttpClient http = httpClientFactory is not null
            ? httpClientFactory()
            : new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        CachedToken? token = authService.GetCurrentToken();
        if (token?.AccessToken is not null)
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token.AccessToken);

        http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("text/event-stream"));

        string lastEvent = "";

        try
        {
            using HttpResponseMessage response = await http.GetAsync(
                sseUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // Non-fatal: the apply was accepted — progress just can't be streamed
                OutputWriter.WriteMessage("table",
                    $"Platform {(rollback ? "rollback" : "update")} queued (unable to stream progress).");
                return 0;
            }

            using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using StreamReader reader = new(stream);

            string? line;
            string dataBuffer = "";

            while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
            {
                if (line.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
                {
                    lastEvent = line["event:".Length..].Trim();
                }
                else if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    dataBuffer = line["data:".Length..].Trim();
                }
                else if (line.Length == 0 && dataBuffer.Length > 0)
                {
                    PrintProgressLine(dataBuffer, lastEvent);
                    dataBuffer = "";

                    if (TerminalEvents.Contains(lastEvent))
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Console.WriteLine("Detached. Update continues in the background.");
            return 0;
        }
        catch (Exception ex)
        {
            // Stream lost — not necessarily a failure (update may still succeed)
            OutputWriter.WriteError($"Stream error: {ex.Message}");
            return 1;
        }

        bool succeeded = lastEvent.Equals("platform:update:complete",
            StringComparison.OrdinalIgnoreCase);

        if (succeeded)
            Console.WriteLine($"Update complete. Running {targetVersion}.");
        else
            OutputWriter.WriteError("Update failed. Check server logs for details.");

        return succeeded ? 0 : 1;
    }

    private static void PrintProgressLine(string data, string eventName)
    {
        string timestamp = DateTimeOffset.Now.ToString("HH:mm:ss");

        try
        {
            var evt = JsonSerializer.Deserialize<PlatformProgressEventDto>(data, JsonOpts);
            if (evt?.Message is not null)
            {
                Console.WriteLine($"[{timestamp}] {evt.Message}");
                return;
            }
        }
        catch
        {
            // Plain-text event
        }

        Console.WriteLine($"[{timestamp}] {data}");
    }
}

// ---------------------------------------------------------------------------
// Internal DTO for SSE event payloads
// ---------------------------------------------------------------------------

internal sealed class PlatformProgressEventDto
{
    public string? Message { get; set; }
    public string? Status  { get; set; }
}
