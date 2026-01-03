using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SysmonConfigPusher.Core.Configuration;
using SysmonConfigPusher.Core.Interfaces;

namespace SysmonConfigPusher.Infrastructure.BinaryCache;

/// <summary>
/// Service for downloading and caching Sysmon binaries from live.sysinternals.com.
/// Supports caching multiple versions in separate subdirectories.
/// </summary>
public class SysmonBinaryCacheService : ISysmonBinaryCacheService
{
    private readonly SysmonConfigPusherSettings _settings;
    private readonly ILogger<SysmonBinaryCacheService> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _cacheDirectory;

    public SysmonBinaryCacheService(
        IOptions<SysmonConfigPusherSettings> settings,
        ILogger<SysmonBinaryCacheService> logger,
        HttpClient httpClient)
    {
        _settings = settings.Value;
        _logger = logger;
        _httpClient = httpClient;

        if (OperatingSystem.IsWindows())
        {
            _cacheDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "SysmonConfigPusher", "cache");
        }
        else
        {
            // On Linux/Docker, use /data/cache
            _cacheDirectory = "/data/cache";
        }

        Directory.CreateDirectory(_cacheDirectory);
    }

    public string BinaryFilename => GetBinaryFilename();

    public string CachePath
    {
        get
        {
            // Return the path to the latest/default version
            var latestVersion = GetAllCachedVersions().FirstOrDefault();
            return latestVersion?.FilePath ?? Path.Combine(_cacheDirectory, "default", BinaryFilename);
        }
    }

    public bool IsCached => GetAllCachedVersions().Any();

    private string GetBinaryFilename()
    {
        try
        {
            var uri = new Uri(_settings.SysmonBinaryUrl);
            var filename = Path.GetFileName(uri.LocalPath);
            return string.IsNullOrEmpty(filename) ? "Sysmon64.exe" : filename;
        }
        catch
        {
            return "Sysmon64.exe";
        }
    }

    public SysmonCacheInfo? GetCacheInfo()
    {
        // Return info for the latest version
        return GetAllCachedVersions().FirstOrDefault();
    }

    public SysmonCacheInfo? GetCacheInfo(string version)
    {
        var versionDir = Path.Combine(_cacheDirectory, SanitizeVersion(version));
        if (!Directory.Exists(versionDir))
            return null;

        var exeFiles = Directory.GetFiles(versionDir, "*.exe");
        if (exeFiles.Length == 0)
            return null;

        var filePath = exeFiles[0];
        return GetCacheInfoFromFile(filePath);
    }

    public string? GetCachePath(string version)
    {
        var versionDir = Path.Combine(_cacheDirectory, SanitizeVersion(version));
        if (!Directory.Exists(versionDir))
            return null;

        var exeFiles = Directory.GetFiles(versionDir, "*.exe");
        return exeFiles.Length > 0 ? exeFiles[0] : null;
    }

    public bool IsVersionCached(string version)
    {
        return GetCachePath(version) != null;
    }

    public IReadOnlyList<SysmonCacheInfo> GetAllCachedVersions()
    {
        var versions = new List<SysmonCacheInfo>();

        if (!Directory.Exists(_cacheDirectory))
            return versions;

        foreach (var dir in Directory.GetDirectories(_cacheDirectory))
        {
            var exeFiles = Directory.GetFiles(dir, "*.exe");
            if (exeFiles.Length > 0)
            {
                var info = GetCacheInfoFromFile(exeFiles[0]);
                if (info != null)
                    versions.Add(info);
            }
        }

        // Sort by version descending (latest first)
        return versions
            .OrderByDescending(v => v.Version != null ? new Version(NormalizeVersion(v.Version)) : new Version(0, 0))
            .ToList();
    }

    public bool DeleteCachedVersion(string version)
    {
        var versionDir = Path.Combine(_cacheDirectory, SanitizeVersion(version));
        if (!Directory.Exists(versionDir))
            return false;

        try
        {
            Directory.Delete(versionDir, recursive: true);
            _logger.LogInformation("Deleted cached Sysmon version {Version}", version);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete cached Sysmon version {Version}", version);
            return false;
        }
    }

    public async Task<SysmonCacheResult> UpdateCacheAsync(CancellationToken cancellationToken = default)
    {
        var downloadUrl = _settings.SysmonBinaryUrl;
        _logger.LogInformation("Downloading Sysmon binary from {Url}", downloadUrl);

        try
        {
            using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            // Download to a temp file first to get the version
            var tempPath = Path.Combine(_cacheDirectory, $"download_{Guid.NewGuid()}.tmp");
            await SaveToFileAsync(response, tempPath, cancellationToken);

            // Get version from the downloaded file
            var versionInfo = FileVersionInfo.GetVersionInfo(tempPath);
            var version = versionInfo.FileVersion ?? "unknown";

            // Move to version-specific directory
            var versionDir = Path.Combine(_cacheDirectory, SanitizeVersion(version));
            Directory.CreateDirectory(versionDir);

            var finalPath = Path.Combine(versionDir, BinaryFilename);
            if (File.Exists(finalPath))
                File.Delete(finalPath);
            File.Move(tempPath, finalPath);

            var cacheInfo = GetCacheInfoFromFile(finalPath);
            _logger.LogInformation("Sysmon binary cached successfully: {Path} (version: {Version}, size: {Size} bytes)",
                finalPath, cacheInfo?.Version ?? "unknown", cacheInfo?.FileSizeBytes ?? 0);

            return new SysmonCacheResult(true, "Sysmon binary downloaded and cached successfully", cacheInfo);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to download Sysmon binary from {Url}", downloadUrl);
            return new SysmonCacheResult(false, $"Failed to download: {ex.Message}", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error caching Sysmon binary");
            return new SysmonCacheResult(false, $"Error: {ex.Message}", null);
        }
    }

    public async Task<SysmonCacheResult> AddFromFileAsync(Stream fileStream, string filename, CancellationToken cancellationToken = default)
    {
        try
        {
            // Validate filename
            if (!filename.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return new SysmonCacheResult(false, "File must be an .exe file", null);

            // Save to temp file first
            var tempPath = Path.Combine(_cacheDirectory, $"upload_{Guid.NewGuid()}.tmp");
            await using (var tempFile = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await fileStream.CopyToAsync(tempFile, cancellationToken);
            }

            // Get version from the uploaded file
            var versionInfo = FileVersionInfo.GetVersionInfo(tempPath);
            var version = versionInfo.FileVersion;

            if (string.IsNullOrEmpty(version))
            {
                File.Delete(tempPath);
                return new SysmonCacheResult(false, "Could not determine version from file. Make sure this is a valid Sysmon executable.", null);
            }

            // Move to version-specific directory
            var versionDir = Path.Combine(_cacheDirectory, SanitizeVersion(version));
            Directory.CreateDirectory(versionDir);

            var finalPath = Path.Combine(versionDir, filename);
            if (File.Exists(finalPath))
                File.Delete(finalPath);
            File.Move(tempPath, finalPath);

            var cacheInfo = GetCacheInfoFromFile(finalPath);
            _logger.LogInformation("Sysmon binary added from upload: {Path} (version: {Version}, size: {Size} bytes)",
                finalPath, cacheInfo?.Version ?? "unknown", cacheInfo?.FileSizeBytes ?? 0);

            return new SysmonCacheResult(true, $"Sysmon {version} added successfully", cacheInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding Sysmon binary from file upload");
            return new SysmonCacheResult(false, $"Error: {ex.Message}", null);
        }
    }

    private SysmonCacheInfo? GetCacheInfoFromFile(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        var fileInfo = new FileInfo(filePath);
        string? version = null;

        try
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(filePath);
            version = versionInfo.FileVersion;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get version info from {FilePath}", filePath);
        }

        return new SysmonCacheInfo(
            filePath,
            version,
            fileInfo.Length,
            fileInfo.LastWriteTimeUtc);
    }

    private async Task SaveToFileAsync(HttpResponseMessage response, string path, CancellationToken cancellationToken)
    {
        await using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await using var downloadStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await downloadStream.CopyToAsync(fileStream, cancellationToken);
    }

    private static string SanitizeVersion(string version)
    {
        // Replace any characters that aren't valid in directory names
        return string.Join("_", version.Split(Path.GetInvalidFileNameChars()));
    }

    private static string NormalizeVersion(string version)
    {
        // Ensure version has 4 parts for proper comparison
        var parts = version.Split('.');
        while (parts.Length < 4)
        {
            parts = parts.Append("0").ToArray();
        }
        return string.Join(".", parts.Take(4));
    }
}
