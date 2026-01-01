using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography.X509Certificates;
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
    private readonly IConfiguration _configuration;
    private readonly IAuditService _auditService;
    private readonly ISysmonBinaryCacheService _binaryCacheService;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly ILogger<SettingsController> _logger;

    public SettingsController(
        IWebHostEnvironment env,
        IConfiguration configuration,
        IAuditService auditService,
        ISysmonBinaryCacheService binaryCacheService,
        IHostApplicationLifetime appLifetime,
        ILogger<SettingsController> logger)
    {
        _env = env;
        _configuration = configuration;
        _auditService = auditService;
        _binaryCacheService = binaryCacheService;
        _appLifetime = appLifetime;
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
            var agent = root["Agent"];

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
                    SysmonBinaryUrl = sysmonConfigPusher?["SysmonBinaryUrl"]?.GetValue<string>() ?? "https://live.sysinternals.com/Sysmon.exe",
                    DefaultParallelism = sysmonConfigPusher?["DefaultParallelism"]?.GetValue<int>() ?? 50,
                    RemoteDirectory = sysmonConfigPusher?["RemoteDirectory"]?.GetValue<string>() ?? "C:\\SysmonFiles",
                    AuditLogPath = sysmonConfigPusher?["AuditLogPath"]?.GetValue<string>() ?? ""
                },
                Agent = new AgentSettingsDto
                {
                    RegistrationToken = agent?["RegistrationToken"]?.GetValue<string>() ?? "",
                    PollIntervalSeconds = agent?["PollIntervalSeconds"]?.GetValue<int>() ?? 30
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
            var oldAgent = root["Agent"]?.ToJsonString();

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
                ["RemoteDirectory"] = settings.SysmonConfigPusher.RemoteDirectory,
                ["AuditLogPath"] = settings.SysmonConfigPusher.AuditLogPath
            };

            // Update Agent section
            root["Agent"] = new JsonObject
            {
                ["RegistrationToken"] = settings.Agent.RegistrationToken,
                ["PollIntervalSeconds"] = settings.Agent.PollIntervalSeconds
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
                NewSysmonConfigPusher = root["SysmonConfigPusher"]?.ToJsonString(),
                OldAgent = oldAgent,
                NewAgent = root["Agent"]?.ToJsonString()
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

    /// <summary>
    /// Get all cached Sysmon binary versions.
    /// </summary>
    [HttpGet("binary-cache/versions")]
    [Authorize(Policy = "RequireViewer")]
    public ActionResult<IEnumerable<BinaryCacheStatusDto>> GetAllCachedVersions()
    {
        var versions = _binaryCacheService.GetAllCachedVersions();
        return Ok(versions.Select(v => new BinaryCacheStatusDto
        {
            IsCached = true,
            FilePath = v.FilePath,
            Version = v.Version,
            FileSizeBytes = v.FileSizeBytes,
            CachedAt = v.CachedAt
        }));
    }

    /// <summary>
    /// Upload a Sysmon binary from file.
    /// </summary>
    [HttpPost("binary-cache/upload")]
    [RequestSizeLimit(50 * 1024 * 1024)] // 50MB limit
    public async Task<ActionResult<BinaryCacheUpdateResultDto>> UploadBinary(IFormFile file, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "No file provided" });

        if (!file.FileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "File must be an .exe file" });

        _logger.LogInformation("User {User} uploading Sysmon binary: {Filename}", User.Identity?.Name, file.FileName);

        await using var stream = file.OpenReadStream();
        var result = await _binaryCacheService.AddFromFileAsync(stream, file.FileName, cancellationToken);

        await _auditService.LogAsync(User.Identity?.Name, AuditAction.BinaryCacheUpdate, new
        {
            Action = "Upload",
            Filename = file.FileName,
            Success = result.Success,
            Message = result.Message,
            Version = result.CacheInfo?.Version,
            FileSizeBytes = result.CacheInfo?.FileSizeBytes
        });

        if (!result.Success)
            return BadRequest(new BinaryCacheUpdateResultDto
            {
                Success = false,
                Message = result.Message ?? "Failed to add binary"
            });

        return Ok(new BinaryCacheUpdateResultDto
        {
            Success = result.Success,
            Message = result.Message ?? "Binary uploaded successfully",
            Version = result.CacheInfo?.Version,
            FileSizeBytes = result.CacheInfo?.FileSizeBytes,
            CachedAt = result.CacheInfo?.CachedAt
        });
    }

    /// <summary>
    /// Delete a specific cached Sysmon version.
    /// </summary>
    [HttpDelete("binary-cache/versions/{version}")]
    public async Task<ActionResult> DeleteCachedVersion(string version)
    {
        _logger.LogInformation("User {User} deleting cached Sysmon version: {Version}", User.Identity?.Name, version);

        var result = _binaryCacheService.DeleteCachedVersion(version);

        await _auditService.LogAsync(User.Identity?.Name, AuditAction.BinaryCacheUpdate, new
        {
            Action = "Delete",
            Version = version,
            Success = result
        });

        if (!result)
            return NotFound(new { message = $"Version {version} not found in cache" });

        return Ok(new { message = $"Version {version} deleted successfully" });
    }

    /// <summary>
    /// Get TLS certificate status.
    /// </summary>
    [HttpGet("tls-status")]
    [Authorize(Policy = "RequireViewer")]
    public ActionResult<TlsCertificateStatusDto> GetTlsStatus()
    {
        try
        {
            var result = new TlsCertificateStatusDto();

            // Check Kestrel configuration for certificate settings
            var certPath = _configuration["Kestrel:Endpoints:Https:Certificate:Path"];
            var certSubject = _configuration["Kestrel:Endpoints:Https:Certificate:Subject"];
            var certStore = _configuration["Kestrel:Endpoints:Https:Certificate:Store"];
            var certLocation = _configuration["Kestrel:Endpoints:Https:Certificate:Location"];

            if (!string.IsNullOrEmpty(certPath))
            {
                // PFX file certificate
                result.ConfigurationType = "PFX File";
                result.ConfiguredPath = certPath;

                if (System.IO.File.Exists(certPath))
                {
                    try
                    {
                        var certPassword = _configuration["Kestrel:Endpoints:Https:Certificate:Password"];
                        var cert = new X509Certificate2(certPath, certPassword);
                        PopulateCertificateDetails(result, cert);
                        cert.Dispose();
                    }
                    catch (Exception ex)
                    {
                        result.ErrorMessage = $"Could not read certificate: {ex.Message}";
                    }
                }
                else
                {
                    result.ErrorMessage = "Certificate file not found";
                }
            }
            else if (!string.IsNullOrEmpty(certSubject))
            {
                // Windows Certificate Store
                result.ConfigurationType = "Windows Certificate Store";
                result.ConfiguredPath = $"{certLocation}/{certStore}/{certSubject}";

                try
                {
                    var storeLocation = certLocation?.ToLower() == "currentuser"
                        ? StoreLocation.CurrentUser
                        : StoreLocation.LocalMachine;
                    var storeName = certStore ?? "My";

                    using var store = new X509Store(storeName, storeLocation);
                    store.Open(OpenFlags.ReadOnly);
                    var certs = store.Certificates.Find(X509FindType.FindBySubjectDistinguishedName, certSubject, false);

                    if (certs.Count == 0)
                    {
                        // Try partial match
                        certs = store.Certificates.Find(X509FindType.FindBySubjectName, certSubject.Replace("CN=", ""), false);
                    }

                    if (certs.Count > 0)
                    {
                        PopulateCertificateDetails(result, certs[0]);
                    }
                    else
                    {
                        result.ErrorMessage = "Certificate not found in store";
                    }
                }
                catch (Exception ex)
                {
                    result.ErrorMessage = $"Could not access certificate store: {ex.Message}";
                }
            }
            else
            {
                // Development certificate or default
                result.ConfigurationType = "Development Certificate";
                result.IsDevelopmentCertificate = true;

                // Try to find the dev cert
                try
                {
                    using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
                    store.Open(OpenFlags.ReadOnly);
                    var devCerts = store.Certificates
                        .Find(X509FindType.FindBySubjectName, "localhost", false)
                        .Where(c => c.Issuer.Contains("ASP.NET Core") || c.FriendlyName.Contains("ASP.NET"))
                        .OrderByDescending(c => c.NotAfter)
                        .ToList();

                    if (devCerts.Count > 0)
                    {
                        PopulateCertificateDetails(result, devCerts[0]);
                    }
                }
                catch
                {
                    // Ignore errors reading dev cert
                }
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get TLS certificate status");
            return StatusCode(500, new { message = "Failed to get TLS status" });
        }
    }

    private static void PopulateCertificateDetails(TlsCertificateStatusDto result, X509Certificate2 cert)
    {
        result.Subject = cert.Subject;
        result.Issuer = cert.Issuer;
        result.Thumbprint = cert.Thumbprint;
        result.NotBefore = cert.NotBefore;
        result.NotAfter = cert.NotAfter;
        result.IsValid = DateTime.Now >= cert.NotBefore && DateTime.Now <= cert.NotAfter;
        result.DaysUntilExpiry = (int)(cert.NotAfter - DateTime.Now).TotalDays;
    }

    /// <summary>
    /// Restart the application to apply settings changes.
    /// </summary>
    [HttpPost("restart")]
    public async Task<ActionResult<RestartResultDto>> RestartApplication()
    {
        _logger.LogWarning("User {User} initiated application restart", User.Identity?.Name);

        await _auditService.LogAsync(User.Identity?.Name, AuditAction.ServiceRestart, new
        {
            RequestedAt = DateTime.UtcNow
        });

        // Schedule the stop after a brief delay to allow the response to be sent
        _ = Task.Run(async () =>
        {
            await Task.Delay(500);
            _appLifetime.StopApplication();
        });

        return Ok(new RestartResultDto
        {
            Success = true,
            Message = "Application is restarting. Please wait a few seconds and refresh the page."
        });
    }
}

