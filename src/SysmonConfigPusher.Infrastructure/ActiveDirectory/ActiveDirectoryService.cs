using System.DirectoryServices;
using Microsoft.Extensions.Logging;
using SysmonConfigPusher.Core.Interfaces;

namespace SysmonConfigPusher.Infrastructure.ActiveDirectory;

public class ActiveDirectoryService : IActiveDirectoryService
{
    private readonly ILogger<ActiveDirectoryService> _logger;

    public ActiveDirectoryService(ILogger<ActiveDirectoryService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsAvailable => true;

    public async Task<IEnumerable<AdComputer>> EnumerateComputersAsync(
        string? searchBase = null,
        string? filter = null,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var computers = new List<AdComputer>();

            try
            {
                using var entry = searchBase != null
                    ? new DirectoryEntry($"LDAP://{searchBase}")
                    : new DirectoryEntry();

                using var searcher = new DirectorySearcher(entry)
                {
                    // Use objectCategory=computer to exclude service accounts (gMSA, etc.)
                    Filter = string.IsNullOrWhiteSpace(filter)
                        ? "(objectCategory=computer)"
                        : $"(&(objectCategory=computer){filter})",
                    PageSize = 500,
                    SizeLimit = 0 // No limit
                };

                searcher.PropertiesToLoad.AddRange([
                    "cn",
                    "distinguishedName",
                    "operatingSystem",
                    "operatingSystemVersion",
                    "lastLogonTimestamp",
                    "userAccountControl"
                ]);

                _logger.LogInformation("Enumerating computers from AD with filter: {Filter}", searcher.Filter);

                using var results = searcher.FindAll();

                foreach (SearchResult result in results)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    var computer = MapToAdComputer(result);
                    if (computer != null)
                    {
                        computers.Add(computer);
                    }
                }

                _logger.LogInformation("Found {Count} computers in Active Directory", computers.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enumerate computers from Active Directory");
                throw;
            }

            return computers;
        }, cancellationToken);
    }

    public async Task<AdComputer?> GetComputerAsync(string hostname, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var entry = new DirectoryEntry();
                using var searcher = new DirectorySearcher(entry)
                {
                    Filter = $"(&(objectCategory=computer)(cn={EscapeLdapFilter(hostname)}))"
                };

                searcher.PropertiesToLoad.AddRange([
                    "cn",
                    "distinguishedName",
                    "operatingSystem",
                    "operatingSystemVersion",
                    "lastLogonTimestamp",
                    "userAccountControl"
                ]);

                var result = searcher.FindOne();
                return result != null ? MapToAdComputer(result) : null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get computer {Hostname} from Active Directory", hostname);
                throw;
            }
        }, cancellationToken);
    }

    private static AdComputer? MapToAdComputer(SearchResult result)
    {
        var cn = GetProperty<string>(result, "cn");
        if (string.IsNullOrEmpty(cn))
            return null;

        var uac = GetProperty<int>(result, "userAccountControl");
        var disabled = (uac & 0x0002) != 0; // ACCOUNTDISABLE flag

        var lastLogonTimestamp = GetProperty<long>(result, "lastLogonTimestamp");
        DateTime? lastLogon = lastLogonTimestamp > 0
            ? DateTime.FromFileTimeUtc(lastLogonTimestamp)
            : null;

        return new AdComputer(
            Hostname: cn,
            DistinguishedName: GetProperty<string>(result, "distinguishedName"),
            OperatingSystem: GetProperty<string>(result, "operatingSystem"),
            OperatingSystemVersion: GetProperty<string>(result, "operatingSystemVersion"),
            LastLogon: lastLogon,
            Enabled: !disabled);
    }

    private static T? GetProperty<T>(SearchResult result, string propertyName)
    {
        if (result.Properties.Contains(propertyName) && result.Properties[propertyName].Count > 0)
        {
            var value = result.Properties[propertyName][0];
            if (value is T typedValue)
                return typedValue;
        }
        return default;
    }

    private static string EscapeLdapFilter(string value)
    {
        return value
            .Replace("\\", "\\5c")
            .Replace("*", "\\2a")
            .Replace("(", "\\28")
            .Replace(")", "\\29")
            .Replace("\0", "\\00");
    }
}
