using System.CommandLine;
using System.Text;
using System.Text.Json;
using CloudSmith.Cli.Commands.Module;
using CloudSmith.Cli.Output;
using CloudSmith.Cli.Services;

namespace CloudSmith.Cli.Commands;

public static class ModuleCommands
{
    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    public static Command Build(IConfigService configService, IAuthService authService)
    {
        Command module = new("module", "Manage CloudSmith modules.");

        module.AddCommand(BuildList(configService, authService));
        module.AddCommand(BuildInstall(configService, authService));
        module.AddCommand(BuildUninstall(configService, authService));
        module.AddCommand(BuildEnable(configService, authService));
        module.AddCommand(BuildDisable(configService, authService));

        // AB#1925 — published module catalog
        module.AddCommand(CatalogCommands.Build(configService, authService));

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

                var modules = JsonSerializer.Deserialize<List<ModuleDto>>(body, JsonOpts) ?? [];

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
    // cs module install <id> [--version x.y.z] [--from-catalog]
    // -------------------------------------------------------------------------
    private static Command BuildInstall(IConfigService configService, IAuthService authService)
    {
        Command install = new("install", "Install a module.");

        Argument<string> idArg          = new("id", "Module ID.");
        Option<string?>  versionOpt     = new("--version", "Version to install.");
        Option<bool>     fromCatalogOpt = new("--from-catalog",
            "Look up the module in the published catalog before installing; prompts if not verified.");

        install.AddArgument(idArg);
        install.AddOption(versionOpt);
        install.AddOption(fromCatalogOpt);

        install.SetHandler(async (string id, string? version, bool fromCatalog,
            string? serverOverride, string output) =>
        {
            string server = serverOverride ?? configService.Get("server") ?? "https://localhost";
            using System.Net.Http.HttpClient http = ClusterCommands.MakeClient(server, authService);

            try
            {
                // --from-catalog: lookup entry, warn if unverified, then install
                if (fromCatalog)
                {
                    System.Net.Http.HttpResponseMessage catalogResponse =
                        await http.GetAsync("api/v1/modules/catalog");
                    string catalogBody = await catalogResponse.Content.ReadAsStringAsync();

                    if (!catalogResponse.IsSuccessStatusCode)
                    {
                        OutputWriter.WriteError(
                            $"Catalog lookup failed {(int)catalogResponse.StatusCode}: {catalogBody}");
                        Environment.Exit(1);
                        return;
                    }

                    CatalogResponse? catalog =
                        JsonSerializer.Deserialize<CatalogResponse>(catalogBody, JsonOpts);
                    CatalogEntry? entry = catalog?.Items.FirstOrDefault(
                        e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));

                    if (entry is null)
                    {
                        OutputWriter.WriteError(
                            $"Module '{id}' not found in the published catalog.");
                        Environment.Exit(1);
                        return;
                    }

                    // Use catalog version if no explicit version supplied
                    version ??= entry.Version;

                    if (!entry.IsVerified)
                    {
                        Spectre.Console.AnsiConsole.MarkupLine(
                            "[yellow]Warning:[/] Module is not cosign-verified. Install anyway? [y/N] ");
                        string? answer = Console.ReadLine();
                        if (!string.Equals(answer?.Trim(), "y",
                            StringComparison.OrdinalIgnoreCase))
                        {
                            OutputWriter.WriteMessage(output, "Aborted.");
                            return;
                        }
                    }
                }

                // POST /api/v1/modules/{id}/install
                var payload = version is not null ? (object)new { version } : new { };
                string json = JsonSerializer.Serialize(payload);
                using StringContent content = new(json, Encoding.UTF8, "application/json");

                System.Net.Http.HttpResponseMessage response =
                    await http.PostAsync(
                        $"api/v1/modules/{Uri.EscapeDataString(id)}/install", content);
                string body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    // Fall back to legacy endpoint for backward compatibility
                    var legacyPayload = version is not null
                        ? (object)new { id, version }
                        : new { id };
                    string legacyJson = JsonSerializer.Serialize(legacyPayload);
                    using StringContent legacyContent =
                        new(legacyJson, Encoding.UTF8, "application/json");

                    response = await http.PostAsync("api/v1/modules/install", legacyContent);
                    body     = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        OutputWriter.WriteError($"API error {(int)response.StatusCode}: {body}");
                        Environment.Exit(1);
                        return;
                    }
                }

                if (output.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(body);
                    return;
                }

                // Try to deserialize as ModuleDto to extract name/version for the message
                ModuleDto? m = null;
                try
                {
                    m = JsonSerializer.Deserialize<ModuleDto>(body, JsonOpts);
                }
                catch { /* not a ModuleDto — that's fine */ }

                string displayName    = m?.Name    ?? id;
                string displayVersion = m?.Version ?? version ?? "";

                string versionSuffix = displayVersion.Length > 0 ? $" v{displayVersion}" : "";
                Spectre.Console.AnsiConsole.MarkupLine(
                    $"[green]checkmark[/]  Module '[bold]{Spectre.Console.Markup.Escape(displayName)}[/]'" +
                    $"{Spectre.Console.Markup.Escape(versionSuffix)} queued for installation.");
            }
            catch (Exception ex)
            {
                OutputWriter.WriteError($"Request failed: {ex.Message}");
                Environment.Exit(1);
            }
        }, idArg, versionOpt, fromCatalogOpt, CommandBase.ServerOption, CommandBase.OutputOption);

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
            await PatchModuleState(id, "enable",
                server: serverOverride ?? configService.Get("server") ?? "https://localhost",
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
            await PatchModuleState(id, "disable",
                server: serverOverride ?? configService.Get("server") ?? "https://localhost",
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
