namespace SysmonConfigPusher.Shared;

/// <summary>
/// Result status of a command execution
/// </summary>
public enum CommandResultStatus
{
    Success,
    Failed,
    PartialSuccess,
    Timeout,
    Unauthorized
}

/// <summary>
/// Base response from agent to server
/// </summary>
public class AgentResponse
{
    /// <summary>Command ID this is responding to</summary>
    public required string CommandId { get; set; }

    /// <summary>Result status</summary>
    public CommandResultStatus Status { get; set; }

    /// <summary>Human-readable message</summary>
    public string? Message { get; set; }

    /// <summary>Timestamp when response was generated</summary>
    public DateTime RespondedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Optional payload data (JSON serialized)</summary>
    public string? Payload { get; set; }
}

/// <summary>
/// Status information about the agent and Sysmon
/// </summary>
public class AgentStatusPayload
{
    /// <summary>Agent version</summary>
    public required string AgentVersion { get; set; }

    /// <summary>Hostname of the machine</summary>
    public required string Hostname { get; set; }

    /// <summary>Whether Sysmon is installed</summary>
    public bool SysmonInstalled { get; set; }

    /// <summary>Sysmon version if installed</summary>
    public string? SysmonVersion { get; set; }

    /// <summary>Path to Sysmon executable</summary>
    public string? SysmonPath { get; set; }

    /// <summary>SHA256 hash of current config</summary>
    public string? ConfigHash { get; set; }

    /// <summary>Sysmon service status</summary>
    public string? ServiceStatus { get; set; }

    /// <summary>Sysmon driver status</summary>
    public string? DriverStatus { get; set; }

    /// <summary>Last config update time</summary>
    public DateTime? LastConfigUpdate { get; set; }

    /// <summary>Operating system info</summary>
    public string? OperatingSystem { get; set; }

    /// <summary>Whether the machine is 64-bit</summary>
    public bool Is64Bit { get; set; }

    /// <summary>Agent uptime in seconds</summary>
    public long UptimeSeconds { get; set; }
}

/// <summary>
/// Response payload for event queries
/// </summary>
public class QueryEventsResultPayload
{
    /// <summary>Total events matching the query</summary>
    public int TotalCount { get; set; }

    /// <summary>Number of events returned (may be limited)</summary>
    public int ReturnedCount { get; set; }

    /// <summary>The events</summary>
    public List<SysmonEventDto> Events { get; set; } = new();
}

/// <summary>
/// A single Sysmon event
/// </summary>
public class SysmonEventDto
{
    /// <summary>Event ID (1=ProcessCreate, 3=NetworkConnect, etc.)</summary>
    public int EventId { get; set; }

    /// <summary>Event type name</summary>
    public string? EventType { get; set; }

    /// <summary>Event timestamp</summary>
    public DateTime TimeCreated { get; set; }

    /// <summary>Raw XML of the event</summary>
    public string? RawXml { get; set; }

    /// <summary>Parsed event data as key-value pairs</summary>
    public Dictionary<string, string> EventData { get; set; } = new();

    // Flat properties for common fields (populated from EventData)
    public string? ProcessName { get; set; }
    public int? ProcessId { get; set; }
    public string? Image { get; set; }
    public string? CommandLine { get; set; }
    public string? User { get; set; }
    public string? ParentProcessName { get; set; }
    public int? ParentProcessId { get; set; }
    public string? ParentImage { get; set; }
    public string? ParentCommandLine { get; set; }
    public string? DestinationIp { get; set; }
    public int? DestinationPort { get; set; }
    public string? DestinationHostname { get; set; }
    public string? SourceIp { get; set; }
    public int? SourcePort { get; set; }
    public string? Protocol { get; set; }
    public string? TargetFilename { get; set; }
    public string? QueryName { get; set; }
    public string? QueryResults { get; set; }
    public string? ImageLoaded { get; set; }
    public string? Signature { get; set; }

    // ProcessAccess-specific fields (Event ID 10)
    public string? SourceImage { get; set; }
    public string? TargetImage { get; set; }
    public string? GrantedAccess { get; set; }
    public string? CallTrace { get; set; }
}
