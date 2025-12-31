using System.Management;
using Microsoft.Extensions.Logging;
using SysmonConfigPusher.Core.Interfaces;

namespace SysmonConfigPusher.Infrastructure.Wmi;

public class WmiRemoteExecutionService : IRemoteExecutionService
{
    private readonly ILogger<WmiRemoteExecutionService> _logger;

    public WmiRemoteExecutionService(ILogger<WmiRemoteExecutionService> logger)
    {
        _logger = logger;
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

                var scope = CreateManagementScope(hostname, @"root\cimv2");
                scope.Connect();

                // Create ProcessStartup object to hide the window
                using var startupClass = new ManagementClass(scope, new ManagementPath("Win32_ProcessStartup"), null);
                using var startupInstance = startupClass.CreateInstance();
                startupInstance["ShowWindow"] = 0; // SW_HIDE - window is hidden

                using var processClass = new ManagementClass(scope, new ManagementPath("Win32_Process"), null);
                var inParams = processClass.GetMethodParameters("Create");
                inParams["CommandLine"] = command;
                inParams["ProcessStartupInformation"] = startupInstance;

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

                var scope = CreateManagementScope(hostname, @"root\cimv2");
                scope.Connect();

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
                var scope = CreateManagementScope(hostname, @"root\cimv2");
                scope.Connect();

                // Query for Sysmon service - use LIKE for case-insensitive matching
                using var searcher = new ManagementObjectSearcher(scope,
                    new ObjectQuery("SELECT Name, PathName FROM Win32_Service WHERE Name LIKE 'Sysmon%' OR PathName LIKE '%Sysmon%.exe%'"));

                using var results = searcher.Get();

                foreach (ManagementObject service in results)
                {
                    var pathName = service["PathName"]?.ToString();
                    if (!string.IsNullOrEmpty(pathName))
                    {
                        // Extract the executable path and query its version
                        var exePath = ExtractExePath(pathName);
                        if (!string.IsNullOrEmpty(exePath) &&
                            exePath.Contains("sysmon", StringComparison.OrdinalIgnoreCase))
                        {
                            return QueryFileVersion(scope, exePath);
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get Sysmon version from {Hostname}", hostname);
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
                var scope = CreateManagementScope(hostname, @"root\cimv2");
                scope.Connect();

                // Query the Sysmon registry for config hash
                // Sysmon stores config info in HKLM\SYSTEM\CurrentControlSet\Services\SysmonDrv\Parameters
                var regScope = CreateManagementScope(hostname, @"root\default");
                regScope.Connect();

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
                var scope = CreateManagementScope(hostname, @"root\cimv2");
                scope.Connect();

                // Query for Sysmon service - use LIKE for case-insensitive matching
                // Also check for custom service names by looking for Sysmon in the PathName
                using var searcher = new ManagementObjectSearcher(scope,
                    new ObjectQuery("SELECT Name, PathName FROM Win32_Service WHERE Name LIKE 'Sysmon%' OR PathName LIKE '%Sysmon%.exe%'"));

                using var results = searcher.Get();

                foreach (ManagementObject service in results)
                {
                    var serviceName = service["Name"]?.ToString();
                    var pathName = service["PathName"]?.ToString();

                    _logger.LogDebug("Found potential Sysmon service on {Hostname}: Name={ServiceName}, Path={PathName}",
                        hostname, serviceName, pathName);

                    if (!string.IsNullOrEmpty(pathName))
                    {
                        var exePath = ExtractExePath(pathName);
                        if (!string.IsNullOrEmpty(exePath) &&
                            exePath.Contains("sysmon", StringComparison.OrdinalIgnoreCase))
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
                _logger.LogWarning(ex, "Failed to get Sysmon path from {Hostname}", hostname);
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
}
