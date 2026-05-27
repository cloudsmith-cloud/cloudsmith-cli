using System.CommandLine;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CloudSmith.Cli.Output;
using CloudSmith.Cli.Services;

namespace CloudSmith.Cli.Commands;

public static class ClusterCommands
{
    public static Command Build(IConfigService configService, IAuthService authService)
    {
        Command cluster = new("cluster", "Manage CloudSmith clusters.");

        cluster.AddCommand(BuildList(configService, authService));
        cluster.AddCommand(BuildGet(configService, authService));
        cluster.AddCommand(BuildAdd(configService, authService));
        cluster.AddCommand(BuildRemove(configService, authService));

        return cluster;
    }

    // -------------------------------------------------------------------------
    // cs cluster list
    // -------------------------------------------------------------------------
    private static Command BuildList(IConfigService configService, IAuthService authService)
    {
        Command list = new("list", "List all clusters.");

        list.SetHandler(async (string? serverOverride, string output) =>
        {
            string server = serverOverride ?? configService.Get("server") ?? "https://localhost";
            using HttpClient http = MakeClient(server, authService);

            try
            {
                HttpResponseMessage response = await http.GetAsync("api/v1/clusters");
                string body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    OutputWriter.WriteError($"API error {(int)response.StatusCode}: {body}");
                    Environment.Exit(1);
                    return;
                }

                if (output.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(body);
                    return;
                }

                var clusters = JsonSerializer.Deserialize<List<ClusterDto>>(body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

                var table = new Spectre.Console.Table()
                    .AddColumn("Name")
                    .AddColumn("Status")
                    .AddColumn("NodeCount")
                    .AddColumn("Version");

                foreach (var c in clusters)
                    table.AddRow(
                        Spectre.Console.Markup.Escape(c.Name ?? ""),
                        Spectre.Console.Markup.Escape(c.Status ?? ""),
                        (c.NodeCount ?? 0).ToString(),
                        Spectre.Console.Markup.Escape(c.Version ?? ""));

                Spectre.Console.AnsiConsole.Write(table);
            }
            catch (Exception ex)
            {
                OutputWriter.WriteError($"Request failed: {ex.Message}");
                Environment.Exit(1);
            }
        }, CommandBase.ServerOption, CommandBase.OutputOption);

        return list;
    }

    // -------------------------------------------------------------------------
    // cs cluster get <name>
    // -------------------------------------------------------------------------
    private static Command BuildGet(IConfigService configService, IAuthService authService)
    {
        Command get = new("get", "Get details of a specific cluster.");

        Argument<string> nameArg = new("name", "Cluster name.");
        get.AddArgument(nameArg);

        get.SetHandler(async (string name, string? serverOverride, string output) =>
        {
            string server = serverOverride ?? configService.Get("server") ?? "https://localhost";
            using HttpClient http = MakeClient(server, authService);

            try
            {
                HttpResponseMessage response = await http.GetAsync($"api/v1/clusters/{Uri.EscapeDataString(name)}");
                string body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    OutputWriter.WriteError($"API error {(int)response.StatusCode}: {body}");
                    Environment.Exit(1);
                    return;
                }

                if (output.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(body);
                    return;
                }

                var c = JsonSerializer.Deserialize<ClusterDto>(body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (c is null)
                {
                    OutputWriter.WriteError("Unexpected empty response.");
                    Environment.Exit(1);
                    return;
                }

                OutputWriter.WriteTable(output, [
                    ("Name",      c.Name      ?? ""),
                    ("Status",    c.Status    ?? ""),
                    ("NodeCount", (c.NodeCount ?? 0).ToString()),
                    ("Version",   c.Version   ?? ""),
                    ("Host",      c.Host      ?? ""),
                    ("Port",      (c.Port ?? 0).ToString())
                ], "Field", "Value");
            }
            catch (Exception ex)
            {
                OutputWriter.WriteError($"Request failed: {ex.Message}");
                Environment.Exit(1);
            }
        }, nameArg, CommandBase.ServerOption, CommandBase.OutputOption);

        return get;
    }

