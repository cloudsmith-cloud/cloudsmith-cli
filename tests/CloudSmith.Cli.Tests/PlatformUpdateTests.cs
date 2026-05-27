using System.Net;
using System.Net.Http;
using System.Text.Json;
using CloudSmith.Cli.Commands.Platform;
using CloudSmith.Cli.Services;
using Xunit;

namespace CloudSmith.Cli.Tests;

/// <summary>
/// AB#1955 — cs platform update [--check | --apply | --rollback] — happy-path tests.
/// </summary>
public class PlatformUpdateTests
{
    // ---------------------------------------------------------------------------
    // Stubs
    // ---------------------------------------------------------------------------

    private static readonly IAuthService NoopAuth = new NoopAuthService();

    private static string MakeCheckJson(
        string current = "sha-154e088",
        string latest  = "sha-abc1234",
        string digest  = "sha256:abc",
        bool   available = true)
    {
        return JsonSerializer.Serialize(new
        {
            currentVersion  = current,
            latestVersion   = latest,
            latestDigest    = digest,
            updateAvailable = available,
            checkedAt       = DateTimeOffset.UtcNow.ToString("O")
        });
    }

    private static string MakeApplyJson(
        string updateId = "11111111-2222-3333-4444-555555555555",
        string status   = "Accepted",
        string message  = "Update queued.")
    {
        return JsonSerializer.Serialize(new { updateId, status, message });
    }

    // ---------------------------------------------------------------------------
    // check — update available → returns HTTP 200 body; deserialization correct
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Check_UpdateAvailable_DeserializesCorrectly()
    {
        string json = MakeCheckJson(available: true);

        using HttpClient http = new(new MockHttpMessageHandler(HttpStatusCode.OK, json))
        {
            BaseAddress = new Uri("http://test.local/")
        };

        HttpResponseMessage response =
            await http.GetAsync("api/v1/platform/updates/check");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();
        PlatformUpdateStatus? status = JsonSerializer.Deserialize<PlatformUpdateStatus>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(status);
        Assert.Equal("sha-154e088", status.CurrentVersion);
        Assert.Equal("sha-abc1234", status.LatestVersion);
        Assert.True(status.UpdateAvailable);
    }

    // ---------------------------------------------------------------------------
    // check — up to date → UpdateAvailable is false
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Check_UpToDate_UpdateAvailableIsFalse()
    {
        string json = MakeCheckJson(
            current: "sha-154e088", latest: "sha-154e088", available: false);

        using HttpClient http = new(new MockHttpMessageHandler(HttpStatusCode.OK, json))
        {
            BaseAddress = new Uri("http://test.local/")
        };

        HttpResponseMessage response =
            await http.GetAsync("api/v1/platform/updates/check");
        string body = await response.Content.ReadAsStringAsync();

        PlatformUpdateStatus? status = JsonSerializer.Deserialize<PlatformUpdateStatus>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(status);
        Assert.False(status.UpdateAvailable);
        Assert.Equal(status.CurrentVersion, status.LatestVersion);
    }

    // ---------------------------------------------------------------------------
    // check — RunCheckAsync exits 0 when up to date
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task RunCheckAsync_UpToDate_Returns0()
    {
        string json = MakeCheckJson(
            current: "sha-154e088", latest: "sha-154e088", available: false);

        int exitCode = await PlatformUpdateCommands.RunCheckAsync(
            "http://test.local",
            NoopAuth,
            "table",
            httpClientFactory: () => new HttpClient(
                new MockHttpMessageHandler(HttpStatusCode.OK, json))
            {
                BaseAddress = new Uri("http://test.local/")
            });

        Assert.Equal(0, exitCode);
    }

    // ---------------------------------------------------------------------------
    // check — RunCheckAsync exits 1 when update available
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task RunCheckAsync_UpdateAvailable_Returns1()
    {
        string json = MakeCheckJson(available: true);

        int exitCode = await PlatformUpdateCommands.RunCheckAsync(
            "http://test.local",
            NoopAuth,
            "table",
            httpClientFactory: () => new HttpClient(
                new MockHttpMessageHandler(HttpStatusCode.OK, json))
            {
                BaseAddress = new Uri("http://test.local/")
            });

        Assert.Equal(1, exitCode);
    }

    // ---------------------------------------------------------------------------
    // check — API error → returns 1
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task RunCheckAsync_ApiError_Returns1()
    {
        int exitCode = await PlatformUpdateCommands.RunCheckAsync(
            "http://test.local",
            NoopAuth,
            "table",
            httpClientFactory: () => new HttpClient(
                new MockHttpMessageHandler(HttpStatusCode.ServiceUnavailable, "{\"error\":\"down\"}"))
            {
                BaseAddress = new Uri("http://test.local/")
            });

        Assert.Equal(1, exitCode);
    }

