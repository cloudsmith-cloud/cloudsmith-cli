using Microsoft.Identity.Client;

namespace CloudSmith.Cli.Services;

public interface IAuthService
{
    Task<CachedToken> LoginAsync(string server, CancellationToken cancellationToken = default);
    Task LogoutAsync(CancellationToken cancellationToken = default);
    CachedToken? GetCurrentToken();
}

public sealed class AuthService : IAuthService
{
    private readonly ITokenCacheService _tokenCache;
    private readonly IConfigService _configService;
    private readonly Func<string, IPublicClientApplication>? _appFactory;

    // Placeholder client ID — in a real deployment this is the Keycloak OIDC client ID.
    // The server URL is used to derive the authority dynamically.
    private const string DefaultClientId = "cloudsmith-cli";
    private const string DeviceCodeScope = "openid profile offline_access";

    public AuthService(ITokenCacheService tokenCache, IConfigService configService,
        Func<string, IPublicClientApplication>? appFactory = null)
    {
        _tokenCache = tokenCache;
        _configService = configService;
        _appFactory = appFactory;
    }

    public async Task<CachedToken> LoginAsync(string server, CancellationToken cancellationToken = default)
    {
        IPublicClientApplication app = _appFactory != null
            ? _appFactory(server)
            : BuildMsalApp(server);

        AuthenticationResult result = await app
            .AcquireTokenWithDeviceCode(
                new[] { DeviceCodeScope },
                deviceCodeResult =>
                {
                    Console.WriteLine(deviceCodeResult.Message);
                    return Task.CompletedTask;
                })
            .ExecuteAsync(cancellationToken);

        string upn = result.Account?.Username ?? result.ClaimsPrincipal?.Identity?.Name ?? "unknown";

        CachedToken token = new()
        {
            Upn = upn,
            AccessToken = result.AccessToken,
            RefreshToken = string.Empty, // MSAL manages refresh internally via token cache
            ExpiresOn = result.ExpiresOn,
            Server = server
        };

        _tokenCache.Save(token);
        return token;
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        CachedToken? current = _tokenCache.Load();

        if (current is { AccessToken: not null, Server: not null })
        {
            // Best-effort: revoke server-side
            await TryRevokeServerTokenAsync(current.Server, current.AccessToken, cancellationToken);
        }

        _tokenCache.Clear();
    }

    public CachedToken? GetCurrentToken()
    {
        CachedToken? token = _tokenCache.Load();
        if (token is null) return null;
        if (token.ExpiresOn <= DateTimeOffset.UtcNow) return null;
        return token;
    }

    private static IPublicClientApplication BuildMsalApp(string server)
    {
        // Derive authority from the server URL (Keycloak OIDC endpoint pattern).
        string authority = server.TrimEnd('/') + "/auth/realms/cloudsmith";

        return PublicClientApplicationBuilder
            .Create(DefaultClientId)
            .WithAuthority(new Uri(authority))
            .WithDefaultRedirectUri()
            .Build();
    }

    private static async Task TryRevokeServerTokenAsync(string server, string accessToken,
        CancellationToken cancellationToken)
    {
        try
        {
            using HttpClient http = new()
            {
                BaseAddress = new Uri(server.TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromSeconds(5)
            };
            http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            await http.DeleteAsync("api/v1/auth/token", cancellationToken);
        }
        catch
        {
            // Server unreachable or revocation endpoint not available — proceed with local logout.
        }
    }
}
