using System.CommandLine;
using System.Text.Json;
using CloudSmith.Cli.Commands;
using CloudSmith.Cli.Output;
using CloudSmith.Cli.Services;

namespace CloudSmith.Cli.Commands.Module;

/// <summary>
/// AB#1925 — cs module catalog list / cs module catalog get
/// </summary>
public static class CatalogCommands
{
    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Builds the "catalog" sub-command group and returns it for wiring into "module".
    /// </summary>
    public static Command Build(IConfigService configService, IAuthService authService)
    {
        Command catalog = new("catalog", "Browse the published module catalog.");

        catalog.AddCommand(BuildList(configService, authService));
        catalog.AddCommand(BuildGet(configService, authService));

        return catalog;
    }

    // -------------------------------------------------------------------------
    // cs module catalog list [--installed] [--verified] [--output <table|json>]
    // -------------------------------------------------------------------------

    private static Command BuildList(IConfigService configService, IAuthService authService)
    {
        Command list = new("list", "List modules in the published catalog.");

        Option<bool> installedOpt = new("--installed", "Show only installed modules.");
        Option<bool> verifiedOpt  = new("--verified",  "Show only cosign-verified modules.");

        list.AddOption(installedOpt);
        list.AddOption(verifiedOpt);

        list.SetHandler(async (bool installed, bool verified, string? serverOverride, string output) =>
        {
            string server = serverOverride ?? configService.Get("server") ?? "https://localhost";
            using System.Net.Http.HttpClient http = ClusterCommands.MakeClient(server, authService);

            try
            {
                System.Net.Http.HttpResponseMessage response =
                    await http.GetAsync("api/v1/modules/catalog");
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

                CatalogResponse? catalog = JsonSerializer.Deserialize<CatalogResponse>(body, JsonOpts);
                List<CatalogEntry> items = catalog?.Items ?? [];

                // Apply filters
                if (installed) items = items.Where(e => e.IsInstalled).ToList();
                if (verified)  items = items.Where(e => e.IsVerified).ToList();

                var table = new Spectre.Console.Table()
                    .AddColumn("Id")
                    .AddColumn("Name")
                    .AddColumn("Version")
                    .AddColumn("Publisher")
                    .AddColumn("Verified")
                    .AddColumn("Installed");

                foreach (CatalogEntry e in items)
                    table.AddRow(
                        Spectre.Console.Markup.Escape(e.Id),
                        Spectre.Console.Markup.Escape(e.Name),
                        Spectre.Console.Markup.Escape(e.Version),
                        Spectre.Console.Markup.Escape(e.Publisher),
                        e.IsVerified  ? "[green]checkmark[/]" : "",
                        e.IsInstalled ? "[blue]installed[/]"  : "");

                Spectre.Console.AnsiConsole.Write(table);
            }
            catch (Exception ex)
            {
                OutputWriter.WriteError($"Request failed: {ex.Message}");
                Environment.Exit(1);
            }
        }, installedOpt, verifiedOpt, CommandBase.ServerOption, CommandBase.OutputOption);

        return list;
    }

    // -------------------------------------------------------------------------
    // cs module catalog get <id> [--output <table|json>]
    // -------------------------------------------------------------------------

    private static Command BuildGet(IConfigService configService, IAuthService authService)
    {
        Command get = new("get", "Get details of a single catalog entry.");

        Argument<string> idArg = new("id", "Module ID to look up.");
        get.AddArgument(idArg);

        get.SetHandler(async (string id, string? serverOverride, string output) =>
        {
            string server = serverOverride ?? configService.Get("server") ?? "https://localhost";
            using System.Net.Http.HttpClient http = ClusterCommands.MakeClient(server, authService);

            try
            {
                System.Net.Http.HttpResponseMessage response =
                    await http.GetAsync("api/v1/modules/catalog");
                string body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    OutputWriter.WriteError($"API error {(int)response.StatusCode}: {body}");
                    Environment.Exit(1);
                    return;
                }

                CatalogResponse? catalog = JsonSerializer.Deserialize<CatalogResponse>(body, JsonOpts);
                CatalogEntry? entry = catalog?.Items.FirstOrDefault(
                    e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));

                if (entry is null)
                {
                    OutputWriter.WriteError($"Catalog entry '{id}' not found.");
                    Environment.Exit(1);
                    return;
                }

                if (output.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(JsonSerializer.Serialize(entry,
                        new JsonSerializerOptions { WriteIndented = true }));
                    return;
                }

                OutputWriter.WriteTable(output, [
                    ("Id",           entry.Id),
                    ("Name",         entry.Name),
                    ("Version",      entry.Version),
                    ("Description",  entry.Description),
                    ("Publisher",    entry.Publisher),
                    ("GhcrImageRef", entry.GhcrImageRef),
                    ("ManifestUrl",  entry.ManifestUrl),
                    ("SignatureRef", entry.SignatureRef ?? ""),
                    ("IsVerified",   entry.IsVerified.ToString()),
                    ("IsInstalled",  entry.IsInstalled.ToString()),
                    ("IsEnabled",    entry.IsEnabled.ToString())
                ], "Field", "Value");
            }
            catch (Exception ex)
            {
                OutputWriter.WriteError($"Request failed: {ex.Message}");
                Environment.Exit(1);
            }
        }, idArg, CommandBase.ServerOption, CommandBase.OutputOption);

        return get;
    }
}
