namespace CloudSmith.Cli.Commands.Module;

/// <summary>
/// A single entry in the published module catalog.
/// </summary>
public sealed record CatalogEntry(
    string   Id,
    string   Name,
    string   Version,
    string   Description,
    string   Publisher,
    string   GhcrImageRef,
    string   ManifestUrl,
    string?  SignatureRef,
    bool     IsVerified,
    bool     IsInstalled,
    bool     IsEnabled
);

/// <summary>
/// The full response from GET /api/v1/modules/catalog.
/// </summary>
public sealed record CatalogResponse(
    List<CatalogEntry> Items,
    int                TotalCount,
    DateTimeOffset     FetchedAt
);
