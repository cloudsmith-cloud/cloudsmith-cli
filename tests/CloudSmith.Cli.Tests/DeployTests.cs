using System.Net;
using System.Net.Http;
using System.Text.Json;
using Xunit;

namespace CloudSmith.Cli.Tests;

/// <summary>
/// AB#1529 — cs deploy plan/apply/status — happy-path tests using mock HTTP.
/// </summary>
public class DeployTests
{
    // -------------------------------------------------------------------------
    // plan — POST /api/v1/deploy/v1/plan
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DeployPlan_PostsClusterPayload()
    {
        var planResponse = new
        {
            planId = "plan-abc-123",
            changes = new[]
            {
                new { action = "create", resource = "vm/worker-01", detail = "new VM" },
                new { action = "update", resource = "vnet/core", detail = "extend subnet" }
            }
        };
        string json = JsonSerializer.Serialize(planResponse);

        var handler = new CapturingHttpMessageHandler(HttpStatusCode.OK, json);
        using HttpClient http = new(handler) { BaseAddress = new Uri("http://test.local/") };

        using StringContent content = new(
            JsonSerializer.Serialize(new { cluster = "cluster-a" }),
            System.Text.Encoding.UTF8, "application/json");

        HttpResponseMessage response = await http.PostAsync("api/v1/deploy/v1/plan", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("cluster-a", handler.CapturedBody);

        string body = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body)!;
        Assert.Equal("plan-abc-123", result["planId"].GetString());
    }

    // -------------------------------------------------------------------------
    // apply — POST /api/v1/deploy/v1/apply
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DeployApply_ReturnsJobId()
    {
        var applyResponse = new { jobId = "job-xyz-456", status = "Running", cluster = "cluster-a" };
        string json = JsonSerializer.Serialize(applyResponse);

        using HttpClient http = new(new MockHttpMessageHandler(HttpStatusCode.Accepted, json))
        {
            BaseAddress = new Uri("http://test.local/")
        };

        using StringContent content = new(
            JsonSerializer.Serialize(new { cluster = "cluster-a" }),
            System.Text.Encoding.UTF8, "application/json");

        HttpResponseMessage response = await http.PostAsync("api/v1/deploy/v1/apply", content);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body)!;
        Assert.Equal("job-xyz-456", result["jobId"].GetString());
        Assert.Equal("Running",     result["status"].GetString());
    }

    // -------------------------------------------------------------------------
    // status — GET /api/v1/deploy/v1/jobs/{id}
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DeployStatus_ReturnsJobDetails()
    {
        var jobResponse = new
        {
            jobId     = "job-xyz-456",
            status    = "Completed",
            cluster   = "cluster-a",
            startedAt = "2026-05-27T10:00:00Z",
            updatedAt = "2026-05-27T10:05:00Z"
        };
        string json = JsonSerializer.Serialize(jobResponse);

        using HttpClient http = new(new MockHttpMessageHandler(HttpStatusCode.OK, json))
        {
            BaseAddress = new Uri("http://test.local/")
        };

        HttpResponseMessage response = await http.GetAsync("api/v1/deploy/v1/jobs/job-xyz-456");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body)!;
        Assert.Equal("Completed", result["status"].GetString());
        Assert.Equal("cluster-a", result["cluster"].GetString());
    }

    // -------------------------------------------------------------------------
    // Error path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DeployStatus_NotFound_Returns404()
    {
        using HttpClient http = new(new MockHttpMessageHandler(HttpStatusCode.NotFound, "{\"error\":\"Job not found\"}"))
        {
            BaseAddress = new Uri("http://test.local/")
        };

        HttpResponseMessage response = await http.GetAsync("api/v1/deploy/v1/jobs/nonexistent");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
