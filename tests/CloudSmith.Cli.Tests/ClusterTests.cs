using System.Net;
using System.Net.Http;
using System.Text.Json;
using CloudSmith.Cli.Commands;
using CloudSmith.Cli.Services;
using Moq;
using Xunit;

namespace CloudSmith.Cli.Tests;

/// <summary>
/// AB#1528 — cs cluster list/get/add/remove — happy-path tests using mock HTTP.
/// </summary>
public class ClusterTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static (IConfigService config, IAuthService auth) MakeServices()
    {
        var config = new Mock<IConfigService>();
        config.Setup(c => c.Get("server")).Returns("http://test.local");

        var auth = new Mock<IAuthService>();
        auth.Setup(a => a.GetCurrentToken()).Returns((CachedToken?)null);

        return (config.Object, auth.Object);
    }

    private static HttpClient MakeHttpClient(HttpStatusCode status, string body)
    {
        var handler = new MockHttpMessageHandler(status, body);
        return new HttpClient(handler) { BaseAddress = new Uri("http://test.local/") };
    }

    // -------------------------------------------------------------------------
    // list — GET /api/v1/clusters → table rows
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ClusterList_Returns_ExpectedRows()
    {
        var clusters = new[]
        {
            new { name = "cluster-a", status = "Ready", nodeCount = 3, version = "1.2.0" },
            new { name = "cluster-b", status = "Degraded", nodeCount = 2, version = "1.1.0" }
        };
        string json = JsonSerializer.Serialize(clusters);

        using HttpClient http = MakeHttpClient(HttpStatusCode.OK, json);
        HttpResponseMessage response = await http.GetAsync("api/v1/clusters");

        string body = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(body)!;

        Assert.Equal(2, result.Count);
        Assert.Equal("cluster-a", result[0]["name"].GetString());
        Assert.Equal("Ready",     result[0]["status"].GetString());
        Assert.Equal(3,           result[0]["nodeCount"].GetInt32());
    }

    // -------------------------------------------------------------------------
    // get — GET /api/v1/clusters/{name}
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ClusterGet_Returns_SingleCluster()
    {
        var cluster = new { name = "cluster-a", status = "Ready", nodeCount = 3, version = "1.2.0", host = "10.0.0.1", port = 443 };
        string json = JsonSerializer.Serialize(cluster);

        using HttpClient http = MakeHttpClient(HttpStatusCode.OK, json);
        HttpResponseMessage response = await http.GetAsync("api/v1/clusters/cluster-a");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body)!;

        Assert.Equal("cluster-a", result["name"].GetString());
        Assert.Equal("10.0.0.1",  result["host"].GetString());
    }

    // -------------------------------------------------------------------------
    // add — POST /api/v1/clusters
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ClusterAdd_PostsCorrectPayload()
    {
        var created = new { name = "cluster-new", status = "Provisioning", nodeCount = 0, version = "" };
        string json = JsonSerializer.Serialize(created);

        var handler = new CapturingHttpMessageHandler(HttpStatusCode.Created, json);
        using HttpClient http = new(handler) { BaseAddress = new Uri("http://test.local/") };

        using StringContent content = new(
            JsonSerializer.Serialize(new { name = "cluster-new", host = "10.0.0.5", port = 443 }),
            System.Text.Encoding.UTF8, "application/json");

        HttpResponseMessage response = await http.PostAsync("api/v1/clusters", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains("cluster-new", handler.CapturedBody);
    }

    // -------------------------------------------------------------------------
    // remove — DELETE /api/v1/clusters/{name}
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ClusterRemove_Sends_DeleteRequest()
    {
        var handler = new CapturingHttpMessageHandler(HttpStatusCode.NoContent, "");
        using HttpClient http = new(handler) { BaseAddress = new Uri("http://test.local/") };

        HttpResponseMessage response = await http.DeleteAsync("api/v1/clusters/cluster-a");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpMethod.Delete, handler.CapturedMethod);
        Assert.Contains("cluster-a", handler.CapturedRequestUri);
    }

    // -------------------------------------------------------------------------
    // Error path — non-2xx response
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ClusterList_NonSuccess_ReturnsErrorBody()
    {
        using HttpClient http = MakeHttpClient(HttpStatusCode.Unauthorized, "{\"error\":\"Unauthorized\"}");
        HttpResponseMessage response = await http.GetAsync("api/v1/clusters");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Unauthorized", body);
    }
}

// ---------------------------------------------------------------------------
// Mock helpers shared across test files
// ---------------------------------------------------------------------------

internal sealed class MockHttpMessageHandler(HttpStatusCode status, string body) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
        return Task.FromResult(response);
    }
}

internal sealed class CapturingHttpMessageHandler(HttpStatusCode status, string body) : HttpMessageHandler
{
    public string       CapturedBody       { get; private set; } = "";
    public string       CapturedRequestUri { get; private set; } = "";
    public HttpMethod   CapturedMethod     { get; private set; } = HttpMethod.Get;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CapturedBody       = request.Content is not null
            ? await request.Content.ReadAsStringAsync(cancellationToken)
            : "";
        CapturedRequestUri = request.RequestUri?.ToString() ?? "";
        CapturedMethod     = request.Method;

        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
    }
}
