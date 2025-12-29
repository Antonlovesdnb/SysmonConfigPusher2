namespace SysmonConfigPusher.Core.Interfaces;

/// <summary>
/// Service for downloading and caching Sysmon binaries.
/// </summary>
public interface ISysmonBinaryCacheService
{
    /// <summary>
    /// Gets the path to the cached Sysmon binary (Sysmon64.exe).
    /// </summary>
    string CachePath { get; }

    /// <summary>
    /// Checks if the Sysmon binary is cached.
    /// </summary>
    bool IsCached { get; }

    /// <summary>
    /// Gets information about the cached binary.
    /// </summary>
    SysmonCacheInfo? GetCacheInfo();

    /// <summary>
    /// Downloads the Sysmon binary from the configured URL and caches it.
    /// </summary>
    Task<SysmonCacheResult> UpdateCacheAsync(CancellationToken cancellationToken = default);
}

public record SysmonCacheInfo(
    string FilePath,
    string? Version,
    long FileSizeBytes,
    DateTime CachedAt);

public record SysmonCacheResult(
    bool Success,
    string? Message,
    SysmonCacheInfo? CacheInfo);
