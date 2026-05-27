using System.CommandLine;
using System.Text;
using System.Text.Json;
using CloudSmith.Cli.Output;
using CloudSmith.Cli.Services;

namespace CloudSmith.Cli.Commands;

public static class DeployCommands
{
    public static Command Build(IConfigService configService, IAuthService authService)
    {
        Command deploy = new("deploy", "Manage deployments.");

        deploy.AddCommand(BuildPlan(configService, authService));
        deploy.AddCommand(BuildApply(configService, authService));
        deploy.AddCommand(BuildStatus(configService, authService));

        return deploy;
    }

    // -------------------------------------------------------------------------
    // cs deploy plan --cluster <name>
    // -------------------------------------------------------------------------
    private static Command BuildPlan(IConfigService configService, IAuthService authService)
    {
        Command plan = new("plan", "Create a deployment plan for a cluster.");

        Option<string> clusterOpt = new("--cluster", "Target cluster name.") { IsRequired = true };
        plan.AddOption(clusterOpt);

        plan.SetHandler(async (string cluster, string? serverOverride, string output) =>
        {
            string server = serverOverride ?? configService.Get("server") ?? "https://localhost";
            using System.Net.Http.HttpClient http = ClusterCommands.MakeClient(server, authService);

            try
            {
                var payload = new { cluster };
                string json = JsonSerializer.Serialize(payload);
                using StringContent content = new(json, Encoding.UTF8, "application/json");

                System.Net.Http.HttpResponseMessage response =
                    await http.PostAsync("api/v1/deploy/v1/plan", content);
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

                var plan = JsonSerializer.Deserialize<DeployPlanDto>(body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                var table = new Spectre.Console.Table()
                    .AddColumn("Action")
                    .AddColumn("Resource")
                    .AddColumn("Detail");

                if (plan?.Changes is not null)
                {
                    foreach (var change in plan.Changes)
                        table.AddRow(
                            Spectre.Console.Markup.Escape(change.Action   ?? ""),
                            Spectre.Console.Markup.Escape(change.Resource ?? ""),
                            Spectre.Console.Markup.Escape(change.Detail   ?? ""));
                }

                Spectre.Console.AnsiConsole.Write(table);

                if (plan?.PlanId is not null)
                    OutputWriter.WriteMessage(output, $"Plan ID: {plan.PlanId}");
            }
            catch (Exception ex)
            {
                OutputWriter.WriteError($"Request failed: {ex.Message}");
                Environment.Exit(1);
            }
        }, clusterOpt, CommandBase.ServerOption, CommandBase.OutputOption);

        return plan;
    }

    // -------------------------------------------------------------------------
    // cs deploy apply --cluster <name> [--yes]
    // -------------------------------------------------------------------------
    private static Command BuildApply(IConfigService configService, IAuthService authService)
    {
        Command apply = new("apply", "Execute a deployment plan.");

        Option<string> clusterOpt = new("--cluster", "Target cluster name.") { IsRequired = true };
        Option<bool>   yesOpt     = new("--yes", "Skip confirmation prompt.");

        apply.AddOption(clusterOpt);
        apply.AddOption(yesOpt);

        apply.SetHandler(async (string cluster, bool yes, string? serverOverride, string output) =>
        {
            if (!yes)
            {
                Console.Write($"Apply deployment to cluster '{cluster}'? [y/N] ");
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
                var payload = new { cluster };
                string json = JsonSerializer.Serialize(payload);
                using StringContent content = new(json, Encoding.UTF8, "application/json");

                System.Net.Http.HttpResponseMessage response =
                    await http.PostAsync("api/v1/deploy/v1/apply", content);
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

                var result = JsonSerializer.Deserialize<DeployApplyDto>(body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                OutputWriter.WriteTable(output, [
                    ("JobId",   result?.JobId   ?? ""),
                    ("Status",  result?.Status  ?? ""),
                    ("Cluster", cluster)
                ], "Field", "Value");
            }
            catch (Exception ex)
            {
                OutputWriter.WriteError($"Request failed: {ex.Message}");
                Environment.Exit(1);
            }
        }, clusterOpt, yesOpt, CommandBase.ServerOption, CommandBase.OutputOption);

        return apply;
    }

    // -------------------------------------------------------------------------
    // cs deploy status --job <id>
    // -------------------------------------------------------------------------
    private static Command BuildStatus(IConfigService configService, IAuthService authService)
    {
        Command status = new("status", "Poll the status of a deployment job.");

        Option<string> jobOpt = new("--job", "Job ID.") { IsRequired = true };
        status.AddOption(jobOpt);

        status.SetHandler(async (string job, string? serverOverride, string output) =>
        {
            string server = serverOverride ?? configService.Get("server") ?? "https://localhost";
            using System.Net.Http.HttpClient http = ClusterCommands.MakeClient(server, authService);

            try
            {
                System.Net.Http.HttpResponseMessage response =
                    await http.GetAsync($"api/v1/deploy/v1/jobs/{Uri.EscapeDataString(job)}");
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

                var jobDto = JsonSerializer.Deserialize<DeployJobDto>(body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                OutputWriter.WriteTable(output, [
                    ("JobId",     jobDto?.JobId     ?? ""),
                    ("Status",    jobDto?.Status    ?? ""),
                    ("Cluster",   jobDto?.Cluster   ?? ""),
                    ("StartedAt", jobDto?.StartedAt ?? ""),
                    ("UpdatedAt", jobDto?.UpdatedAt ?? "")
                ], "Field", "Value");
            }
            catch (Exception ex)
            {
                OutputWriter.WriteError($"Request failed: {ex.Message}");
                Environment.Exit(1);
            }
        }, jobOpt, CommandBase.ServerOption, CommandBase.OutputOption);

        return status;
    }
}

// ---------------------------------------------------------------------------
// DTOs
// ---------------------------------------------------------------------------

internal sealed class DeployPlanDto
{
    public string?            PlanId  { get; set; }
    public List<PlanChange>?  Changes { get; set; }
}

internal sealed class PlanChange
{
    public string? Action   { get; set; }
    public string? Resource { get; set; }
    public string? Detail   { get; set; }
}

internal sealed class DeployApplyDto
{
    public string? JobId   { get; set; }
    public string? Status  { get; set; }
    public string? Cluster { get; set; }
}

internal sealed class DeployJobDto
{
    public string? JobId     { get; set; }
    public string? Status    { get; set; }
    public string? Cluster   { get; set; }
    public string? StartedAt { get; set; }
    public string? UpdatedAt { get; set; }
}
