namespace SysmonConfigPusher.Agent.Configuration;

/// <summary>
/// Agent configuration loaded from agent.json
/// </summary>
public class AgentConfig
{
    /// <summary>Unique agent ID (generated on first run)</summary>
    public string AgentId { get; set; } = string.Empty;

    /// <summary>Server URL to connect to</summary>
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>Registration token for initial registration</summary>
    public string RegistrationToken { get; set; } = string.Empty;

    /// <summary>Auth token received after registration</summary>
    public string? AuthToken { get; set; }

    /// <summary>Poll interval in seconds</summary>
    public int PollIntervalSeconds { get; set; } = 30;

    /// <summary>Whether the agent has been registered</summary>
    public bool IsRegistered { get; set; }

    /// <summary>Optional tags for grouping</summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>TLS certificate thumbprint to pin (optional)</summary>
    public string? CertificateThumbprint { get; set; }

    /// <summary>Whether to validate server certificate</summary>
    public bool ValidateServerCertificate { get; set; } = true;
}
