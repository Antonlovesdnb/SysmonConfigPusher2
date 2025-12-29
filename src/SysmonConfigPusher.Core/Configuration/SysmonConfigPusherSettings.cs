namespace SysmonConfigPusher.Core.Configuration;

/// <summary>
/// Configuration settings for Sysmon deployment operations.
/// </summary>
public class SysmonConfigPusherSettings
{
    public const string SectionName = "SysmonConfigPusher";

    /// <summary>
    /// URL to download Sysmon binary from (default: live.sysinternals.com).
    /// </summary>
    public string SysmonBinaryUrl { get; set; } = "https://live.sysinternals.com/Sysmon.exe";

    /// <summary>
    /// Default parallelism for deployment operations (1-500).
    /// </summary>
    public int DefaultParallelism { get; set; } = 50;

    /// <summary>
    /// Remote directory on target hosts for Sysmon files.
    /// </summary>
    public string RemoteDirectory { get; set; } = @"C:\SysmonFiles";
}
