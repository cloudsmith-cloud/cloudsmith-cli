using System.CommandLine;
using System.Text;
using System.Text.Json;
using CloudSmith.Cli.Output;
using CloudSmith.Cli.Services;

namespace CloudSmith.Cli.Commands;

public static class ModuleCommands
{
    public static Command Build(IConfigService configService, IAuthService authService)
    {
        Command module = new("module", "Manage CloudSmith modules.");

        module.AddCommand(BuildList(configService, authService));
        module.AddCommand(BuildInstall(configService, authService));
        module.AddCommand(BuildUninstall(configService, authService));
        module.AddCommand(BuildEnable(configService, authService));
        module.AddCommand(BuildDisable(configService, authService));

        return module;
    }

    // -------------------------------------------------------------------------
    // cs module list
    // -------------------------------------------------------------------------
    private static Command BuildList(IConfigService configService, IAuthService authService)
    {
        Command list = new("list", "List installed modules.");

        list.SetHandler(async (string? serverOverride, string output) =>
        {
            string server = serverOverride ?? configService.Get("server") ?? "https://localhost";
            using System.Net.Http.HttpClient http = ClusterCommands.MakeClient(server, authService);

            try
            {
                System.Net.Http.HttpResponseMessage response = await http.GetAsync("api/v1/modules");
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

                var modules = JsonSerializer.Deserialize<List<ModuleDto>>(body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

                var table = new Spectre.Console.Table()
                    .AddColumn("Id")
                    .AddColumn("Name")
                    .AddColumn("Version")
                    .AddColumn("Enabled");

                foreach (var m in modules)
                    table.AddRow(
                        Spectre.Console.Markup.Escape(m.Id      ?? ""),
                        Spectre.Console.Markup.Escape(m.Name    ?? ""),
                        Spectre.Console.Markup.Escape(m.Version ?? ""),
                        m.Enabled ? "[green]true[/]" : "[grey]false[/]");

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
    // cs module install <id> [--version x.y.z]
    // -------------------------------------------------------------------------
    private static Command BuildInstall(IConfigService configService, IAuthService authService)
    {
        Command install = new("install", "Install a module.");

        Argument<string> idArg      = new("id", "Module ID.");
        Option<string?>  versionOpt = new("--version", "Version to install.");

        install.AddArgument(idArg);
        install.AddOption(versionOpt);

        install.SetHandler(async (string id, string? version, string? serverOverride, string output) =>
        {
            string server = serverOverride ?? configService.Get("server") ?? "https://localhost";
            using System.Net.Http.HttpClient http = ClusterCommands.MakeClient(server, authService);

            try
            {
                var payload = version is not null ? new { id, version } : (object)new { id };
                string json = JsonSerializer.Serialize(payload);
                using StringContent content = new(json, Encoding.UTF8, "application/json");

                System.Net.Http.HttpResponseMessage response =
                    await http.PostAsync("api/v1/modules/install", content);
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

                var m = JsonSerializer.Deserialize<ModuleDto>(body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                OutputWriter.WriteMessage(output, $"Module '{m?.Name ?? id}' installed successfully.");
            }
            catch (Exception ex)
            {
                OutputWriter.WriteError($"Request failed: {ex.Message}");
                Environment.Exit(1);
            }
        }, idArg, versionOpt, CommandBase.ServerOption, CommandBase.OutputOption);

        return install;
    }

    // -------------------------------------------------------------------------
    // cs module uninstall <id>
    // -------------------------------------------------------------------------
    private static Command BuildUninstall(IConfigService configService, IAuthService authService)
    {
        Command uninstall = new("uninstall", "Remove a module.");

        Argument<string> idArg = new("id", "Module ID.");
        uninstall.AddArgument(idArg);

        uninstall.SetHandler(async (string id, string? serverOverride, string output) =>
        {
            string server = serverOverride ?? configService.Get("server") ?? "https://localhost";
            using System.Net.Http.HttpClient http = ClusterCommands.MakeClient(server, authService);

            try
            {
                System.Net.Http.HttpResponseMessage response =
                    await http.DeleteAsync($"api/v1/modules/{Uri.EscapeDataString(id)}");
                string body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    OutputWriter.WriteError($"API error {(int)response.StatusCode}: {body}");
                    Environment.Exit(1);
                    return;
                }

                if (output.Equals("json", StringComparison.OrdinalIgnoreCase))
                    OutputWriter.WriteJson(new { message = $"Module '{id}' uninstalled." });
                else
                    OutputWriter.WriteMessage(output, $"Module '{id}' uninstalled.");
            }
            catch (Exception ex)
            {
                OutputWriter.WriteError($"Request failed: {ex.Message}");
                Environment.Exit(1);
            }
        }, idArg, CommandBase.ServerOption, CommandBase.OutputOption);

        return uninstall;
    }

    // -------------------------------------------------------------------------
    // cs module enable <id>
    // -------------------------------------------------------------------------
    private static Command BuildEnable(IConfigService configService, IAuthService authService)
    {
        Command enable = new("enable", "Enable a module.");

        Argument<string> idArg = new("id", "Module ID.");
        enable.AddArgument(idArg);

        enable.SetHandler(async (string id, string? serverOverride, string output) =>
        {
            await PatchModuleState(id, "enable", server: serverOverride ?? configService.Get("server") ?? "https://localhost",
                authService, output);
        }, idArg, CommandBase.ServerOption, CommandBase.OutputOption);

        return enable;
    }

    // -------------------------------------------------------------------------
    // cs module disable <id>
    // -------------------------------------------------------------------------
    private static Command BuildDisable(IConfigService configService, IAuthService authService)
    {
        Command disable = new("disable", "Disable a module.");

        Argument<string> idArg = new("id", "Module ID.");
        disable.AddArgument(idArg);

        disable.SetHandler(async (string id, string? serverOverride, string output) =>
        {
            await PatchModuleState(id, "disable", server: serverOverride ?? configService.Get("server") ?? "https://localhost",
                authService, output);
        }, idArg, CommandBase.ServerOption, CommandBase.OutputOption);

        return disable;
    }

    // -------------------------------------------------------------------------
    // Shared enable/disable helper
    // -------------------------------------------------------------------------
    private static async Task PatchModuleState(string id, string action, string server,
        IAuthService authService, string output)
    {
        using System.Net.Http.HttpClient http = ClusterCommands.MakeClient(server, authService);

        try
        {
            System.Net.Http.HttpResponseMessage response =
                await http.PatchAsync($"api/v1/modules/{Uri.EscapeDataString(id)}/{action}",
                    new StringContent("{}", Encoding.UTF8, "application/json"));
            string body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                OutputWriter.WriteError($"API error {(int)response.StatusCode}: {body}");
                Environment.Exit(1);
                return;
            }

            if (output.Equals("json", StringComparison.OrdinalIgnoreCase))
                OutputWriter.WriteJson(new { id, action, message = $"Module '{id}' {action}d." });
            else
                OutputWriter.WriteMessage(output, $"Module '{id}' {action}d.");
        }
        catch (Exception ex)
        {
            OutputWriter.WriteError($"Request failed: {ex.Message}");
            Environment.Exit(1);
        }
    }
}

// ---------------------------------------------------------------------------
// DTO
// ---------------------------------------------------------------------------

internal sealed class ModuleDto
{
    public string? Id      { get; set; }
    public string? Name    { get; set; }
    public string? Version { get; set; }
    public bool    Enabled { get; set; }
}
