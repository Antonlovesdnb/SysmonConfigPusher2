using SysmonConfigPusher.Core.Interfaces;

namespace SysmonConfigPusher.Service.Services;

/// <summary>
/// Null implementation of IActiveDirectoryService for AgentOnly mode.
/// Returns empty results since AD browsing is not available.
/// </summary>
public class NullActiveDirectoryService : IActiveDirectoryService
{
    private readonly ILogger<NullActiveDirectoryService> _logger;

    public NullActiveDirectoryService(ILogger<NullActiveDirectoryService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsAvailable => false;

    public Task<IEnumerable<AdComputer>> EnumerateComputersAsync(
        string? searchBase = null,
        string? filter = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("AD enumeration not available in AgentOnly mode");
        return Task.FromResult<IEnumerable<AdComputer>>(Array.Empty<AdComputer>());
    }

    public Task<AdComputer?> GetComputerAsync(string hostname, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("AD lookup not available in AgentOnly mode for {Hostname}", hostname);
        return Task.FromResult<AdComputer?>(null);
    }
}
