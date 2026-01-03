using Microsoft.AspNetCore.Authentication;

namespace SysmonConfigPusher.Service.Authentication;

/// <summary>
/// Options for API Key authentication.
/// </summary>
public class ApiKeyAuthOptions : AuthenticationSchemeOptions
{
    public const string DefaultScheme = "ApiKey";
    public const string DefaultHeaderName = "X-Api-Key";

    /// <summary>
    /// The header name to look for the API key. Default: X-Api-Key
    /// </summary>
    public string HeaderName { get; set; } = DefaultHeaderName;

    /// <summary>
    /// List of valid API keys with their associated roles.
    /// </summary>
    public List<ApiKeyConfig> Keys { get; set; } = new();
}

/// <summary>
/// Configuration for a single API key.
/// </summary>
public class ApiKeyConfig
{
    /// <summary>
    /// The API key value (should be a secure random string).
    /// </summary>
    public string Key { get; set; } = "";

    /// <summary>
    /// Display name for this key (for audit logging).
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Role assigned to this key: Admin, Operator, or Viewer.
    /// </summary>
    public string Role { get; set; } = "Viewer";
}