// DTOs
public class AppSettingsDto
{
    public AuthorizationSettingsDto Authorization { get; set; } = new();
    public SysmonConfigPusherSettingsDto SysmonConfigPusher { get; set; } = new();
    public AgentSettingsDto Agent { get; set; } = new();
}

public class AgentSettingsDto
{
    public string RegistrationToken { get; set; } = "";
    public int PollIntervalSeconds { get; set; } = 30;
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
    public string SysmonBinaryUrl { get; set; } = "https://live.sysinternals.com/Sysmon.exe";
    public int DefaultParallelism { get; set; } = 50;
    public string RemoteDirectory { get; set; } = "C:\\SysmonFiles";
    public string AuditLogPath { get; set; } = "";
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

public class RestartResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
}

public class TlsCertificateStatusDto
{
    public string ConfigurationType { get; set; } = "Unknown";
    public string? ConfiguredPath { get; set; }
    public bool IsDevelopmentCertificate { get; set; }
    public string? Subject { get; set; }
    public string? Issuer { get; set; }
    public string? Thumbprint { get; set; }
    public DateTime? NotBefore { get; set; }
    public DateTime? NotAfter { get; set; }
    public bool IsValid { get; set; }
    public int? DaysUntilExpiry { get; set; }
    public string? ErrorMessage { get; set; }
}
