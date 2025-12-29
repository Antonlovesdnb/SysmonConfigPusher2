using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.Json.Nodes;
using SysmonConfigPusher.Core.Interfaces;

namespace SysmonConfigPusher.Service.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "RequireAdmin")]
public class SettingsController : ControllerBase
{
    private readonly IWebHostEnvironment _env;
    private readonly IAuditService _auditService;
    private readonly ISysmonBinaryCacheService _binaryCacheService;
    private readonly ILogger<SettingsController> _logger;

    public SettingsController(
        IWebHostEnvironment env,
        IAuditService auditService,
        ISysmonBinaryCacheService binaryCacheService,
        ILogger<SettingsController> logger)
    {
        _env = env;
        _auditService = auditService;
        _binaryCacheService = binaryCacheService;
        _logger = logger;
    }

    private string GetAppSettingsPath()
    {
        return Path.Combine(_env.ContentRootPath, "appsettings.json");
    }

    /// <summary>
    /// Get all editable application settings.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<AppSettingsDto>> GetSettings()
    {
        try
        {
            var path = GetAppSettingsPath();
            var json = await System.IO.File.ReadAllTextAsync(path);
            var root = JsonNode.Parse(json);

            if (root == null)
                return StatusCode(500, new { message = "Failed to parse settings file" });

            var authorization = root["Authorization"];
            var sysmonConfigPusher = root["SysmonConfigPusher"];

            return Ok(new AppSettingsDto
            {
                Authorization = new AuthorizationSettingsDto
                {
                    AdminGroup = authorization?["AdminGroup"]?.GetValue<string>() ?? "SysmonPusher-Admins",
                    OperatorGroup = authorization?["OperatorGroup"]?.GetValue<string>() ?? "SysmonPusher-Operators",
                    ViewerGroup = authorization?["ViewerGroup"]?.GetValue<string>() ?? "SysmonPusher-Viewers",
                    DefaultRole = authorization?["DefaultRole"]?.GetValue<string>() ?? "Viewer"
                },
                SysmonConfigPusher = new SysmonConfigPusherSettingsDto
                {
                    SysmonBinaryUrl = sysmonConfigPusher?["SysmonBinaryUrl"]?.GetValue<string>() ?? "https://live.sysinternals.com/Sysmon64.exe",
                    DefaultParallelism = sysmonConfigPusher?["DefaultParallelism"]?.GetValue<int>() ?? 50,
                    RemoteDirectory = sysmonConfigPusher?["RemoteDirectory"]?.GetValue<string>() ?? "C:\\SysmonFiles"
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read settings file");
            return StatusCode(500, new { message = "Failed to read settings file" });
        }
    }

    /// <summary>
    /// Update application settings. Requires application restart to take effect.
    /// </summary>
    [HttpPut]
    public async Task<ActionResult<UpdateSettingsResultDto>> UpdateSettings([FromBody] AppSettingsDto settings)
    {
        try
        {
            var path = GetAppSettingsPath();
            var json = await System.IO.File.ReadAllTextAsync(path);
            var root = JsonNode.Parse(json);

            if (root == null)
                return StatusCode(500, new { message = "Failed to parse settings file" });

            // Capture old values for audit
            var oldAuthorization = root["Authorization"]?.ToJsonString();
            var oldSysmonConfigPusher = root["SysmonConfigPusher"]?.ToJsonString();

            // Update Authorization section
            root["Authorization"] = new JsonObject
            {
                ["AdminGroup"] = settings.Authorization.AdminGroup,
                ["OperatorGroup"] = settings.Authorization.OperatorGroup,
                ["ViewerGroup"] = settings.Authorization.ViewerGroup,
                ["DefaultRole"] = settings.Authorization.DefaultRole
            };

            // Update SysmonConfigPusher section
            root["SysmonConfigPusher"] = new JsonObject
            {
                ["SysmonBinaryUrl"] = settings.SysmonConfigPusher.SysmonBinaryUrl,
                ["DefaultParallelism"] = settings.SysmonConfigPusher.DefaultParallelism,
                ["RemoteDirectory"] = settings.SysmonConfigPusher.RemoteDirectory
            };

            // Write back with proper formatting
            var options = new JsonSerializerOptions { WriteIndented = true };
            var updatedJson = root.ToJsonString(options);
            await System.IO.File.WriteAllTextAsync(path, updatedJson);

            // Audit log the change
            await _auditService.LogAsync(User.Identity?.Name, AuditAction.SettingsUpdate, new
            {
                OldAuthorization = oldAuthorization,
                NewAuthorization = root["Authorization"]?.ToJsonString(),
                OldSysmonConfigPusher = oldSysmonConfigPusher,
                NewSysmonConfigPusher = root["SysmonConfigPusher"]?.ToJsonString()
            });

            _logger.LogInformation("User {User} updated application settings", User.Identity?.Name);

            return Ok(new UpdateSettingsResultDto
            {
                Success = true,
                Message = "Settings saved successfully. Please restart the application for changes to take effect.",
                RestartRequired = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update settings file");
            return StatusCode(500, new { message = "Failed to save settings: " + ex.Message });
        }
    }

    /// <summary>
    /// Get Sysmon binary cache status.
    /// </summary>
    [HttpGet("binary-cache")]
    [Authorize(Policy = "RequireViewer")]
    public ActionResult<BinaryCacheStatusDto> GetBinaryCacheStatus()
    {
        var cacheInfo = _binaryCacheService.GetCacheInfo();

        return Ok(new BinaryCacheStatusDto
        {
            IsCached = _binaryCacheService.IsCached,
            FilePath = _binaryCacheService.CachePath,
            Version = cacheInfo?.Version,
            FileSizeBytes = cacheInfo?.FileSizeBytes,
            CachedAt = cacheInfo?.CachedAt
        });
    }

    /// <summary>
    /// Download/update the Sysmon binary cache.
    /// </summary>
    [HttpPost("binary-cache/update")]
    public async Task<ActionResult<BinaryCacheUpdateResultDto>> UpdateBinaryCache(CancellationToken cancellationToken)
    {
        _logger.LogInformation("User {User} initiated Sysmon binary cache update", User.Identity?.Name);

        var result = await _binaryCacheService.UpdateCacheAsync(cancellationToken);

        await _auditService.LogAsync(User.Identity?.Name, AuditAction.BinaryCacheUpdate, new
        {
            Success = result.Success,
            Message = result.Message,
            Version = result.CacheInfo?.Version,
            FileSizeBytes = result.CacheInfo?.FileSizeBytes
        });

        return Ok(new BinaryCacheUpdateResultDto
        {
            Success = result.Success,
            Message = result.Message ?? (result.Success ? "Binary cached successfully" : "Failed to cache binary"),
            Version = result.CacheInfo?.Version,
            FileSizeBytes = result.CacheInfo?.FileSizeBytes,
            CachedAt = result.CacheInfo?.CachedAt
        });
    }
}

// DTOs
public class AppSettingsDto
{
    public AuthorizationSettingsDto Authorization { get; set; } = new();
    public SysmonConfigPusherSettingsDto SysmonConfigPusher { get; set; } = new();
}

public class AuthorizationSettingsDto
{
    public string AdminGroup { get; set; } = "SysmonPusher-Admins";
    public string OperatorGroup { get; set; } = "SysmonPusher-Operators";
    public string ViewerGroup { get; set; } = "SysmonPusher-Viewers";
    public string DefaultRole { get; set; } = "Viewer";
}

public class SysmonConfigPusherSettingsDto
{
    public string SysmonBinaryUrl { get; set; } = "https://live.sysinternals.com/Sysmon64.exe";
    public int DefaultParallelism { get; set; } = 50;
    public string RemoteDirectory { get; set; } = "C:\\SysmonFiles";
}

public class UpdateSettingsResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public bool RestartRequired { get; set; }
}

public class BinaryCacheStatusDto
{
    public bool IsCached { get; set; }
    public string? FilePath { get; set; }
    public string? Version { get; set; }
    public long? FileSizeBytes { get; set; }
    public DateTime? CachedAt { get; set; }
}

public class BinaryCacheUpdateResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string? Version { get; set; }
    public long? FileSizeBytes { get; set; }
    public DateTime? CachedAt { get; set; }
}
