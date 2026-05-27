namespace CloudSmith.Cli.Commands.Platform;

/// <summary>
/// Response from GET /api/v1/platform/updates/check.
/// </summary>
public sealed record PlatformUpdateStatus(
    string         CurrentVersion,
    string         LatestVersion,
    string         LatestDigest,
    bool           UpdateAvailable,
    DateTimeOffset CheckedAt
);

/// <summary>
/// Response from PUT /api/v1/platform/updates/apply.
/// </summary>
public sealed record PlatformUpdateResponse(
    Guid   UpdateId,
    string Status,
    string Message
);
