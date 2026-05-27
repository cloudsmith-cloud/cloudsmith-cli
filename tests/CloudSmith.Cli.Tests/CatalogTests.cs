using System.Net;
using System.Net.Http;
using System.Text.Json;
using CloudSmith.Cli.Commands.Module;
using Xunit;

namespace CloudSmith.Cli.Tests;

/// <summary>
/// AB#1925 — cs module catalog list / catalog get / module install --from-catalog
/// Happy-path HTTP layer tests (no CLI invocation needed — exercises deserialization
/// and HTTP routing directly, matching the pattern used by ModuleTests and ClusterTests).
/// </summary>
public class CatalogTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static string MakeCatalogJson(IEnumerable<object> items, int? total = null)
    {
        var response = new
        {
            items,
            totalCount = total ?? items.Count(),
            fetchedAt  = DateTimeOffset.UtcNow.ToString("O")
        };
        return JsonSerializer.Serialize(response);
    }

    private static object MakeEntry(
        string  id          = "cloudsmith-monitoring",
        string  name        = "Monitoring",
        string  version     = "1.0.0",
        string  description = "CloudSmith monitoring module",
        string  publisher   = "CloudSmith",
        bool    isVerified  = true,
        bool    isInstalled = false,
        bool    isEnabled   = false) => new
    {
        id,
        name,
        version,
        description,
        publisher,
        ghcrImageRef = $"ghcr.io/cloudsmith-cloud/{id}:{version}",
        manifestUrl  = $"https://modules.cloudsmith.cloud/{id}/{version}/manifest.json",
        signatureRef = (string?)null,
        isVerified,
        isInstalled,
        isEnabled
    };

    // ---------------------------------------------------------------------------
    // catalog list — GET /api/v1/modules/catalog returns a CatalogResponse
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CatalogList_Deserializes_Items()
    {
        var entries = new[] { MakeEntry(), MakeEntry("cloudsmith-backup", "Backup", "2.0.0") };
        string json = MakeCatalogJson(entries);

        using HttpClient http = new(new MockHttpMessageHandler(HttpStatusCode.OK, json))
        {
            BaseAddress = new Uri("http://test.local/")
        };

        HttpResponseMessage response = await http.GetAsync("api/v1/modules/catalog");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();
        CatalogResponse? catalog = JsonSerializer.Deserialize<CatalogResponse>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(catalog);
        Assert.Equal(2,    catalog.Items.Count);
        Assert.Equal("cloudsmith-monitoring", catalog.Items[0].Id);
        Assert.Equal("cloudsmith-backup",     catalog.Items[1].Id);
        Assert.Equal(2,    catalog.TotalCount);
    }

    // ---------------------------------------------------------------------------
    // catalog list — verified flag on a single verified entry
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CatalogList_VerifiedEntry_IsMarked()
    {
        var entries = new[] { MakeEntry(isVerified: true) };
        string json = MakeCatalogJson(entries);

        using HttpClient http = new(new MockHttpMessageHandler(HttpStatusCode.OK, json))
        {
            BaseAddress = new Uri("http://test.local/")
        };

        HttpResponseMessage response = await http.GetAsync("api/v1/modules/catalog");
        string body = await response.Content.ReadAsStringAsync();

        CatalogResponse? catalog = JsonSerializer.Deserialize<CatalogResponse>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.True(catalog!.Items[0].IsVerified);
    }

    // ---------------------------------------------------------------------------
    // catalog list — installed flag
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CatalogList_InstalledEntry_IsMarked()
    {
        var entries = new[]
        {
            MakeEntry("cloudsmith-monitoring", isInstalled: true),
            MakeEntry("cloudsmith-backup",     isInstalled: false)
        };
        string json = MakeCatalogJson(entries);

        using HttpClient http = new(new MockHttpMessageHandler(HttpStatusCode.OK, json))
        {
            BaseAddress = new Uri("http://test.local/")
        };

        HttpResponseMessage response = await http.GetAsync("api/v1/modules/catalog");
        string body = await response.Content.ReadAsStringAsync();

        CatalogResponse? catalog = JsonSerializer.Deserialize<CatalogResponse>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        var installed = catalog!.Items.Where(e => e.IsInstalled).ToList();
        Assert.Single(installed);
        Assert.Equal("cloudsmith-monitoring", installed[0].Id);
    }

    // ---------------------------------------------------------------------------
    // catalog get — find entry by id (case-insensitive)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CatalogGet_FindsEntryById_CaseInsensitive()
    {
        var entries = new[]
        {
            MakeEntry("cloudsmith-monitoring"),
            MakeEntry("cloudsmith-backup")
        };
        string json = MakeCatalogJson(entries);

        using HttpClient http = new(new MockHttpMessageHandler(HttpStatusCode.OK, json))
        {
            BaseAddress = new Uri("http://test.local/")
        };

        HttpResponseMessage response = await http.GetAsync("api/v1/modules/catalog");
        string body = await response.Content.ReadAsStringAsync();

        CatalogResponse? catalog = JsonSerializer.Deserialize<CatalogResponse>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        // Simulate case-insensitive lookup used by "catalog get"
        CatalogEntry? entry = catalog?.Items.FirstOrDefault(
            e => string.Equals(e.Id, "CLOUDSMITH-MONITORING", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(entry);
        Assert.Equal("cloudsmith-monitoring", entry.Id);
        Assert.Equal("Monitoring",            entry.Name);
    }

    // ---------------------------------------------------------------------------
    // catalog get — missing id returns null
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CatalogGet_MissingId_ReturnsNull()
    {
        string json = MakeCatalogJson(new[] { MakeEntry("cloudsmith-monitoring") });

        using HttpClient http = new(new MockHttpMessageHandler(HttpStatusCode.OK, json))
        {
            BaseAddress = new Uri("http://test.local/")
        };

        HttpResponseMessage response = await http.GetAsync("api/v1/modules/catalog");
        string body = await response.Content.ReadAsStringAsync();

        CatalogResponse? catalog = JsonSerializer.Deserialize<CatalogResponse>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        CatalogEntry? entry = catalog?.Items.FirstOrDefault(
            e => string.Equals(e.Id, "does-not-exist", StringComparison.OrdinalIgnoreCase));

        Assert.Null(entry);
    }

    // ---------------------------------------------------------------------------
    // install --from-catalog — uses catalog version when no explicit version given
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CatalogInstall_UsesVersionFromCatalog_WhenNotExplicit()
    {
        // Catalog returns version "1.2.3"
        var entries = new[] { MakeEntry("cloudsmith-monitoring", version: "1.2.3", isVerified: true) };
        string catalogJson = MakeCatalogJson(entries);

        CatalogResponse? catalog = JsonSerializer.Deserialize<CatalogResponse>(catalogJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        CatalogEntry? entry = catalog?.Items.FirstOrDefault(
            e => string.Equals(e.Id, "cloudsmith-monitoring", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(entry);
        Assert.Equal("1.2.3", entry.Version);
    }

    // ---------------------------------------------------------------------------
    // install --from-catalog — POST /api/v1/modules/{id}/install with version body
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CatalogInstall_PostsToPerIdInstallEndpoint()
    {
        string installResponseJson = JsonSerializer.Serialize(
            new { id = "cloudsmith-monitoring", name = "Monitoring", version = "1.0.0", enabled = false });

        var handler = new CapturingHttpMessageHandler(HttpStatusCode.OK, installResponseJson);
        using HttpClient http = new(handler) { BaseAddress = new Uri("http://test.local/") };

        using StringContent content = new(
            JsonSerializer.Serialize(new { version = "1.0.0" }),
            System.Text.Encoding.UTF8, "application/json");

        HttpResponseMessage response =
            await http.PostAsync("api/v1/modules/cloudsmith-monitoring/install", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("cloudsmith-monitoring", handler.CapturedRequestUri);
        Assert.Contains("install",               handler.CapturedRequestUri);
        Assert.Contains("1.0.0",                 handler.CapturedBody);
    }

    // ---------------------------------------------------------------------------
    // install --from-catalog — unverified module triggers warning path
    // ---------------------------------------------------------------------------

    [Fact]
    public void CatalogInstall_UnverifiedEntry_FlaggedCorrectly()
    {
        // The unverified warning is handled at runtime via Console.ReadLine(); here we
        // just verify the model correctly reflects IsVerified = false.
        var entry = (CatalogEntry)JsonSerializer.Deserialize<CatalogEntry>(
            JsonSerializer.Serialize(MakeEntry(isVerified: false)),
            typeof(CatalogEntry),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        Assert.False(entry.IsVerified);
    }

    // ---------------------------------------------------------------------------
    // API error path — non-2xx returns error body
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CatalogList_NonSuccess_ReturnsErrorBody()
    {
        using HttpClient http = new(
            new MockHttpMessageHandler(HttpStatusCode.ServiceUnavailable, "{\"error\":\"unavailable\"}"))
        {
            BaseAddress = new Uri("http://test.local/")
        };

        HttpResponseMessage response = await http.GetAsync("api/v1/modules/catalog");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("unavailable", body);
    }
}
