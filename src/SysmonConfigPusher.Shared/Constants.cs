namespace SysmonConfigPusher.Shared;

/// <summary>
/// Shared constants for agent-server communication
/// </summary>
public static class AgentConstants
{
    /// <summary>Current agent protocol version</summary>
    public const string ProtocolVersion = "1.0";

    /// <summary>Agent service name</summary>
    public const string ServiceName = "SysmonConfigPusherAgent";

    /// <summary>Agent service display name</summary>
    public const string ServiceDisplayName = "Sysmon Config Pusher Agent";

    /// <summary>Default poll interval in seconds</summary>
    public const int DefaultPollIntervalSeconds = 30;

    /// <summary>Minimum poll interval in seconds</summary>
    public const int MinPollIntervalSeconds = 10;

    /// <summary>Maximum poll interval in seconds</summary>
    public const int MaxPollIntervalSeconds = 300;

    /// <summary>Default installation directory</summary>
    public const string DefaultInstallPath = @"C:\Program Files\SysmonConfigPusher\Agent";

    /// <summary>Config file name</summary>
    public const string ConfigFileName = "agent.json";

    /// <summary>Sysmon files directory relative to install path</summary>
    public const string SysmonFilesDirectory = "SysmonFiles";

    /// <summary>API endpoints</summary>
    public static class Endpoints
    {
        public const string Register = "/api/agent/register";
        public const string Heartbeat = "/api/agent/heartbeat";
        public const string CommandResult = "/api/agent/command-result";
    }

    /// <summary>HTTP headers</summary>
    public static class Headers
    {
        public const string AgentId = "X-Agent-Id";
        public const string AgentVersion = "X-Agent-Version";
        public const string AuthToken = "X-Agent-Auth";
    }

    /// <summary>Whitelisted commands the agent can execute (security measure)</summary>
    public static readonly HashSet<string> AllowedExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        "Sysmon.exe",
        "Sysmon64.exe"
    };

    /// <summary>Whitelisted Sysmon command-line arguments</summary>
    public static readonly HashSet<string> AllowedSysmonArgs = new(StringComparer.OrdinalIgnoreCase)
    {
        "-accepteula",
        "-i",
        "-c",
        "-u",
        "-h"
    };
}
