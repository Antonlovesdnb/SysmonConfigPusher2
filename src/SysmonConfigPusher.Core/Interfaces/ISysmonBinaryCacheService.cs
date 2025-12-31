namespace SysmonConfigPusher.Core.Interfaces;

/// <summary>
/// Service for downloading and caching Sysmon binaries.
/// Supports caching multiple versions.
/// </summary>
public interface ISysmonBinaryCacheService
{
    /// <summary>
    /// Gets the filename of the Sysmon binary (e.g., Sysmon.exe or Sysmon64.exe).
    /// </summary>
    string BinaryFilename { get; }

    /// <summary>
    /// Gets the full path to the default/latest cached Sysmon binary.
    /// </summary>
    string CachePath { get; }

    /// <summary>
    /// Checks if any Sysmon binary is cached.
    /// </summary>
    bool IsCached { get; }

    /// <summary>
    /// Gets information about the default/latest cached binary.
    /// </summary>
    SysmonCacheInfo? GetCacheInfo();

    /// <summary>
    /// Downloads the Sysmon binary from the configured URL and caches it.
    /// </summary>
    Task<SysmonCacheResult> UpdateCacheAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all cached Sysmon versions.
    /// </summary>
    IReadOnlyList<SysmonCacheInfo> GetAllCachedVersions();

    /// <summary>
    /// Gets information about a specific cached version.
    /// </summary>
    SysmonCacheInfo? GetCacheInfo(string version);

    /// <summary>
    /// Gets the full path to a specific cached version.
    /// </summary>
    string? GetCachePath(string version);

    /// <summary>
    /// Checks if a specific version is cached.
    /// </summary>
    bool IsVersionCached(string version);

    /// <summary>
    /// Deletes a specific cached version.
    /// </summary>
    bool DeleteCachedVersion(string version);

    /// <summary>
    /// Adds a Sysmon binary from a file upload.
    /// </summary>
    Task<SysmonCacheResult> AddFromFileAsync(Stream fileStream, string filename, CancellationToken cancellationToken = default);
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