    // -------------------------------------------------------------------------
    // cs cluster add
    // -------------------------------------------------------------------------
    private static Command BuildAdd(IConfigService configService, IAuthService authService)
    {
        Command add = new("add", "Register a new cluster.");

        Option<string> nameOpt = new("--name", "Cluster name.") { IsRequired = true };
        Option<string> hostOpt = new("--host", "Cluster host address.") { IsRequired = true };
        Option<int>    portOpt = new Option<int>("--port", () => 443, "Cluster port (default 443).");

        add.AddOption(nameOpt);
        add.AddOption(hostOpt);
        add.AddOption(portOpt);

        add.SetHandler(async (string name, string host, int port, string? serverOverride, string output) =>
        {
            string server = serverOverride ?? configService.Get("server") ?? "https://localhost";
            using HttpClient http = MakeClient(server, authService);

            try
            {
                var payload = new { name, host, port };
                string json = JsonSerializer.Serialize(payload);
                using StringContent content = new(json, Encoding.UTF8, "application/json");

                HttpResponseMessage response = await http.PostAsync("api/v1/clusters", content);
                string body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    OutputWriter.WriteError($"API error {(int)response.StatusCode}: {body}");
                    Environment.Exit(1);
                    return;
                }

                if (output.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(body);
                    return;
                }

                var c = JsonSerializer.Deserialize<ClusterDto>(body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                OutputWriter.WriteMessage(output, $"Cluster '{c?.Name ?? name}' registered successfully.");
            }
            catch (Exception ex)
            {
                OutputWriter.WriteError($"Request failed: {ex.Message}");
                Environment.Exit(1);
            }
        }, nameOpt, hostOpt, portOpt, CommandBase.ServerOption, CommandBase.OutputOption);

        return add;
    }

    // -------------------------------------------------------------------------
    // cs cluster remove <name>
    // -------------------------------------------------------------------------
    private static Command BuildRemove(IConfigService configService, IAuthService authService)
    {
        Command remove = new("remove", "Deregister a cluster.");

        Argument<string> nameArg  = new("name", "Cluster name.");
        Option<bool>     forceOpt = new("--force", "Skip confirmation prompt.");

        remove.AddArgument(nameArg);
        remove.AddOption(forceOpt);

        remove.SetHandler(async (string name, bool force, string? serverOverride, string output) =>
        {
            if (!force)
            {
                Console.Write($"Remove cluster '{name}'? [y/N] ");
                string? answer = Console.ReadLine();
                if (!string.Equals(answer?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
                {
                    OutputWriter.WriteMessage(output, "Aborted.");
                    return;
                }
            }

            string server = serverOverride ?? configService.Get("server") ?? "https://localhost";
            using HttpClient http = MakeClient(server, authService);

            try
            {
                HttpResponseMessage response = await http.DeleteAsync($"api/v1/clusters/{Uri.EscapeDataString(name)}");
                string body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    OutputWriter.WriteError($"API error {(int)response.StatusCode}: {body}");
                    Environment.Exit(1);
                    return;
                }

                if (output.Equals("json", StringComparison.OrdinalIgnoreCase))
                    OutputWriter.WriteJson(new { message = $"Cluster '{name}' removed." });
                else
                    OutputWriter.WriteMessage(output, $"Cluster '{name}' removed.");
            }
            catch (Exception ex)
            {
                OutputWriter.WriteError($"Request failed: {ex.Message}");
                Environment.Exit(1);
            }
        }, nameArg, forceOpt, CommandBase.ServerOption, CommandBase.OutputOption);

        return remove;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    internal static HttpClient MakeClient(string server, IAuthService authService)
    {
        HttpClient http = new() { BaseAddress = new Uri(server.TrimEnd('/') + "/") };
        CachedToken? token = authService.GetCurrentToken();
        if (token?.AccessToken is not null)
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return http;
    }
}

// ---------------------------------------------------------------------------
// DTOs
// ---------------------------------------------------------------------------

internal sealed class ClusterDto
{
    public string? Name      { get; set; }
    public string? Status    { get; set; }
    public int?    NodeCount { get; set; }
    public string? Version   { get; set; }
    public string? Host      { get; set; }
    public int?    Port      { get; set; }
}
