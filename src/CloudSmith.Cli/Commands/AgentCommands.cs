using System.CommandLine;
using System.Text;
using System.Text.Json;
using CloudSmith.Cli.Output;
using CloudSmith.Cli.Services;

namespace CloudSmith.Cli.Commands;

/// <summary>
/// cs agent — per-host agent management (AB#1531).
/// Note: the ADO story title uses "runner" — this CLI implements the correct "agent" terminology.
/// </summary>
public static class AgentCommands
{
    public static Command Build(IConfigService configService, IAuthService authService)
    {
        Command agent = new("agent", "Manage CloudSmith agents.");

        agent.AddCommand(BuildList(configService, authService));
        agent.AddCommand(BuildRegister(configService, authService));
        agent.AddCommand(BuildRemove(configService, authService));

        return agent;
    }

    // -------------------------------------------------------------------------
    // cs agent list
    // -------------------------------------------------------------------------
    private static Command BuildList(IConfigService configService, IAuthService authService)
    {
        Command list = new("list", "List registered agents.");

        list.SetHandler(async (string? serverOverride, string output) =>
        {
            string server = serverOverride ?? configService.Get("server") ?? "https://localhost";
            using System.Net.Http.HttpClient http = ClusterCommands.MakeClient(server, authService);

            try
            {
                System.Net.Http.HttpResponseMessage response = await http.GetAsync("api/v1/agents");
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

                var agents = JsonSerializer.Deserialize<List<AgentDto>>(body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

                var table = new Spectre.Console.Table()
                    .AddColumn("Id")
                    .AddColumn("Hostname")
                    .AddColumn("Status")
                    .AddColumn("LastSeen");

                foreach (var a in agents)
                    table.AddRow(
                        Spectre.Console.Markup.Escape(a.Id       ?? ""),
                        Spectre.Console.Markup.Escape(a.Hostname ?? ""),
                        Spectre.Console.Markup.Escape(a.Status   ?? ""),
                        Spectre.Console.Markup.Escape(a.LastSeen ?? ""));

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
    // cs agent register --host <hostname> --token <token>
    // -------------------------------------------------------------------------
    private static Command BuildRegister(IConfigService configService, IAuthService authService)
    {
        Command register = new("register", "Register an agent.");

        Option<string> hostOpt  = new("--host",  "Agent hostname.") { IsRequired = true };
        Option<string> tokenOpt = new("--token", "Agent registration token.") { IsRequired = true };

        register.AddOption(hostOpt);
        register.AddOption(tokenOpt);

        register.SetHandler(async (string host, string token, string? serverOverride, string output) =>
        {
            string server = serverOverride ?? configService.Get("server") ?? "https://localhost";
            using System.Net.Http.HttpClient http = ClusterCommands.MakeClient(server, authService);

            try
            {
                var payload = new { hostname = host, token };
                string json = JsonSerializer.Serialize(payload);
                using StringContent content = new(json, Encoding.UTF8, "application/json");

                System.Net.Http.HttpResponseMessage response =
                    await http.PostAsync("api/v1/agents", content);
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

                var agent = JsonSerializer.Deserialize<AgentDto>(body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                OutputWriter.WriteTable(output, [
                    ("Id",       agent?.Id       ?? ""),
                    ("Hostname", agent?.Hostname ?? host),
                    ("Status",   agent?.Status   ?? "")
                ], "Field", "Value");
            }
            catch (Exception ex)
            {
                OutputWriter.WriteError($"Request failed: {ex.Message}");
                Environment.Exit(1);
            }
        }, hostOpt, tokenOpt, CommandBase.ServerOption, CommandBase.OutputOption);

        return register;
    }

    // -------------------------------------------------------------------------
    // cs agent remove <id> [--force]
    // -------------------------------------------------------------------------
    private static Command BuildRemove(IConfigService configService, IAuthService authService)
    {
        Command remove = new("remove", "Deregister an agent.");

        Argument<string> idArg    = new("id", "Agent ID.");
        Option<bool>     forceOpt = new("--force", "Skip confirmation prompt.");

        remove.AddArgument(idArg);
        remove.AddOption(forceOpt);

        remove.SetHandler(async (string id, bool force, string? serverOverride, string output) =>
        {
            if (!force)
            {
                Console.Write($"Remove agent '{id}'? [y/N] ");
                string? answer = Console.ReadLine();
                if (!string.Equals(answer?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
                {
                    OutputWriter.WriteMessage(output, "Aborted.");
                    return;
                }
            }

            string server = serverOverride ?? configService.Get("server") ?? "https://localhost";
            using System.Net.Http.HttpClient http = ClusterCommands.MakeClient(server, authService);

            try
            {
                System.Net.Http.HttpResponseMessage response =
                    await http.DeleteAsync($"api/v1/agents/{Uri.EscapeDataString(id)}");
                string body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    OutputWriter.WriteError($"API error {(int)response.StatusCode}: {body}");
                    Environment.Exit(1);
                    return;
                }

                if (output.Equals("json", StringComparison.OrdinalIgnoreCase))
                    OutputWriter.WriteJson(new { message = $"Agent '{id}' removed." });
                else
                    OutputWriter.WriteMessage(output, $"Agent '{id}' removed.");
            }
            catch (Exception ex)
            {
                OutputWriter.WriteError($"Request failed: {ex.Message}");
                Environment.Exit(1);
            }
        }, idArg, forceOpt, CommandBase.ServerOption, CommandBase.OutputOption);

        return remove;
    }
}

// ---------------------------------------------------------------------------
// DTO
// ---------------------------------------------------------------------------

internal sealed class AgentDto
{
    public string? Id       { get; set; }
    public string? Hostname { get; set; }
    public string? Status   { get; set; }
    public string? LastSeen { get; set; }
}
