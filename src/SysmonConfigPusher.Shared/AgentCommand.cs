namespace SysmonConfigPusher.Shared;

using System.Text.Json.Serialization;

/// <summary>
/// Types of commands the server can send to the agent
/// </summary>
public enum AgentCommandType
{
    /// <summary>Request agent status and Sysmon info</summary>
    GetStatus,

    /// <summary>Install Sysmon with provided binary and config</summary>
    InstallSysmon,

    /// <summary>Update Sysmon configuration</summary>
    UpdateConfig,

    /// <summary>Uninstall Sysmon</summary>
    UninstallSysmon,

    /// <summary>Query Sysmon event logs</summary>
    QueryEvents,

    /// <summary>Restart Sysmon service</summary>
    RestartSysmon
}

/// <summary>
/// Base command sent from server to agent
/// </summary>
public class AgentCommand
{
    /// <summary>Unique command ID for correlation</summary>
    public string CommandId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Type of command to execute</summary>
    public AgentCommandType Type { get; set; }

    /// <summary>Timestamp when command was issued</summary>
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Optional payload data (JSON serialized)</summary>
    public string? Payload { get; set; }
}

/// <summary>
/// Payload for InstallSysmon command
/// </summary>
public class InstallSysmonPayload
{
    /// <summary>Base64-encoded Sysmon executable</summary>
    public required string SysmonBinaryBase64 { get; set; }

    /// <summary>Sysmon configuration XML content (optional - Sysmon can be installed without config)</summary>
    public string? ConfigXml { get; set; }

    /// <summary>Whether to use 64-bit Sysmon</summary>
    public bool Use64Bit { get; set; } = true;

    /// <summary>Expected hash of config for verification</summary>
    public string? ConfigHash { get; set; }
}

/// <summary>
/// Payload for UpdateConfig command
/// </summary>
public class UpdateConfigPayload
{
    /// <summary>Sysmon configuration XML content</summary>
    public required string ConfigXml { get; set; }

    /// <summary>Expected hash of config for verification</summary>
    public string? ConfigHash { get; set; }
}

/// <summary>
/// Payload for QueryEvents command
/// </summary>
public class QueryEventsPayload
{
    /// <summary>How far back to query (in hours)</summary>
    public int TimeRangeHours { get; set; } = 24;

    /// <summary>Maximum number of events to return</summary>
    public int MaxEvents { get; set; } = 1000;

    /// <summary>Optional filter by event IDs</summary>
    public int[]? EventIds { get; set; }

    /// <summary>Optional XPath filter</summary>
    public string? XPathFilter { get; set; }
}
