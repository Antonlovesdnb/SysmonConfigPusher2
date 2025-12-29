using System.Security.Claims;
using System.Security.Principal;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using SysmonConfigPusher.Core.Configuration;

namespace SysmonConfigPusher.Service.Authorization;

/// <summary>
/// Transforms Windows authentication claims by adding application role claims
/// based on Active Directory security group membership.
/// </summary>
public class AdGroupClaimsTransformation : IClaimsTransformation
{
    private readonly AuthorizationSettings _settings;
    private readonly ILogger<AdGroupClaimsTransformation> _logger;

    public AdGroupClaimsTransformation(
        IOptions<AuthorizationSettings> settings,
        ILogger<AdGroupClaimsTransformation> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        // Skip if already transformed (claims transformation can be called multiple times)
        if (principal.HasClaim(c => c.Type == ClaimTypes.Role &&
            (c.Value == "Admin" || c.Value == "Operator" || c.Value == "Viewer")))
        {
            return Task.FromResult(principal);
        }

        var identity = principal.Identity as ClaimsIdentity;
        if (identity == null || !identity.IsAuthenticated)
        {
            return Task.FromResult(principal);
        }

        var username = principal.Identity?.Name ?? "Unknown";
        var roleClaims = new List<Claim>();
        var rolesAdded = new List<string>();

        // Check Windows group membership for each configured role
        if (IsInGroup(principal, _settings.AdminGroup))
        {
            roleClaims.Add(new Claim(ClaimTypes.Role, "Admin"));
            rolesAdded.Add("Admin");
        }

        if (IsInGroup(principal, _settings.OperatorGroup))
        {
            roleClaims.Add(new Claim(ClaimTypes.Role, "Operator"));
            rolesAdded.Add("Operator");
        }

        if (IsInGroup(principal, _settings.ViewerGroup))
        {
            roleClaims.Add(new Claim(ClaimTypes.Role, "Viewer"));
            rolesAdded.Add("Viewer");
        }

        // If user is not in any configured group, apply default role
        if (rolesAdded.Count == 0 && !string.IsNullOrEmpty(_settings.DefaultRole))
        {
            roleClaims.Add(new Claim(ClaimTypes.Role, _settings.DefaultRole));
            rolesAdded.Add($"{_settings.DefaultRole} (default)");
        }

        if (rolesAdded.Count > 0)
            _logger.LogInformation("User {User} assigned roles: {Roles}", username, string.Join(", ", rolesAdded));
        else
            _logger.LogWarning("User {User} has NO roles assigned - will be denied access", username);

        // Create a new identity with proper RoleClaimType so User.IsInRole() works
        // Windows auth identity may have a different RoleClaimType that doesn't match ClaimTypes.Role
        var appIdentity = new ClaimsIdentity(roleClaims, "ApplicationRoles", ClaimTypes.Name, ClaimTypes.Role);
        principal.AddIdentity(appIdentity);

        return Task.FromResult(principal);
    }

    private bool IsInGroup(ClaimsPrincipal principal, string groupName)
    {
        if (string.IsNullOrEmpty(groupName))
            return false;

        var username = principal.Identity?.Name ?? "Unknown";

        // Check if user is in group using Windows identity
        if (principal.Identity is WindowsIdentity windowsIdentity)
        {
            try
            {
                var windowsPrincipal = new WindowsPrincipal(windowsIdentity);
                var isInRole = windowsPrincipal.IsInRole(groupName);
                _logger.LogInformation("User {User}: IsInRole({Group}) = {Result}", username, groupName, isInRole);
                return isInRole;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check group membership for {Group}", groupName);
            }
        }
        else
        {
            _logger.LogWarning("User {User}: Identity is not WindowsIdentity, type is {Type}",
                username, principal.Identity?.GetType().Name ?? "null");
        }

        // Fallback: check group claims (some auth schemes add groups as claims)
        // Windows auth typically adds group SIDs, so we check for group name in claims
        var groupClaim = principal.Claims.FirstOrDefault(c =>
            (c.Type == ClaimTypes.GroupSid || c.Type == "groups" || c.Type == ClaimTypes.Role) &&
            c.Value.Equals(groupName, StringComparison.OrdinalIgnoreCase));

        return groupClaim != null;
    }
}
