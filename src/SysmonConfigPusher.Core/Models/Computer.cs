namespace SysmonConfigPusher.Core.Models;

public class Computer
{
    public int Id { get; set; }
    public required string Hostname { get; set; }
    public string? DistinguishedName { get; set; }
    public string? OperatingSystem { get; set; }
    public DateTime? LastSeen { get; set; }
    public string? SysmonVersion { get; set; }
    public string? SysmonPath { get; set; }
    public string? ConfigHash { get; set; }
    public string? ConfigTag { get; set; }
    public DateTime? LastDeployment { get; set; }
    public DateTime? LastInventoryScan { get; set; }

    /// <summary>
    /// Status of the last inventory scan: "Online", "Offline", or null if never scanned
    /// </summary>
    public string? LastScanStatus { get; set; }

    // Agent-related properties
    /// <summary>
    /// Whether this computer is managed via an agent (true) or WMI/SMB (false)
    /// </summary>
    public bool IsAgentManaged { get; set; }

    /// <summary>
    /// Unique agent ID for agent-managed computers
    /// </summary>
    public string? AgentId { get; set; }

    /// <summary>
    /// Agent version for agent-managed computers
    /// </summary>
    public string? AgentVersion { get; set; }

    /// <summary>
    /// Last heartbeat received from agent
    /// </summary>
    public DateTime? AgentLastHeartbeat { get; set; }

    /// <summary>
    /// Agent authentication token
    /// </summary>
    public string? AgentAuthToken { get; set; }

    /// <summary>
    /// Comma-separated tags for agent-managed computers
    /// </summary>
    public string? AgentTags { get; set; }

    public ICollection<ComputerGroupMember> GroupMemberships { get; set; } = [];
    public ICollection<DeploymentResult> DeploymentResults { get; set; } = [];
    public ICollection<AgentPendingCommand> PendingCommands { get; set; } = [];
}
