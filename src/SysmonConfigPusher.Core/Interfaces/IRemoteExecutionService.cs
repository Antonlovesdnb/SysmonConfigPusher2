namespace SysmonConfigPusher.Core.Interfaces;

public interface IRemoteExecutionService
{
    /// <summary>
    /// Indicates if this service is available in the current deployment mode.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Executes a command on a remote host using WMI Win32_Process.Create.
    /// Fire-and-forget - returns the process ID if started successfully.
    /// </summary>
    Task<RemoteExecutionResult> ExecuteCommandAsync(
        string hostname,
        string command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests connectivity to a remote host via WMI.
    /// </summary>
    Task<bool> TestConnectivityAsync(
        string hostname,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the Sysmon version installed on a remote host by querying the registry or service.
    /// </summary>
    Task<string?> GetSysmonVersionAsync(
        string hostname,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current Sysmon config hash from a remote host.
    /// </summary>
    Task<string?> GetSysmonConfigHashAsync(
        string hostname,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the path to the Sysmon executable on a remote host by querying the service.
    /// Returns null if Sysmon is not installed.
    /// </summary>
    Task<string?> GetSysmonPathAsync(
        string hostname,
        CancellationToken cancellationToken = default);
}

public record RemoteExecutionResult(
    bool Success,
    int? ProcessId,
    int? ReturnValue,
    string? ErrorMessage);
