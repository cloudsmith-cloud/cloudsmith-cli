using System.Net;
using System.Net.Http;
using System.Text.Json;
using Xunit;

namespace CloudSmith.Cli.Tests;

/// <summary>
/// AB#1530 — cs module list/install/uninstall/enable/disable — happy-path tests.
/// </summary>
public class ModuleTests
{
    // -------------------------------------------------------------------------
    // list — GET /api/v1/modules
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ModuleList_Returns_Modules()
    {
        var modules = new[]
        {
            new { id = "mod-monitoring", name = "Monitoring", version = "2.1.0", enabled = true },
            new { id = "mod-backup",     name = "Backup",     version = "1.0.0", enabled = false }
        };
        string json = JsonSerializer.Serialize(modules);

        using HttpClient http = new(new MockHttpMessageHandler(HttpStatusCode.OK, json))
        {
            BaseAddress = new Uri("http://test.local/")
        };

        HttpResponseMessage response = await http.GetAsync("api/v1/modules");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(body)!;
        Assert.Equal(2, result.Count);
        Assert.Equal("mod-monitoring", result[0]["id"].GetString());
        Assert.True(result[0]["enabled"].GetBoolean());
        Assert.False(result[1]["enabled"].GetBoolean());
    }

    // -------------------------------------------------------------------------
    // install — POST /api/v1/modules/install
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ModuleInstall_PostsIdAndVersion()
    {
        var installed = new { id = "mod-backup", name = "Backup", version = "1.5.0", enabled = true };
        string json = JsonSerializer.Serialize(installed);

        var handler = new CapturingHttpMessageHandler(HttpStatusCode.Created, json);
        using HttpClient http = new(handler) { BaseAddress = new Uri("http://test.local/") };

        using StringContent content = new(
            JsonSerializer.Serialize(new { id = "mod-backup", version = "1.5.0" }),
            System.Text.Encoding.UTF8, "application/json");

        HttpResponseMessage response = await http.PostAsync("api/v1/modules/install", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains("mod-backup", handler.CapturedBody);
        Assert.Contains("1.5.0",      handler.CapturedBody);
    }

    // -------------------------------------------------------------------------
    // install — no version specified
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ModuleInstall_WithoutVersion_PostsIdOnly()
    {
        var installed = new { id = "mod-monitoring", name = "Monitoring", version = "2.1.0", enabled = true };
        string json = JsonSerializer.Serialize(installed);

        var handler = new CapturingHttpMessageHandler(HttpStatusCode.Created, json);
        using HttpClient http = new(handler) { BaseAddress = new Uri("http://test.local/") };

        using StringContent content = new(
            JsonSerializer.Serialize(new { id = "mod-monitoring" }),
            System.Text.Encoding.UTF8, "application/json");

        HttpResponseMessage response = await http.PostAsync("api/v1/modules/install", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains("mod-monitoring", handler.CapturedBody);
    }

    // -------------------------------------------------------------------------
    // uninstall — DELETE /api/v1/modules/{id}
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ModuleUninstall_Sends_DeleteRequest()
    {
        var handler = new CapturingHttpMessageHandler(HttpStatusCode.NoContent, "");
        using HttpClient http = new(handler) { BaseAddress = new Uri("http://test.local/") };

        HttpResponseMessage response = await http.DeleteAsync("api/v1/modules/mod-backup");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpMethod.Delete, handler.CapturedMethod);
        Assert.Contains("mod-backup", handler.CapturedRequestUri);
    }

    // -------------------------------------------------------------------------
    // enable — PATCH /api/v1/modules/{id}/enable
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ModuleEnable_Sends_PatchRequest()
    {
        var handler = new CapturingHttpMessageHandler(HttpStatusCode.OK, "{\"enabled\":true}");
        using HttpClient http = new(handler) { BaseAddress = new Uri("http://test.local/") };

        HttpResponseMessage response = await http.PatchAsync(
            "api/v1/modules/mod-monitoring/enable",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpMethod.Patch, handler.CapturedMethod);
        Assert.Contains("mod-monitoring", handler.CapturedRequestUri);
        Assert.Contains("enable",         handler.CapturedRequestUri);
    }

    // -------------------------------------------------------------------------
    // disable — PATCH /api/v1/modules/{id}/disable
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ModuleDisable_Sends_PatchRequest()
    {
        var handler = new CapturingHttpMessageHandler(HttpStatusCode.OK, "{\"enabled\":false}");
        using HttpClient http = new(handler) { BaseAddress = new Uri("http://test.local/") };

        HttpResponseMessage response = await http.PatchAsync(
            "api/v1/modules/mod-backup/disable",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("disable", handler.CapturedRequestUri);
    }
}
