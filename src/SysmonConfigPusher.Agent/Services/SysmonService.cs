using System.Diagnostics;
using System.Security.Cryptography;
using System.ServiceProcess;
using System.Text;
using SysmonConfigPusher.Shared;

namespace SysmonConfigPusher.Agent.Services;

/// <summary>
/// Handles Sysmon installation, configuration, and status queries.
/// Implements strict security controls - only executes whitelisted commands.
/// </summary>
public class SysmonService
{
    private readonly ILogger<SysmonService> _logger;
    private readonly string _sysmonDirectory;

    // Sysmon64.exe creates "Sysmon64" service, Sysmon.exe creates "Sysmon" service
    private static readonly string[] SysmonServiceNames = ["Sysmon64", "Sysmon"];
    private const string SysmonDriverName = "SysmonDrv";

    public SysmonService(ILogger<SysmonService> logger)
    {
        _logger = logger;
        var basePath = Path.GetDirectoryName(Environment.ProcessPath)
            ?? AgentConstants.DefaultInstallPath;
        _sysmonDirectory = Path.Combine(basePath, AgentConstants.SysmonFilesDirectory);
    }

    /// <summary>
    /// Get current Sysmon status
    /// </summary>
    public AgentStatusPayload GetStatus()
    {
        var status = new AgentStatusPayload
        {
            AgentVersion = typeof(SysmonService).Assembly.GetName().Version?.ToString() ?? "1.0.0",
            Hostname = Environment.MachineName,
            Is64Bit = Environment.Is64BitOperatingSystem,
            OperatingSystem = Environment.OSVersion.ToString(),
            UptimeSeconds = Environment.TickCount64 / 1000
        };

        // Try each possible Sysmon service name
        foreach (var serviceName in SysmonServiceNames)
        {
            try
            {
                using var sc = new ServiceController(serviceName);
                var serviceStatus = sc.Status; // This throws if service doesn't exist

                // Service found!
                status.SysmonInstalled = true;
                status.ServiceStatus = serviceStatus.ToString();

                // Get Sysmon version and path from file
                var sysmonPath = GetSysmonPath();
                if (File.Exists(sysmonPath))
                {
                    status.SysmonPath = sysmonPath;
                    var versionInfo = FileVersionInfo.GetVersionInfo(sysmonPath);
                    // Use full version format (e.g., "15.15.0.0") to match WMI version format
                    status.SysmonVersion = $"{versionInfo.FileMajorPart}.{versionInfo.FileMinorPart}.{versionInfo.FileBuildPart}.{versionInfo.FilePrivatePart}";
                }

                // Get config hash
                status.ConfigHash = GetCurrentConfigHash();

                _logger.LogDebug("Found Sysmon service: {ServiceName}, Status: {Status}, Path: {Path}",
                    serviceName, serviceStatus, status.SysmonPath);
                break; // Found service, stop looking
            }
            catch (InvalidOperationException)
            {
                // Service with this name doesn't exist, try next
                continue;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error checking Sysmon service {ServiceName}", serviceName);
            }
        }

        return status;
    }

