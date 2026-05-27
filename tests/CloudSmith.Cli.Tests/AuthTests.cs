using CloudSmith.Cli.Services;
using Xunit;

namespace CloudSmith.Cli.Tests;

/// <summary>
/// AB#1523 — auth login: token written to cache.
/// AB#1524 — auth logout: cache cleared, revocation attempted.
/// </summary>
public class AuthTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string TempTokenPath() =>
        Path.Combine(Path.GetTempPath(), $"cs-test-tokens-{Guid.NewGuid():N}.json");

    private static TokenCacheService MakeCache(string path) => new(path);

    // -------------------------------------------------------------------------
    // AB#1523 — Token cache round-trip
    // -------------------------------------------------------------------------

    [Fact]
    public void TokenCache_Save_Then_Load_ReturnsSameToken()
    {
        string path = TempTokenPath();
        try
        {
            TokenCacheService cache = MakeCache(path);

            CachedToken saved = new()
            {
                Upn          = "user@example.com",
                AccessToken  = "test-access-token",
                RefreshToken = "",
                ExpiresOn    = DateTimeOffset.UtcNow.AddHours(1),
                Server       = "https://cs.example.com"
            };

            cache.Save(saved);

            CachedToken? loaded = cache.Load();

            Assert.NotNull(loaded);
            Assert.Equal("user@example.com",        loaded!.Upn);
            Assert.Equal("test-access-token",        loaded.AccessToken);
            Assert.Equal("https://cs.example.com",   loaded.Server);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void TokenCache_Load_WhenFileAbsent_ReturnsNull()
    {
        string path = TempTokenPath();
        // Do not create the file
        TokenCacheService cache = MakeCache(path);

        CachedToken? result = cache.Load();

        Assert.Null(result);
    }

    [Fact]
    public void TokenCache_FileIsCreated_AfterSave()
    {
        string path = TempTokenPath();
        try
        {
            TokenCacheService cache = MakeCache(path);

            cache.Save(new CachedToken
            {
                Upn          = "u@example.com",
                AccessToken  = "tok",
                ExpiresOn    = DateTimeOffset.UtcNow.AddHours(1),
                Server       = "https://srv"
            });

            Assert.True(File.Exists(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // -------------------------------------------------------------------------
    // AB#1524 — Logout: cache cleared
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Logout_ClearsLocalCache()
    {
        string tokenPath  = TempTokenPath();
        string configPath = Path.GetTempFileName();

        try
        {
            TokenCacheService tokenCache   = MakeCache(tokenPath);
            ConfigService     configSvc    = new(configPath);
            AuthService       authService  = new(tokenCache, configSvc);

            // Write a token to the cache manually
            tokenCache.Save(new CachedToken
            {
                Upn          = "user@example.com",
                AccessToken  = "stale-token",
                ExpiresOn    = DateTimeOffset.UtcNow.AddHours(-1), // already expired
                Server       = "https://unreachable.invalid"
            });

            Assert.True(File.Exists(tokenPath), "Token file should exist before logout.");

            await authService.LogoutAsync();

            Assert.False(File.Exists(tokenPath), "Token file should be deleted after logout.");
        }
        finally
        {
            if (File.Exists(tokenPath))  File.Delete(tokenPath);
            if (File.Exists(configPath)) File.Delete(configPath);
        }
    }

    [Fact]
    public async Task Logout_WhenNoCacheExists_DoesNotThrow()
    {
        string tokenPath  = TempTokenPath();
        string configPath = Path.GetTempFileName();

        try
        {
            TokenCacheService tokenCache  = MakeCache(tokenPath);
            ConfigService     configSvc   = new(configPath);
            AuthService       authService = new(tokenCache, configSvc);

            // No token was ever saved
            Exception? ex = await Record.ExceptionAsync(() => authService.LogoutAsync());

            Assert.Null(ex);
        }
        finally
        {
            if (File.Exists(tokenPath))  File.Delete(tokenPath);
            if (File.Exists(configPath)) File.Delete(configPath);
        }
    }

    [Fact]
    public void GetCurrentToken_WhenExpired_ReturnsNull()
    {
        string tokenPath  = TempTokenPath();
        string configPath = Path.GetTempFileName();

        try
        {
            TokenCacheService tokenCache  = MakeCache(tokenPath);
            ConfigService     configSvc   = new(configPath);
            AuthService       authService = new(tokenCache, configSvc);

            tokenCache.Save(new CachedToken
            {
                Upn         = "user@example.com",
                AccessToken = "old-token",
                ExpiresOn   = DateTimeOffset.UtcNow.AddSeconds(-1), // expired
                Server      = "https://srv"
            });

            CachedToken? result = authService.GetCurrentToken();

            Assert.Null(result);
        }
        finally
        {
            if (File.Exists(tokenPath))  File.Delete(tokenPath);
            if (File.Exists(configPath)) File.Delete(configPath);
        }
    }

    [Fact]
    public void GetCurrentToken_WhenValid_ReturnsToken()
    {
        string tokenPath  = TempTokenPath();
        string configPath = Path.GetTempFileName();

        try
        {
            TokenCacheService tokenCache  = MakeCache(tokenPath);
            ConfigService     configSvc   = new(configPath);
            AuthService       authService = new(tokenCache, configSvc);

            tokenCache.Save(new CachedToken
            {
                Upn         = "user@example.com",
                AccessToken = "fresh-token",
                ExpiresOn   = DateTimeOffset.UtcNow.AddHours(1),
                Server      = "https://srv"
            });

            CachedToken? result = authService.GetCurrentToken();

            Assert.NotNull(result);
            Assert.Equal("user@example.com", result!.Upn);
        }
        finally
        {
            if (File.Exists(tokenPath))  File.Delete(tokenPath);
            if (File.Exists(configPath)) File.Delete(configPath);
        }
    }
}
