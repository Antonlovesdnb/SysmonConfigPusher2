namespace SysmonConfigPusher.Core.Configuration;

/// <summary>
/// Configuration settings for AD group to application role mapping.
/// </summary>
public class AuthorizationSettings
{
    public const string SectionName = "Authorization";

    /// <summary>
    /// AD security group name for Admin role (full access).
    /// </summary>
    public string AdminGroup { get; set; } = "SysmonPusher-Admins";

    /// <summary>
    /// AD security group name for Operator role (deploy, manage configs).
    /// </summary>
    public string OperatorGroup { get; set; } = "SysmonPusher-Operators";

    /// <summary>
    /// AD security group name for Viewer role (read-only access).
    /// </summary>
    public string ViewerGroup { get; set; } = "SysmonPusher-Viewers";

    /// <summary>
    /// Default role assigned to authenticated users not in any configured group.
    /// Set to empty string to deny access to users not in any group.
    /// </summary>
    public string DefaultRole { get; set; } = "Viewer";
}
