using System.CommandLine;
using System.Net.Http.Headers;
using System.Text.Json;
using CloudSmith.Cli.Output;
using CloudSmith.Cli.Services;

namespace CloudSmith.Cli.Commands;

/// <summary>
/// cs watch --job &lt;id&gt; — streams job progress via SSE (AB#1532).
/// Falls back to SSE (/api/v1/jobs/{id}/stream) when SignalR hub is unavailable.
/// </summary>
public static class WatchCommand
{
    private static readonly HashSet<string> TerminalStates =
        new(StringComparer.OrdinalIgnoreCase) { "Completed", "Failed", "Cancelled" };

    public static Command Build(IConfigService configService, IAuthService authService)
    {
        Command watch = new("watch", "Stream job progress in real time.");

        Option<string> jobOpt = new("--job", "Job ID to watch.") { IsRequired = true };
        watch.AddOption(jobOpt);

        watch.SetHandler(async (string job, string? serverOverride, string output) =>
        {
            string server = serverOverride ?? configService.Get("server") ?? "https://localhost";
            string? accessToken = authService.GetCurrentToken()?.AccessToken;

            int exitCode = await StreamJobAsync(server, job, accessToken);
            Environment.Exit(exitCode);
        }, jobOpt, CommandBase.ServerOption, CommandBase.OutputOption);

        return watch;
    }

    /// <summary>
    /// Streams the job SSE feed, printing timestamp-prefixed log lines.
    /// Returns 0 for Completed, 1 for Failed/Cancelled/error.
    /// An optional <paramref name="httpClientFactory"/> allows tests to inject a mock handler.
    /// </summary>
    internal static async Task<int> StreamJobAsync(
        string server,
        string jobId,
        string? accessToken,
        CancellationToken cancellationToken = default,
        Func<HttpClient>? httpClientFactory = null)
    {
        string sseUrl = $"{server.TrimEnd('/')}/api/v1/jobs/{Uri.EscapeDataString(jobId)}/stream";

        using HttpClient http = httpClientFactory is not null
            ? httpClientFactory()
            : new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        if (accessToken is not null)
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken);

        http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("text/event-stream"));

        string lastState = "";

        try
        {
            using HttpResponseMessage response = await http.GetAsync(
                sseUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                OutputWriter.WriteError(
                    $"SSE connect failed {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}");
                return 1;
            }

            using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using StreamReader reader = new(stream);

            string? line;
            string dataBuffer = "";

            while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
            {
                if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    dataBuffer = line["data:".Length..].Trim();
                }
                else if (line.Length == 0 && dataBuffer.Length > 0)
                {
                    // End of SSE event — process the buffered data
                    ProcessSseEvent(dataBuffer, ref lastState);
                    dataBuffer = "";

                    if (TerminalStates.Contains(lastState))
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            OutputWriter.WriteError("Watch cancelled.");
            return 1;
        }
        catch (Exception ex)
        {
            OutputWriter.WriteError($"Stream error: {ex.Message}");
            return 1;
        }

        return lastState.Equals("Completed", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
    }

    private static void ProcessSseEvent(string data, ref string lastState)
    {
        string timestamp = DateTimeOffset.Now.ToString("HH:mm:ss");

        // Try to parse as JSON job-event
        try
        {
            var evt = JsonSerializer.Deserialize<JobEventDto>(data,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (evt is not null)
            {
                if (evt.Status is not null)
                    lastState = evt.Status;

                string message = evt.Message ?? evt.Status ?? data;
                Console.WriteLine($"[{timestamp}] {message}");
                return;
            }
        }
        catch
        {
            // Not JSON — treat as a plain log line
        }

        Console.WriteLine($"[{timestamp}] {data}");
    }
}

// ---------------------------------------------------------------------------
// DTO
// ---------------------------------------------------------------------------

internal sealed class JobEventDto
{
    public string? Status  { get; set; }
    public string? Message { get; set; }
}
