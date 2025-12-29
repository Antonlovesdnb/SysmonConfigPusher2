using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SysmonConfigPusher.Service.Controllers;

/// <summary>
/// Controller for authentication and user information endpoints.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AuthController : ControllerBase
{
    private readonly ILogger<AuthController> _logger;

    public AuthController(ILogger<AuthController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Get information about the currently authenticated user.
    /// </summary>
    [HttpGet("me")]
    public ActionResult<UserInfoDto> GetCurrentUser()
    {
        var username = User.Identity?.Name ?? "Unknown";

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
            CanManageConfigs: roles.Contains("Admin") || roles.Contains("Operator")
        );

        _logger.LogDebug("User info requested for {User}: {Roles}", username, string.Join(", ", roles));

        return Ok(userInfo);
    }
}

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
    bool CanManageConfigs);
