namespace SysmonConfigPusher.Shared;

/// <summary>
/// Agent registration request sent when agent first connects
/// </summary>
public class AgentRegistrationRequest
{
    /// <summary>Unique agent ID (generated on first run, persisted)</summary>
    public required string AgentId { get; set; }

    /// <summary>Hostname of the machine</summary>
    public required string Hostname { get; set; }

    /// <summary>Agent version</summary>
    public required string AgentVersion { get; set; }

    /// <summary>Operating system info</summary>
    public string? OperatingSystem { get; set; }

    /// <summary>Whether the machine is 64-bit</summary>
    public bool Is64Bit { get; set; }

    /// <summary>Registration token for authentication</summary>
    public required string RegistrationToken { get; set; }

    /// <summary>Optional tags/labels for grouping</summary>
    public List<string> Tags { get; set; } = new();
}

/// <summary>
/// Server response to agent registration
/// </summary>
public class AgentRegistrationResponse
{
    /// <summary>Whether registration was accepted</summary>
    public bool Accepted { get; set; }

    /// <summary>Message explaining rejection if not accepted</summary>
    public string? Message { get; set; }

    /// <summary>Assigned computer ID in the database</summary>
    public int? ComputerId { get; set; }

    /// <summary>How often agent should poll for commands (seconds)</summary>
    public int PollIntervalSeconds { get; set; } = 30;

    /// <summary>Server-issued auth token for subsequent requests</summary>
    public string? AuthToken { get; set; }
}

/// <summary>
/// Heartbeat sent periodically by agent
/// </summary>
public class AgentHeartbeat
{
    /// <summary>Agent ID</summary>
    public required string AgentId { get; set; }

    /// <summary>Current agent status</summary>
    public required AgentStatusPayload Status { get; set; }

    /// <summary>Timestamp</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Server response to heartbeat, may include pending commands
/// </summary>
public class HeartbeatResponse
{
    /// <summary>Whether the agent is still registered</summary>
    public bool Registered { get; set; } = true;

    /// <summary>Pending commands to execute</summary>
    public List<AgentCommand> PendingCommands { get; set; } = new();

    /// <summary>Updated poll interval if changed</summary>
    public int? NewPollIntervalSeconds { get; set; }
}
