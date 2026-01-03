using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace SysmonConfigPusher.Service.Authentication;

/// <summary>
/// Authentication handler for API key-based authentication.
/// Used for Docker/AgentOnly deployments where Windows Auth is not available.
/// </summary>
public class ApiKeyAuthHandler : AuthenticationHandler<ApiKeyAuthOptions>
{
    public ApiKeyAuthHandler(
        IOptionsMonitor<ApiKeyAuthOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Check for API key header
        if (!Request.Headers.TryGetValue(Options.HeaderName, out var apiKeyHeader))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var apiKey = apiKeyHeader.FirstOrDefault();
        if (string.IsNullOrEmpty(apiKey))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        // Find matching key configuration
        var keyConfig = Options.Keys.FirstOrDefault(k =>
            string.Equals(k.Key, apiKey, StringComparison.Ordinal));

        if (keyConfig == null)
        {
            Logger.LogWarning("Invalid API key attempted");
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key"));
        }

        // Validate role
        var validRoles = new[] { "Admin", "Operator", "Viewer" };
        var role = validRoles.Contains(keyConfig.Role) ? keyConfig.Role : "Viewer";

        // Create claims principal
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, keyConfig.Name),
            new Claim(ClaimTypes.NameIdentifier, keyConfig.Name),
            new Claim(ClaimTypes.Role, role),
            new Claim("auth_method", "ApiKey")
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        Logger.LogDebug("API key authenticated: {Name} with role {Role}", keyConfig.Name, role);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401;
        Response.Headers.Append("WWW-Authenticate", $"ApiKey realm=\"SysmonConfigPusher\", header=\"{Options.HeaderName}\"");
        return Task.CompletedTask;
    }
}
