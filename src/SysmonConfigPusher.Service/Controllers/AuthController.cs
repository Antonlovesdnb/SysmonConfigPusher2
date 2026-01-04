using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SysmonConfigPusher.Service.Authentication;

namespace SysmonConfigPusher.Service.Controllers;

/// <summary>
/// Controller for authentication and user information endpoints.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IConfiguration configuration,
        ILogger<AuthController> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Get information about the currently authenticated user.
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    public ActionResult<UserInfoDto> GetCurrentUser()
    {
        var username = User.Identity?.Name ?? "Unknown";
        var authMode = GetAuthenticationMode();

        // Extract roles from claims
        var roles = User.Claims
            .Where(c => c.Type == ClaimTypes.Role)
            .Select(c => c.Value)
            .Distinct()
            .ToList();

        // Determine highest role for display
        var highestRole = roles.Contains("Admin") ? "Admin"
            : roles.Contains("Operator") ? "Operator"
            : roles.Contains("Viewer") ? "Viewer"
            : "None";

        // Extract display name (part after domain\)
        var displayName = username.Contains('\\')
            ? username.Split('\\').Last()
            : username;

        var userInfo = new UserInfoDto(
            Username: username,
            DisplayName: displayName,
            Roles: roles,
            HighestRole: highestRole,
            IsAdmin: roles.Contains("Admin"),
            IsOperator: roles.Contains("Admin") || roles.Contains("Operator"),
            CanDeploy: roles.Contains("Admin") || roles.Contains("Operator"),
            CanManageConfigs: roles.Contains("Admin") || roles.Contains("Operator"),
            AuthenticationMode: authMode
        );

        _logger.LogDebug("User info requested for {User}: {Roles}", username, string.Join(", ", roles));

        return Ok(userInfo);
    }

    /// <summary>
    /// Validate an API key and return user info.
    /// Used by frontend to verify key before storing.
    /// </summary>
    [HttpPost("validate")]
    [AllowAnonymous]
    public ActionResult<AuthValidationResult> ValidateKey([FromBody] ValidateKeyRequest request)
    {
        var authMode = GetAuthenticationMode();

        if (!string.Equals(authMode, "ApiKey", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "API key authentication is not enabled" });
        }

        if (string.IsNullOrEmpty(request.ApiKey))
        {
            return BadRequest(new { message = "API key is required" });
        }

        // Look up the key in configuration
        var keys = _configuration.GetSection("Authentication:ApiKeys").Get<List<ApiKeyConfig>>() ?? new();

        _logger.LogInformation("Loaded {Count} API keys from configuration", keys.Count);
        foreach (var k in keys)
        {
            _logger.LogDebug("Configured key: Name={Name}, Role={Role}, KeyLength={Length}",
                k.Name, k.Role, k.Key?.Length ?? 0);
        }

        var keyConfig = keys.FirstOrDefault(k => string.Equals(k.Key, request.ApiKey, StringComparison.Ordinal));

        if (keyConfig == null)
        {
            _logger.LogWarning("Invalid API key validation attempt. Provided key length: {Length}", request.ApiKey?.Length ?? 0);
            return Unauthorized(new { message = "Invalid API key" });
        }

        _logger.LogInformation("API key validated for: {Name}", keyConfig.Name);

        return Ok(new AuthValidationResult
        {
            Valid = true,
            Name = keyConfig.Name,
            Role = keyConfig.Role
        });
    }

    /// <summary>
    /// Get authentication mode info (for login page routing).
    /// </summary>
    [HttpGet("mode")]
    [AllowAnonymous]
    public ActionResult<AuthModeInfo> GetAuthMode()
    {
        var authMode = GetAuthenticationMode();

        return Ok(new AuthModeInfo
        {
            Mode = authMode,
            RequiresLogin = !string.Equals(authMode, "Windows", StringComparison.OrdinalIgnoreCase)
        });
    }

    /// <summary>
    /// Gets the authentication mode, defaulting to Windows if not set or empty.
    /// </summary>
    private string GetAuthenticationMode()
    {
        var mode = _configuration["Authentication:Mode"];
        // Default to Windows auth on Windows, ApiKey otherwise
        if (string.IsNullOrEmpty(mode))
        {
            return OperatingSystem.IsWindows() ? "Windows" : "ApiKey";
        }
        return mode;
    }
}

// DTOs
/// <summary>
/// DTO containing information about the authenticated user.
/// </summary>
public record UserInfoDto(
    string Username,
    string DisplayName,
    List<string> Roles,
    string HighestRole,
    bool IsAdmin,
    bool IsOperator,
    bool CanDeploy,
    bool CanManageConfigs,
    string AuthenticationMode = "Windows");

public class ValidateKeyRequest
{
    public string ApiKey { get; set; } = "";
}

public class AuthValidationResult
{
    public bool Valid { get; set; }
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
}

public class AuthModeInfo
{
    public string Mode { get; set; } = "";
    public bool RequiresLogin { get; set; }
}
