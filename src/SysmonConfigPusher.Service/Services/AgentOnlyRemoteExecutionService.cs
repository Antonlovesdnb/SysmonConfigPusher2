using SysmonConfigPusher.Core.Interfaces;

namespace SysmonConfigPusher.Service.Services;

/// <summary>
/// Remote execution service for AgentOnly mode.
/// Returns not-available for WMI operations - computers must use agents.
/// </summary>
public class AgentOnlyRemoteExecutionService : IRemoteExecutionService
{
    private readonly ILogger<AgentOnlyRemoteExecutionService> _logger;
    private const string NotAvailableMessage = "WMI remote execution is not available in AgentOnly mode. This computer must be managed via an agent.";

    public AgentOnlyRemoteExecutionService(ILogger<AgentOnlyRemoteExecutionService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsAvailable => false;

    public Task<RemoteExecutionResult> ExecuteCommandAsync(
        string hostname,
        string command,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("WMI execution attempted on {Hostname} but not available in AgentOnly mode", hostname);
        return Task.FromResult(new RemoteExecutionResult(
            Success: false,
            ProcessId: null,
            ReturnValue: null,
            ErrorMessage: NotAvailableMessage));
    }

    public Task<bool> TestConnectivityAsync(
        string hostname,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("WMI connectivity test not available in AgentOnly mode for {Hostname}", hostname);
        return Task.FromResult(false);
    }

    public Task<string?> GetSysmonVersionAsync(
        string hostname,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("WMI Sysmon version query not available in AgentOnly mode for {Hostname}", hostname);
        return Task.FromResult<string?>(null);
    }

    public Task<string?> GetSysmonConfigHashAsync(
        string hostname,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("WMI Sysmon config hash query not available in AgentOnly mode for {Hostname}", hostname);
        return Task.FromResult<string?>(null);
    }

    public Task<string?> GetSysmonPathAsync(
        string hostname,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("WMI Sysmon path query not available in AgentOnly mode for {Hostname}", hostname);
        return Task.FromResult<string?>(null);
    }
}
