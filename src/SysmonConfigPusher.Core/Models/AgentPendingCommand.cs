namespace SysmonConfigPusher.Core.Models;

/// <summary>
/// Represents a pending command to be sent to an agent
/// </summary>
public class AgentPendingCommand
{
    public int Id { get; set; }

    /// <summary>
    /// Computer ID this command is for
    /// </summary>
    public int ComputerId { get; set; }

    /// <summary>
    /// Unique command ID
    /// </summary>
    public required string CommandId { get; set; }

    /// <summary>
    /// Command type (GetStatus, InstallSysmon, UpdateConfig, etc.)
    /// </summary>
    public required string CommandType { get; set; }

    /// <summary>
    /// JSON payload for the command
    /// </summary>
    public string? Payload { get; set; }

    /// <summary>
    /// When the command was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the command was sent to the agent
    /// </summary>
    public DateTime? SentAt { get; set; }

    /// <summary>
    /// When the result was received
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Result status (Success, Failed, etc.)
    /// </summary>
    public string? ResultStatus { get; set; }

    /// <summary>
    /// Result message
    /// </summary>
    public string? ResultMessage { get; set; }

    /// <summary>
    /// Result payload (JSON)
    /// </summary>
    public string? ResultPayload { get; set; }

    /// <summary>
    /// User who initiated this command
    /// </summary>
    public string? InitiatedBy { get; set; }

    /// <summary>
    /// Associated deployment job ID (if any)
    /// </summary>
    public int? DeploymentJobId { get; set; }

    public Computer? Computer { get; set; }
    public DeploymentJob? DeploymentJob { get; set; }
}
