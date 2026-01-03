namespace SysmonConfigPusher.Core.Interfaces;

public interface IActiveDirectoryService
{
    /// <summary>
    /// Indicates if this service is available in the current deployment mode.
    /// </summary>
    bool IsAvailable { get; }

    Task<IEnumerable<AdComputer>> EnumerateComputersAsync(string? searchBase = null, string? filter = null, CancellationToken cancellationToken = default);
    Task<AdComputer?> GetComputerAsync(string hostname, CancellationToken cancellationToken = default);
}

public record AdComputer(
    string Hostname,
    string? DistinguishedName,
    string? OperatingSystem,
    string? OperatingSystemVersion,
    DateTime? LastLogon,
    bool Enabled);