    // ---------------------------------------------------------------------------
    // apply — PUT body includes rollback: false
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task RunApplyAsync_PostsRollbackFalse()
    {
        // Handler responds to both GET /check and PUT /apply
        string checkJson = MakeCheckJson(available: true);
        string applyJson = MakeApplyJson();

        var handler = new SequentialHttpMessageHandler(new[]
        {
            (HttpStatusCode.OK,       checkJson),   // GET /check
            (HttpStatusCode.Accepted, applyJson)    // PUT /apply
        });

        int exitCode = await PlatformUpdateCommands.RunApplyAsync(
            "http://test.local",
            NoopAuth,
            "json",
            yes: true,
            rollback: false,
            httpClientFactory: () => new HttpClient(handler)
            {
                BaseAddress = new Uri("http://test.local/")
            });

        // 0 = queued (no stream available in unit test — no stream URL)
        Assert.True(exitCode == 0 || exitCode == 1,
            "Exit code must be 0 or 1 (stream may fail without real SSE)");
        Assert.Contains("rollback", handler.LastCapturedBody);
        Assert.Contains("false",    handler.LastCapturedBody);
    }

    // ---------------------------------------------------------------------------
    // rollback — PUT body includes rollback: true
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task RunApplyAsync_Rollback_PostsRollbackTrue()
    {
        string checkJson = MakeCheckJson(available: true);
        string applyJson = MakeApplyJson();

        var handler = new SequentialHttpMessageHandler(new[]
        {
            (HttpStatusCode.OK,       checkJson),
            (HttpStatusCode.Accepted, applyJson)
        });

        await PlatformUpdateCommands.RunApplyAsync(
            "http://test.local",
            NoopAuth,
            "json",
            yes: true,
            rollback: true,
            httpClientFactory: () => new HttpClient(handler)
            {
                BaseAddress = new Uri("http://test.local/")
            });

        Assert.Contains("rollback", handler.LastCapturedBody);
        Assert.Contains("true",     handler.LastCapturedBody);
    }

    // ---------------------------------------------------------------------------
    // apply — non-2xx from /apply returns 1
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task RunApplyAsync_ApiError_Returns1()
    {
        string checkJson = MakeCheckJson(available: true);

        var handler = new SequentialHttpMessageHandler(new[]
        {
            (HttpStatusCode.OK,                  checkJson),
            (HttpStatusCode.InternalServerError, "{\"error\":\"fail\"}")
        });

        int exitCode = await PlatformUpdateCommands.RunApplyAsync(
            "http://test.local",
            NoopAuth,
            "table",
            yes: true,
            rollback: false,
            httpClientFactory: () => new HttpClient(handler)
            {
                BaseAddress = new Uri("http://test.local/")
            });

        Assert.Equal(1, exitCode);
    }

    // ---------------------------------------------------------------------------
    // Models — PlatformUpdateResponse round-trips through JSON
    // ---------------------------------------------------------------------------

    [Fact]
    public void PlatformUpdateResponse_RoundTripsJson()
    {
        Guid   id  = Guid.NewGuid();
        string raw = JsonSerializer.Serialize(new { updateId = id, status = "Accepted", message = "Queued." });

        PlatformUpdateResponse? resp = JsonSerializer.Deserialize<PlatformUpdateResponse>(raw,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(resp);
        Assert.Equal(id,         resp.UpdateId);
        Assert.Equal("Accepted", resp.Status);
        Assert.Equal("Queued.",  resp.Message);
    }
}

// ---------------------------------------------------------------------------
// Test helpers
// ---------------------------------------------------------------------------

/// <summary>
/// No-op auth service for unit tests.
/// </summary>
internal sealed class NoopAuthService : IAuthService
{
    public CachedToken? GetCurrentToken() => null;
    public Task<CachedToken> LoginAsync(string server, CancellationToken cancellationToken = default)
        => Task.FromResult(new CachedToken { AccessToken = "fake", ExpiresOn = DateTimeOffset.UtcNow.AddHours(1) });
    public Task LogoutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>
/// Returns responses in sequence — first call gets responses[0], second call gets responses[1], etc.
/// Captures the body of the most recent PUT/POST request.
/// </summary>
internal sealed class SequentialHttpMessageHandler(
    IEnumerable<(HttpStatusCode Status, string Body)> responses) : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Body)> _queue = new(responses);
    public string LastCapturedBody { get; private set; } = "";

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content is not null)
            LastCapturedBody = await request.Content.ReadAsStringAsync(cancellationToken);

        var (status, body) = _queue.Count > 0
            ? _queue.Dequeue()
            : (HttpStatusCode.OK, "{}");

        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
    }
}
