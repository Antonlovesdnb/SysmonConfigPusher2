using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SysmonConfigPusher.Core.Interfaces;

namespace SysmonConfigPusher.Service.Controllers;

/// <summary>
/// Controller for server capability discovery.
/// Used by frontend to adapt UI based on available features.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[AllowAnonymous] // Needed before login to show appropriate UI
public class CapabilitiesController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IActiveDirectoryService _adService;
    private readonly IRemoteExecutionService _executionService;
    private readonly IFileTransferService _fileTransferService;
    private readonly ILogger<CapabilitiesController> _logger;

    public CapabilitiesController(
        IConfiguration configuration,
        IActiveDirectoryService adService,
        IRemoteExecutionService executionService,
        IFileTransferService fileTransferService,
        ILogger<CapabilitiesController> logger)
    {
        _configuration = configuration;
        _adService = adService;
        _executionService = executionService;
        _fileTransferService = fileTransferService;
        _logger = logger;
    }

    /// <summary>
    /// Get server capabilities for frontend adaptation.
    /// </summary>
    [HttpGet]
    public ActionResult<ServerCapabilities> GetCapabilities()
    {
        var serverMode = _configuration["ServerMode"] ?? "Full";
        var authMode = _configuration["Authentication:Mode"] ?? "Windows";

        var capabilities = new ServerCapabilities
        {
            ServerMode = serverMode,
            AuthenticationMode = authMode,
            Features = new FeatureFlags
            {
                WmiDeployment = _executionService.IsAvailable,
                SmbFileTransfer = _fileTransferService.IsAvailable,
                ActiveDirectory = _adService.IsAvailable,
                AgentDeployment = true, // Always available
                EventLogViewer = _executionService.IsAvailable, // Requires WMI
                NoiseAnalysis = _executionService.IsAvailable // Requires WMI for event log access
            },
            Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown",
            Platform = OperatingSystem.IsWindows() ? "Windows" : "Linux"
        };

        _logger.LogDebug("Capabilities requested: Mode={Mode}, Auth={Auth}", serverMode, authMode);

        return Ok(capabilities);
    }
}

// DTOs
public class ServerCapabilities
{
    /// <summary>
    /// Server deployment mode: "Full" or "AgentOnly"
    /// </summary>
    public string ServerMode { get; set; } = "Full";

    /// <summary>
    /// Authentication mode: "Windows", "ApiKey", or "OAuth"
    /// </summary>
    public string AuthenticationMode { get; set; } = "Windows";

    /// <summary>
    /// Available features based on server mode and platform
    /// </summary>
    public FeatureFlags Features { get; set; } = new();

    /// <summary>
    /// Server version
    /// </summary>
    public string Version { get; set; } = "";

    /// <summary>
    /// Server platform: "Windows" or "Linux"
    /// </summary>
    public string Platform { get; set; } = "";
}

public class FeatureFlags
{
    /// <summary>
    /// WMI-based remote command execution (Windows only, Full mode)
    /// </summary>
    public bool WmiDeployment { get; set; }

    /// <summary>
    /// SMB file transfer to remote hosts (Windows only, Full mode)
    /// </summary>
    public bool SmbFileTransfer { get; set; }

    /// <summary>
    /// Active Directory computer browsing (Windows domain, Full mode)
    /// </summary>
    public bool ActiveDirectory { get; set; }

    /// <summary>
    /// Agent-based deployment (always available)
    /// </summary>
    public bool AgentDeployment { get; set; } = true;

    /// <summary>
    /// Remote event log viewing (requires WMI)
    /// </summary>
    public bool EventLogViewer { get; set; }

    /// <summary>
    /// Noise analysis feature (requires WMI for event log access)
    /// </summary>
    public bool NoiseAnalysis { get; set; }
}
