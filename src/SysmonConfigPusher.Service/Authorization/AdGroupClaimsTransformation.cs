using System.Collections.Concurrent;
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

    // Cache user roles to avoid repeated AD lookups (5 minute TTL)
    private static readonly ConcurrentDictionary<string, CachedRoles> _roleCache = new();
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private record CachedRoles(List<string> Roles, DateTime CachedAt);

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
        var roles = GetCachedRoles(principal, username);

        var roleClaims = roles.Select(r => new Claim(ClaimTypes.Role, r)).ToList();

        // Create a new identity with proper RoleClaimType so User.IsInRole() works
        var appIdentity = new ClaimsIdentity(roleClaims, "ApplicationRoles", ClaimTypes.Name, ClaimTypes.Role);
        principal.AddIdentity(appIdentity);

        return Task.FromResult(principal);
    }

    private List<string> GetCachedRoles(ClaimsPrincipal principal, string username)
    {
        // Check cache first
        if (_roleCache.TryGetValue(username, out var cached) &&
            DateTime.UtcNow - cached.CachedAt < CacheDuration)
        {
            _logger.LogDebug("Using cached roles for {User}: {Roles}", username, string.Join(", ", cached.Roles));
            return cached.Roles;
        }

        // Not in cache or expired - do AD lookups
        var roles = new List<string>();

        if (IsInGroup(principal, _settings.AdminGroup))
            roles.Add("Admin");

        if (IsInGroup(principal, _settings.OperatorGroup))
            roles.Add("Operator");

        if (IsInGroup(principal, _settings.ViewerGroup))
            roles.Add("Viewer");

        // If user is not in any configured group, apply default role
        if (roles.Count == 0 && !string.IsNullOrEmpty(_settings.DefaultRole))
        {
            roles.Add(_settings.DefaultRole);
            _logger.LogInformation("User {User} assigned default role: {Role}", username, _settings.DefaultRole);
        }
        else if (roles.Count > 0)
        {
            _logger.LogInformation("User {User} assigned roles: {Roles}", username, string.Join(", ", roles));
        }
        else
        {
            _logger.LogWarning("User {User} has NO roles assigned - will be denied access", username);
        }

        // Cache the result
        _roleCache[username] = new CachedRoles(roles, DateTime.UtcNow);

        return roles;
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
