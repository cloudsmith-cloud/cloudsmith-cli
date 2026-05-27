using System.Net;
using System.Net.Http;
using System.Text.Json;
using Xunit;

namespace CloudSmith.Cli.Tests;

/// <summary>
/// AB#1531 — cs agent list/register/remove — happy-path tests.
/// </summary>
public class AgentTests
{
    // -------------------------------------------------------------------------
    // list — GET /api/v1/agents
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AgentList_Returns_Agents()
    {
        var agents = new[]
        {
            new { id = "agent-001", hostname = "node-01", status = "Online",  lastSeen = "2026-05-27T10:00:00Z" },
            new { id = "agent-002", hostname = "node-02", status = "Offline", lastSeen = "2026-05-26T08:00:00Z" }
        };
        string json = JsonSerializer.Serialize(agents);

        using HttpClient http = new(new MockHttpMessageHandler(HttpStatusCode.OK, json))
        {
            BaseAddress = new Uri("http://test.local/")
        };

        HttpResponseMessage response = await http.GetAsync("api/v1/agents");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(body)!;
        Assert.Equal(2, result.Count);
        Assert.Equal("agent-001", result[0]["id"].GetString());
        Assert.Equal("Online",    result[0]["status"].GetString());
    }

    // -------------------------------------------------------------------------
    // register — POST /api/v1/agents
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AgentRegister_PostsHostnameAndToken()
    {
        var created = new { id = "agent-003", hostname = "node-03", status = "Online", lastSeen = "" };
        string json = JsonSerializer.Serialize(created);

        var handler = new CapturingHttpMessageHandler(HttpStatusCode.Created, json);
        using HttpClient http = new(handler) { BaseAddress = new Uri("http://test.local/") };

        using StringContent content = new(
            JsonSerializer.Serialize(new { hostname = "node-03", token = "reg-token-xyz" }),
            System.Text.Encoding.UTF8, "application/json");

        HttpResponseMessage response = await http.PostAsync("api/v1/agents", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains("node-03",      handler.CapturedBody);
        Assert.Contains("reg-token-xyz", handler.CapturedBody);
    }

    // -------------------------------------------------------------------------
    // register — response contains agent ID
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AgentRegister_ResponseContainsId()
    {
        var created = new { id = "agent-003", hostname = "node-03", status = "Online", lastSeen = "" };
        string json = JsonSerializer.Serialize(created);

        using HttpClient http = new(new MockHttpMessageHandler(HttpStatusCode.Created, json))
        {
            BaseAddress = new Uri("http://test.local/")
        };

        using StringContent content = new(
            JsonSerializer.Serialize(new { hostname = "node-03", token = "reg-token-xyz" }),
            System.Text.Encoding.UTF8, "application/json");

        HttpResponseMessage response = await http.PostAsync("api/v1/agents", content);
        string body = await response.Content.ReadAsStringAsync();

        var result = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body)!;
        Assert.Equal("agent-003", result["id"].GetString());
    }

    // -------------------------------------------------------------------------
    // remove — DELETE /api/v1/agents/{id}
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AgentRemove_Sends_DeleteRequest()
    {
        var handler = new CapturingHttpMessageHandler(HttpStatusCode.NoContent, "");
        using HttpClient http = new(handler) { BaseAddress = new Uri("http://test.local/") };

        HttpResponseMessage response = await http.DeleteAsync("api/v1/agents/agent-001");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpMethod.Delete, handler.CapturedMethod);
        Assert.Contains("agent-001", handler.CapturedRequestUri);
    }

    // -------------------------------------------------------------------------
    // Error path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AgentRemove_NotFound_Returns404()
    {
        using HttpClient http = new(new MockHttpMessageHandler(HttpStatusCode.NotFound, "{\"error\":\"Agent not found\"}"))
        {
            BaseAddress = new Uri("http://test.local/")
        };

        HttpResponseMessage response = await http.DeleteAsync("api/v1/agents/nonexistent");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
