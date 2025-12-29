using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SysmonConfigPusher.Core.Configuration;
using SysmonConfigPusher.Core.Interfaces;

namespace SysmonConfigPusher.Infrastructure.BinaryCache;

/// <summary>
/// Service for downloading and caching Sysmon binaries from live.sysinternals.com.
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

        _cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SysmonConfigPusher", "cache");

        Directory.CreateDirectory(_cacheDirectory);
    }

    public string BinaryFilename => GetBinaryFilename();

    public string CachePath => Path.Combine(_cacheDirectory, BinaryFilename);

    public bool IsCached => File.Exists(CachePath);

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
        if (!IsCached)
            return null;

        var fileInfo = new FileInfo(CachePath);
        string? version = null;

        try
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(CachePath);
            version = versionInfo.FileVersion;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get version info from cached Sysmon binary");
        }

        return new SysmonCacheInfo(
            CachePath,
            version,
            fileInfo.Length,
            fileInfo.LastWriteTimeUtc);
    }

    public async Task<SysmonCacheResult> UpdateCacheAsync(CancellationToken cancellationToken = default)
    {
        var downloadUrl = _settings.SysmonBinaryUrl;
        _logger.LogInformation("Downloading Sysmon binary from {Url} (will save as {Filename})", downloadUrl, BinaryFilename);

        try
        {
            // Download directly from the configured URL (respects Sysmon.exe vs Sysmon64.exe)
            using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            await SaveBinaryAsync(response, cancellationToken);

            var cacheInfo = GetCacheInfo();
            _logger.LogInformation("Sysmon binary cached successfully: {Path} (version: {Version}, size: {Size} bytes)",
                CachePath, cacheInfo?.Version ?? "unknown", cacheInfo?.FileSizeBytes ?? 0);

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

    private async Task SaveBinaryAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        response.EnsureSuccessStatusCode();

        var tempPath = CachePath + ".tmp";

        await using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        await using (var downloadStream = await response.Content.ReadAsStreamAsync(cancellationToken))
        {
            await downloadStream.CopyToAsync(fileStream, cancellationToken);
        }

        // Atomic replace
        if (File.Exists(CachePath))
            File.Delete(CachePath);

        File.Move(tempPath, CachePath);
    }
}

