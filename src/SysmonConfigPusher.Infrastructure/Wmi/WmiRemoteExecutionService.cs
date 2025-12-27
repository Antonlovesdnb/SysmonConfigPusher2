using System.Collections.Concurrent;
using System.Management;
using Microsoft.Extensions.Logging;
using SysmonConfigPusher.Core.Interfaces;

namespace SysmonConfigPusher.Infrastructure.Wmi;

public class WmiRemoteExecutionService : IRemoteExecutionService, IDisposable
{
    private readonly ILogger<WmiRemoteExecutionService> _logger;
    private readonly ConcurrentDictionary<string, CachedScope> _scopeCache = new();
    private readonly TimeSpan _cacheExpiry = TimeSpan.FromMinutes(5);
    private readonly Timer _cleanupTimer;
    private bool _disposed;

    private class CachedScope
    {
        public ManagementScope Scope { get; }
        public DateTime LastUsed { get; set; }

        public CachedScope(ManagementScope scope)
        {
            Scope = scope;
            LastUsed = DateTime.UtcNow;
        }
    }

    public WmiRemoteExecutionService(ILogger<WmiRemoteExecutionService> logger)
    {
        _logger = logger;
        _cleanupTimer = new Timer(CleanupExpiredScopes, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    private void CleanupExpiredScopes(object? state)
    {
        var expired = _scopeCache
            .Where(kvp => DateTime.UtcNow - kvp.Value.LastUsed > _cacheExpiry)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expired)
        {
            if (_scopeCache.TryRemove(key, out _))
            {
                _logger.LogDebug("Removed expired WMI scope for {Key}", key);
            }
        }
    }

    private ManagementScope GetOrCreateScope(string hostname, string path)
    {
        var cacheKey = $"{hostname}|{path}";

        if (_scopeCache.TryGetValue(cacheKey, out var cached))
        {
            if (cached.Scope.IsConnected)
            {
                cached.LastUsed = DateTime.UtcNow;
                return cached.Scope;
            }
            // Scope is disconnected, remove from cache
            _scopeCache.TryRemove(cacheKey, out _);
        }

        var scope = CreateManagementScope(hostname, path);
        scope.Connect();

        _scopeCache[cacheKey] = new CachedScope(scope);
        _logger.LogDebug("Created and cached WMI scope for {Hostname} at {Path}", hostname, path);

        return scope;
    }

    public async Task<RemoteExecutionResult> ExecuteCommandAsync(
        string hostname,
        string command,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                _logger.LogDebug("Executing command on {Hostname}: {Command}", hostname, command);

                var scope = GetOrCreateScope(hostname, @"root\cimv2");

                using var processClass = new ManagementClass(scope, new ManagementPath("Win32_Process"), null);
                var inParams = processClass.GetMethodParameters("Create");
                inParams["CommandLine"] = command;

                var outParams = processClass.InvokeMethod("Create", inParams, null);

                var returnValue = Convert.ToInt32(outParams["ReturnValue"]);
                var processId = returnValue == 0 ? Convert.ToInt32(outParams["ProcessId"]) : (int?)null;

                if (returnValue != 0)
                {
                    var errorMessage = GetWmiProcessCreateError(returnValue);
                    _logger.LogWarning("Command failed on {Hostname} with return value {ReturnValue}: {Error}",
                        hostname, returnValue, errorMessage);
                    return new RemoteExecutionResult(false, null, returnValue, errorMessage);
                }

                _logger.LogInformation("Command started on {Hostname}, PID: {ProcessId}", hostname, processId);
                return new RemoteExecutionResult(true, processId, returnValue, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute command on {Hostname}", hostname);
                return new RemoteExecutionResult(false, null, null, ex.Message);
            }
        }, cancellationToken);
    }

    public async Task<bool> TestConnectivityAsync(
        string hostname,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                _logger.LogDebug("Testing WMI connectivity to {Hostname}", hostname);

                var scope = GetOrCreateScope(hostname, @"root\cimv2");

                // Query the operating system to verify connectivity
                using var searcher = new ManagementObjectSearcher(scope,
                    new ObjectQuery("SELECT Caption FROM Win32_OperatingSystem"));

                using var results = searcher.Get();
                var connected = results.Count > 0;

                _logger.LogDebug("WMI connectivity to {Hostname}: {Connected}", hostname, connected);
                return connected;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "WMI connectivity test failed for {Hostname}", hostname);
                return false;
            }
        }, cancellationToken);
    }

    public async Task<string?> GetSysmonVersionAsync(
        string hostname,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                var scope = GetOrCreateScope(hostname, @"root\cimv2");

                // Query for Sysmon service
                using var searcher = new ManagementObjectSearcher(scope,
                    new ObjectQuery("SELECT PathName FROM Win32_Service WHERE Name = 'Sysmon' OR Name = 'Sysmon64'"));

                using var results = searcher.Get();

                foreach (ManagementObject service in results)
                {
                    var pathName = service["PathName"]?.ToString();
                    if (!string.IsNullOrEmpty(pathName))
                    {
                        // Extract the executable path and query its version
                        var exePath = ExtractExePath(pathName);
                        if (!string.IsNullOrEmpty(exePath))
                        {
                            return QueryFileVersion(scope, exePath);
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to get Sysmon version from {Hostname}", hostname);
                return null;
            }
        }, cancellationToken);
    }

    public async Task<string?> GetSysmonConfigHashAsync(
        string hostname,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                // Query the Sysmon registry for config hash
                // Sysmon stores config info in HKLM\SYSTEM\CurrentControlSet\Services\SysmonDrv\Parameters
                var regScope = GetOrCreateScope(hostname, @"root\default");

                using var registry = new ManagementClass(regScope, new ManagementPath("StdRegProv"), null);

                var inParams = registry.GetMethodParameters("GetStringValue");
                inParams["hDefKey"] = 0x80000002u; // HKEY_LOCAL_MACHINE
                inParams["sSubKeyName"] = @"SYSTEM\CurrentControlSet\Services\SysmonDrv\Parameters";
                inParams["sValueName"] = "HashingAlgorithm";

                var outParams = registry.InvokeMethod("GetStringValue", inParams, null);

                if (Convert.ToUInt32(outParams["ReturnValue"]) == 0)
                {
                    return outParams["sValue"]?.ToString();
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to get Sysmon config hash from {Hostname}", hostname);
                return null;
            }
        }, cancellationToken);
    }

    public async Task<string?> GetSysmonPathAsync(
        string hostname,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                var scope = GetOrCreateScope(hostname, @"root\cimv2");

                // Query for Sysmon or Sysmon64 service
                using var searcher = new ManagementObjectSearcher(scope,
                    new ObjectQuery("SELECT PathName FROM Win32_Service WHERE Name = 'Sysmon' OR Name = 'Sysmon64'"));

                using var results = searcher.Get();

                foreach (ManagementObject service in results)
                {
                    var pathName = service["PathName"]?.ToString();
                    if (!string.IsNullOrEmpty(pathName))
                    {
                        var exePath = ExtractExePath(pathName);
                        if (!string.IsNullOrEmpty(exePath))
                        {
                            _logger.LogDebug("Found Sysmon at {Path} on {Hostname}", exePath, hostname);
                            return exePath;
                        }
                    }
                }

                _logger.LogDebug("Sysmon service not found on {Hostname}", hostname);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to get Sysmon path from {Hostname}", hostname);
                return null;
            }
        }, cancellationToken);
    }

    private static ManagementScope CreateManagementScope(string hostname, string path)
    {
        var options = new ConnectionOptions
        {
            Impersonation = ImpersonationLevel.Impersonate,
            Authentication = AuthenticationLevel.PacketPrivacy,
            EnablePrivileges = true
        };

        return new ManagementScope($@"\\{hostname}\{path}", options);
    }

    private static string? ExtractExePath(string pathName)
    {
        // Handle quoted paths
        pathName = pathName.Trim();
        if (pathName.StartsWith('"'))
        {
            var endQuote = pathName.IndexOf('"', 1);
            if (endQuote > 0)
            {
                return pathName.Substring(1, endQuote - 1);
            }
        }

        // Handle unquoted paths - take until first space or end
        var spaceIdx = pathName.IndexOf(' ');
        return spaceIdx > 0 ? pathName[..spaceIdx] : pathName;
    }

    private string? QueryFileVersion(ManagementScope scope, string filePath)
    {
        try
        {
            var escapedPath = filePath.Replace(@"\", @"\\");
            using var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery($"SELECT Version FROM CIM_DataFile WHERE Name = '{escapedPath}'"));

            using var results = searcher.Get();

            foreach (ManagementObject file in results)
            {
                return file["Version"]?.ToString();
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to query file version for {FilePath}", filePath);
        }

        return null;
    }

    private static string GetWmiProcessCreateError(int returnValue)
    {
        return returnValue switch
        {
            2 => "Access denied",
            3 => "Insufficient privilege",
            8 => "Unknown failure",
            9 => "Path not found",
            21 => "Invalid parameter",
            _ => $"Unknown error ({returnValue})"
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cleanupTimer.Dispose();
        _scopeCache.Clear();

        GC.SuppressFinalize(this);
    }
}
