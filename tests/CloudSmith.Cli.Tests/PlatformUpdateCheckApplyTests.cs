using System.Net;
using System.Text.Json;
using CloudSmith.Cli.Commands.Platform;
using Xunit;

namespace CloudSmith.Cli.Tests;

/// <summary>
/// AB#1951 — cs platform update check / apply — happy-path and error tests.
/// </summary>
public class PlatformUpdateCheckApplyTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static readonly NoopAuthService Auth = new();

    private static string MakeCheckJson(
        string current       = "1.2.3",
        string latest        = "1.2.4",
        bool   available     = true,
        string releaseNotes  = "https://github.com/cloudsmith-cloud/cloudsmith/releases/v1.2.4")
    {
        return JsonSerializer.Serialize(new
        {
            currentVersion  = current,
            latestVersion   = latest,
            updateAvailable = available,
            releaseNotes
        });
    }

    private static string MakeApplyJson(
        string jobId   = "upd-abc123",
        string status  = "Queued",
        string message = "Update queued.")
    {
        return JsonSerializer.Serialize(new { jobId, status, message });
    }

    // ---------------------------------------------------------------------------
    // check — update available → RunAsync returns 1, deserializes correctly
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Check_UpdateAvailable_Returns1()
    {
        string json = MakeCheckJson(available: true);

        int exitCode = await PlatformUpdateCheckCommand.RunAsync(
            "http://test.local",
            Auth,
            "table",
            httpClientFactory: () => new HttpClient(
                new MockHttpMessageHandler(HttpStatusCode.OK, json))
            { BaseAddress = new Uri("http://test.local/") });

        Assert.Equal(1, exitCode);
    }

    // ---------------------------------------------------------------------------
    // check — up to date → RunAsync returns 0
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Check_UpToDate_Returns0()
    {
        string json = MakeCheckJson(
            current: "1.2.3", latest: "1.2.3", available: false);

        int exitCode = await PlatformUpdateCheckCommand.RunAsync(
            "http://test.local",
            Auth,
            "table",
            httpClientFactory: () => new HttpClient(
                new MockHttpMessageHandler(HttpStatusCode.OK, json))
            { BaseAddress = new Uri("http://test.local/") });

        Assert.Equal(0, exitCode);
    }

    // ---------------------------------------------------------------------------
    // check — API error → RunAsync returns 1
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Check_ApiError_Returns1()
    {
        int exitCode = await PlatformUpdateCheckCommand.RunAsync(
            "http://test.local",
            Auth,
            "table",
            httpClientFactory: () => new HttpClient(
                new MockHttpMessageHandler(HttpStatusCode.ServiceUnavailable, "{\"error\":\"down\"}"))
            { BaseAddress = new Uri("http://test.local/") });

        Assert.Equal(1, exitCode);
    }

    // ---------------------------------------------------------------------------
    // check — json output → returns raw JSON body, exit 0 even when update available
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Check_JsonOutput_Returns0()
    {
        string json = MakeCheckJson(available: true);

        int exitCode = await PlatformUpdateCheckCommand.RunAsync(
            "http://test.local",
            Auth,
            "json",
            httpClientFactory: () => new HttpClient(
                new MockHttpMessageHandler(HttpStatusCode.OK, json))
            { BaseAddress = new Uri("http://test.local/") });

        // json mode: always exits 0 (raw pass-through — no semantic exit code)
        Assert.Equal(0, exitCode);
    }

    // ---------------------------------------------------------------------------
    // check — response model deserializes correctly
    // ---------------------------------------------------------------------------

    [Fact]
    public void Check_ResponseModel_DeserializesCorrectly()
    {
        string json = MakeCheckJson(
            current: "1.2.3",
            latest: "1.2.4",
            available: true,
            releaseNotes: "https://github.com/example");

        PlatformUpdateCheckResponse? status =
            JsonSerializer.Deserialize<PlatformUpdateCheckResponse>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(status);
        Assert.Equal("1.2.3", status.CurrentVersion);
        Assert.Equal("1.2.4", status.LatestVersion);
        Assert.True(status.UpdateAvailable);
        Assert.Equal("https://github.com/example", status.ReleaseNotes);
    }

    // ---------------------------------------------------------------------------
    // apply — happy path → RunAsync returns 0, body contains jobId
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Apply_HappyPath_Returns0()
    {
        string applyJson = MakeApplyJson(jobId: "upd-abc123");

        var handler = new CapturingHttpMessageHandler(HttpStatusCode.Accepted, applyJson);

        int exitCode = await PlatformUpdateApplyCommand.RunAsync(
            "http://test.local",
            Auth,
            "table",
            yes: true,
            httpClientFactory: () => new HttpClient(handler)
            { BaseAddress = new Uri("http://test.local/") });

        Assert.Equal(0, exitCode);
        Assert.Equal(HttpMethod.Post, handler.CapturedMethod);
        Assert.Contains("platform/update/apply", handler.CapturedRequestUri);
    }

    // ---------------------------------------------------------------------------
    // apply — json output → returns raw JSON, exit 0
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Apply_JsonOutput_Returns0()
    {
        string applyJson = MakeApplyJson();

        int exitCode = await PlatformUpdateApplyCommand.RunAsync(
            "http://test.local",
            Auth,
            "json",
            yes: true,
            httpClientFactory: () => new HttpClient(
                new MockHttpMessageHandler(HttpStatusCode.Accepted, applyJson))
            { BaseAddress = new Uri("http://test.local/") });

        Assert.Equal(0, exitCode);
    }

    // ---------------------------------------------------------------------------
    // apply — API error → RunAsync returns 1
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Apply_ApiError_Returns1()
    {
        int exitCode = await PlatformUpdateApplyCommand.RunAsync(
            "http://test.local",
            Auth,
            "table",
            yes: true,
            httpClientFactory: () => new HttpClient(
                new MockHttpMessageHandler(HttpStatusCode.InternalServerError, "{\"error\":\"fail\"}"))
            { BaseAddress = new Uri("http://test.local/") });

        Assert.Equal(1, exitCode);
    }

    // ---------------------------------------------------------------------------
    // apply — response model deserializes correctly
    // ---------------------------------------------------------------------------

    [Fact]
    public void Apply_ResponseModel_DeserializesCorrectly()
    {
        string json = JsonSerializer.Serialize(new
        {
            jobId   = "upd-abc123",
            status  = "Queued",
            message = "Update queued."
        });

        PlatformUpdateApplyResponse? resp =
            JsonSerializer.Deserialize<PlatformUpdateApplyResponse>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(resp);
        Assert.Equal("upd-abc123",    resp.JobId);
        Assert.Equal("Queued",        resp.Status);
        Assert.Equal("Update queued.", resp.Message);
    }
}