    /// <summary>
    /// Install Sysmon with provided binary and optional config
    /// </summary>
    public async Task<(bool Success, string Message)> InstallAsync(InstallSysmonPayload payload)
    {
        try
        {
            // Ensure directory exists
            Directory.CreateDirectory(_sysmonDirectory);

            // Write binary - use Sysmon.exe as the standard name (sysinternals binary works for both 32/64-bit)
            var binaryPath = Path.Combine(_sysmonDirectory, "Sysmon.exe");
            var binaryBytes = Convert.FromBase64String(payload.SysmonBinaryBase64);
            await File.WriteAllBytesAsync(binaryPath, binaryBytes);
            _logger.LogInformation("Wrote Sysmon binary to {Path}", binaryPath);

            // Validate binary is actually Sysmon (basic check)
            if (!ValidateSysmonBinary(binaryPath))
            {
                File.Delete(binaryPath);
                return (false, "Invalid Sysmon binary - security validation failed");
            }

            // Check if Sysmon is already installed
            var alreadyInstalled = IsSysmonInstalled();
            _logger.LogInformation("Sysmon already installed: {Installed}", alreadyInstalled);

            string? configPath = null;

            // Write config if provided
            if (!string.IsNullOrEmpty(payload.ConfigXml))
            {
                configPath = Path.Combine(_sysmonDirectory, "config.xml");
                await File.WriteAllTextAsync(configPath, payload.ConfigXml);
                _logger.LogInformation("Wrote Sysmon config to {Path}", configPath);

                // Verify config hash if provided
                if (!string.IsNullOrEmpty(payload.ConfigHash))
                {
                    var actualHash = ComputeHash(payload.ConfigXml);
                    if (!string.Equals(actualHash, payload.ConfigHash, StringComparison.OrdinalIgnoreCase))
                    {
                        return (false, "Config hash mismatch - possible tampering");
                    }
                }
            }

            if (alreadyInstalled)
            {
                // Sysmon already installed - just update config if provided
                if (configPath != null)
                {
                    _logger.LogInformation("Sysmon already installed, updating config");
                    var configResult = await ExecuteSysmonCommand(binaryPath, $"-c \"{configPath}\"");
                    if (!configResult.Success)
                    {
                        return configResult;
                    }
                    return (true, "Sysmon already installed - configuration updated");
                }
                else
                {
                    return (true, "Sysmon already installed (no config change requested)");
                }
            }
            else
            {
                // Fresh install
                string installArgs = configPath != null
                    ? $"-accepteula -i \"{configPath}\""
                    : "-accepteula -i";

                var result = await ExecuteSysmonCommand(binaryPath, installArgs);
                if (!result.Success)
                {
                    return result;
                }

                _logger.LogInformation("Sysmon installed successfully");
                return (true, "Sysmon installed successfully");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install Sysmon");
            return (false, $"Installation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Check if Sysmon service is installed (checks both Sysmon64 and Sysmon service names)
    /// </summary>
    private bool IsSysmonInstalled()
    {
        foreach (var serviceName in SysmonServiceNames)
        {
            try
            {
                using var sc = new ServiceController(serviceName);
                var status = sc.Status; // This will throw if service doesn't exist
                return true;
            }
            catch (InvalidOperationException)
            {
                // Try next service name
                continue;
            }
        }
        return false;
    }

    /// <summary>
    /// Update Sysmon configuration
    /// </summary>
    public async Task<(bool Success, string Message)> UpdateConfigAsync(UpdateConfigPayload payload)
    {
        try
        {
            var sysmonPath = GetSysmonPath();
            if (!File.Exists(sysmonPath))
            {
                return (false, "Sysmon not installed");
            }

            // Write new config
            var configPath = Path.Combine(_sysmonDirectory, "config.xml");
            await File.WriteAllTextAsync(configPath, payload.ConfigXml);

            // Verify hash if provided
            if (!string.IsNullOrEmpty(payload.ConfigHash))
            {
                var actualHash = ComputeHash(payload.ConfigXml);
                if (!string.Equals(actualHash, payload.ConfigHash, StringComparison.OrdinalIgnoreCase))
                {
                    return (false, "Config hash mismatch - possible tampering");
                }
            }

            // Apply config
            var result = await ExecuteSysmonCommand(sysmonPath, $"-c \"{configPath}\"");
            if (!result.Success)
            {
                return result;
            }

            _logger.LogInformation("Sysmon config updated successfully");
            return (true, "Configuration updated successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update Sysmon config");
            return (false, $"Config update failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Uninstall Sysmon
    /// </summary>
    public async Task<(bool Success, string Message)> UninstallAsync()
    {
        try
        {
            var sysmonPath = GetSysmonPath();
            if (!File.Exists(sysmonPath))
            {
                return (false, "Sysmon not installed");
            }

            var result = await ExecuteSysmonCommand(sysmonPath, "-u");
            if (!result.Success)
            {
                return result;
            }

            _logger.LogInformation("Sysmon uninstalled successfully");
            return (true, "Sysmon uninstalled successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to uninstall Sysmon");
            return (false, $"Uninstall failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Restart Sysmon service
    /// </summary>
    public async Task<(bool Success, string Message)> RestartAsync()
    {
        foreach (var serviceName in SysmonServiceNames)
        {
            try
            {
                using var sc = new ServiceController(serviceName);
                var status = sc.Status; // This throws if service doesn't exist

                // Found the service, restart it
                if (status == ServiceControllerStatus.Running)
                {
                    sc.Stop();
                    await Task.Run(() => sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30)));
                }

                sc.Start();
                await Task.Run(() => sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30)));

                _logger.LogInformation("Sysmon service {ServiceName} restarted successfully", serviceName);
                return (true, "Sysmon service restarted");
            }
            catch (InvalidOperationException)
            {
                // Service with this name doesn't exist, try next
                continue;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to restart Sysmon service {ServiceName}", serviceName);
                return (false, $"Restart failed: {ex.Message}");
            }
        }

        return (false, "Sysmon service not found");
    }

    private string GetSysmonPath()
    {
        // Check our installation directory first
        var path64 = Path.Combine(_sysmonDirectory, "Sysmon64.exe");
        if (File.Exists(path64)) return path64;

        var path32 = Path.Combine(_sysmonDirectory, "Sysmon.exe");
        if (File.Exists(path32)) return path32;

        // Sysmon installs itself to C:\Windows\ (not System32)
        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        var winPath64 = Path.Combine(windowsDir, "Sysmon64.exe");
        if (File.Exists(winPath64)) return winPath64;

        var winPath32 = Path.Combine(windowsDir, "Sysmon.exe");
        if (File.Exists(winPath32)) return winPath32;

        // Also try System32 as fallback
        var sys64 = Path.Combine(Environment.SystemDirectory, "Sysmon64.exe");
        if (File.Exists(sys64)) return sys64;

        var sys32 = Path.Combine(Environment.SystemDirectory, "Sysmon.exe");
        if (File.Exists(sys32)) return sys32;

        return path64; // Default
    }

    private string? GetCurrentConfigHash()
    {
        try
        {
            var configPath = Path.Combine(_sysmonDirectory, "config.xml");
            if (!File.Exists(configPath)) return null;

            var content = File.ReadAllText(configPath);
            return ComputeHash(content);
        }
        catch
        {
            return null;
        }
    }

    private static string ComputeHash(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private bool ValidateSysmonBinary(string path)
    {
        try
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(path);

            _logger.LogInformation("Validating Sysmon binary: Company={Company}, Product={Product}, File={File}",
                versionInfo.CompanyName, versionInfo.ProductName, versionInfo.FileName);

            // Check it's signed by Microsoft or Sysinternals
            var validCompanies = new[] { "Microsoft", "Sysinternals", "Mark Russinovich" };
            var companyValid = !string.IsNullOrEmpty(versionInfo.CompanyName) &&
                validCompanies.Any(c => versionInfo.CompanyName.Contains(c, StringComparison.OrdinalIgnoreCase));

            if (!companyValid)
            {
                _logger.LogWarning("Sysmon binary company validation failed: {Company}", versionInfo.CompanyName);
                return false;
            }

            // Check product name or file description contains Sysmon
            var productValid = (!string.IsNullOrEmpty(versionInfo.ProductName) &&
                versionInfo.ProductName.Contains("Sysmon", StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(versionInfo.FileDescription) &&
                versionInfo.FileDescription.Contains("Sysmon", StringComparison.OrdinalIgnoreCase));

            if (!productValid)
            {
                _logger.LogWarning("Binary does not appear to be Sysmon: Product={Product}, Description={Description}",
                    versionInfo.ProductName, versionInfo.FileDescription);
                return false;
            }

            _logger.LogInformation("Sysmon binary validation passed");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to validate Sysmon binary");
            return false;
        }
    }

    private async Task<(bool Success, string Message)> ExecuteSysmonCommand(string path, string arguments)
    {
        // SECURITY: Validate the executable is allowed
        var fileName = Path.GetFileName(path);
        if (!AgentConstants.AllowedExecutables.Contains(fileName))
        {
            _logger.LogWarning("Attempted to execute non-whitelisted executable: {Path}", path);
            return (false, "Security violation: executable not whitelisted");
        }

        // SECURITY: Validate arguments contain only allowed flags
        var argParts = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var arg in argParts)
        {
            // Skip file paths (they start with quotes or contain path separators)
            if (arg.StartsWith('"') || arg.Contains('\\') || arg.Contains('/'))
                continue;

            // Check if argument is whitelisted
            if (!AgentConstants.AllowedSysmonArgs.Contains(arg))
            {
                _logger.LogWarning("Attempted to use non-whitelisted Sysmon argument: {Arg}", arg);
                return (false, $"Security violation: argument '{arg}' not whitelisted");
            }
        }

        _logger.LogInformation("Executing: {Path} {Args}", path, arguments);

        var psi = new ProcessStartInfo
        {
            FileName = path,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            return (false, "Failed to start process");
        }

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var error = string.IsNullOrEmpty(stderr) ? stdout : stderr;
            _logger.LogWarning("Sysmon command failed with exit code {Code}: {Error}",
                process.ExitCode, error);
            return (false, $"Command failed (exit code {process.ExitCode}): {error}");
        }

        return (true, stdout);
    }
}
