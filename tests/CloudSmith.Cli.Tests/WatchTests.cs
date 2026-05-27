using System.Net;
using System.Net.Http;
using System.Text;
using CloudSmith.Cli.Commands;
using Xunit;

namespace CloudSmith.Cli.Tests;

/// <summary>
/// AB#1532 — cs watch --job &lt;id&gt; — verifies SSE stream parsing and exit-code logic.
/// </summary>
public class WatchTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a raw SSE stream body from a sequence of event data payloads.
    /// Each SSE event is: "data: {payload}\n\n"
    /// </summary>
    private static string BuildSseBody(params string[] events)
    {
        var sb = new StringBuilder();
        foreach (string evt in events)
        {
            sb.Append("data: ");
            sb.Append(evt);
            sb.Append("\n\n");
        }
        return sb.ToString();
    }

    private static Func<HttpClient> SseClientFactory(string sseBody)
        => () => new HttpClient(new SseHttpMessageHandler(sseBody))
        {
            Timeout = Timeout.InfiniteTimeSpan,
            BaseAddress = new Uri("http://fake.local/")
        };

    // -------------------------------------------------------------------------
    // Completed job → exit code 0
    // -------------------------------------------------------------------------

    [Fact]
    public async Task StreamJob_Completed_Returns0()
    {
        string sseBody = BuildSseBody(
            "{\"status\":\"Running\",\"message\":\"Step 1/3 starting...\"}",
            "{\"status\":\"Running\",\"message\":\"Step 2/3 done\"}",
            "{\"status\":\"Completed\",\"message\":\"Job finished successfully\"}");

        var origOut = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);

        int exit = await WatchCommand.StreamJobAsync(
            server: "http://fake.local",
            jobId: "job-001",
            accessToken: null,
            cancellationToken: default,
            httpClientFactory: SseClientFactory(sseBody));

        Console.SetOut(origOut);

        Assert.Equal(0, exit);
        string output = sw.ToString();
        Assert.Contains("Step 1/3 starting", output);
        Assert.Contains("Job finished successfully", output);
    }

    // -------------------------------------------------------------------------
    // Failed job → exit code 1
    // -------------------------------------------------------------------------

    [Fact]
    public async Task StreamJob_Failed_Returns1()
    {
        string sseBody = BuildSseBody(
            "{\"status\":\"Running\",\"message\":\"Deploying...\"}",
            "{\"status\":\"Failed\",\"message\":\"Timeout waiting for node\"}");

        var origOut = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);

        int exit = await WatchCommand.StreamJobAsync(
            server: "http://fake.local",
            jobId: "job-002",
            accessToken: null,
            httpClientFactory: SseClientFactory(sseBody));

        Console.SetOut(origOut);

        Assert.Equal(1, exit);
        string output = sw.ToString();
        Assert.Contains("Timeout waiting for node", output);
    }

    // -------------------------------------------------------------------------
    // Cancelled job → exit code 1
    // -------------------------------------------------------------------------

    [Fact]
    public async Task StreamJob_Cancelled_Returns1()
    {
        string sseBody = BuildSseBody(
            "{\"status\":\"Running\",\"message\":\"In progress\"}",
            "{\"status\":\"Cancelled\",\"message\":\"Cancelled by user\"}");

        var origOut = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);

        int exit = await WatchCommand.StreamJobAsync(
            server: "http://fake.local",
            jobId: "job-003",
            accessToken: null,
            httpClientFactory: SseClientFactory(sseBody));

        Console.SetOut(origOut);

        Assert.Equal(1, exit);
    }

    // -------------------------------------------------------------------------
    // Plain text (non-JSON) SSE events are output as raw lines
    // -------------------------------------------------------------------------

    [Fact]
    public async Task StreamJob_PlainTextEvents_AreOutput()
    {
        string sseBody = BuildSseBody(
            "Starting deployment",
            "Node 1 ready",
            "{\"status\":\"Completed\",\"message\":\"Done\"}");

        var origOut = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);

        int exit = await WatchCommand.StreamJobAsync(
            server: "http://fake.local",
            jobId: "job-004",
            accessToken: null,
            httpClientFactory: SseClientFactory(sseBody));

        Console.SetOut(origOut);

        Assert.Equal(0, exit);
        string output = sw.ToString();
        Assert.Contains("Starting deployment", output);
        Assert.Contains("Node 1 ready",        output);
    }

    // -------------------------------------------------------------------------
    // HTTP error (non-2xx) → exit code 1
    // -------------------------------------------------------------------------

    [Fact]
    public async Task StreamJob_HttpError_Returns1()
    {
        Func<HttpClient> factory = () => new HttpClient(
            new MockHttpMessageHandler(HttpStatusCode.NotFound, "{\"error\":\"Not found\"}"))
        {
            Timeout = Timeout.InfiniteTimeSpan,
            BaseAddress = new Uri("http://fake.local/")
        };

        // Capture stdout (WriteError uses AnsiConsole which writes to stdout in test environments)
        var origOut = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);

        int exit = await WatchCommand.StreamJobAsync(
            server: "http://fake.local",
            jobId: "nonexistent",
            accessToken: null,
            httpClientFactory: factory);

        Console.SetOut(origOut);

        Assert.Equal(1, exit);
    }

    // -------------------------------------------------------------------------
    // Timestamp prefix is present in output
    // -------------------------------------------------------------------------

    [Fact]
    public async Task StreamJob_OutputLines_HaveTimestampPrefix()
    {
        string sseBody = BuildSseBody(
            "{\"status\":\"Completed\",\"message\":\"Quick job\"}");

        var origOut = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);

        await WatchCommand.StreamJobAsync(
            server: "http://fake.local",
            jobId: "job-005",
            accessToken: null,
            httpClientFactory: SseClientFactory(sseBody));

        Console.SetOut(origOut);

        string output = sw.ToString().Trim();
        // Timestamp format: [HH:mm:ss]
        Assert.Matches(@"^\[\d{2}:\d{2}:\d{2}\]", output);
    }
}

// ---------------------------------------------------------------------------
// SSE-capable mock handler — returns a streaming SSE response
// ---------------------------------------------------------------------------

internal sealed class SseHttpMessageHandler(string sseBody) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(sseBody);
        var stream  = new MemoryStream(bytes);
        var content = new StreamContent(stream);
        content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");

        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        return Task.FromResult(response);
    }
}
