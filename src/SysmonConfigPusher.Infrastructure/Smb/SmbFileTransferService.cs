using Microsoft.Extensions.Logging;
using SysmonConfigPusher.Core.Interfaces;

namespace SysmonConfigPusher.Infrastructure.Smb;

public class SmbFileTransferService : IFileTransferService
{
    private readonly ILogger<SmbFileTransferService> _logger;
    private const string DefaultAdminShare = "C$";
    private const string DefaultSysmonPath = "SysmonFiles";

    public SmbFileTransferService(ILogger<SmbFileTransferService> logger)
    {
        _logger = logger;
    }

    public async Task<FileTransferResult> CopyFileAsync(
        string hostname,
        string localFilePath,
        string remoteRelativePath,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                var remotePath = BuildUncPath(hostname, remoteRelativePath);
                var remoteDir = Path.GetDirectoryName(remotePath);

                _logger.LogDebug("Copying {LocalPath} to {RemotePath}", localFilePath, remotePath);

                // Ensure directory exists
                if (!string.IsNullOrEmpty(remoteDir) && !Directory.Exists(remoteDir))
                {
                    Directory.CreateDirectory(remoteDir);
                }

                File.Copy(localFilePath, remotePath, overwrite: true);

                _logger.LogInformation("Copied file to {Hostname}: {RemotePath}", hostname, remoteRelativePath);
                return new FileTransferResult(true, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to copy file to {Hostname}: {RemotePath}", hostname, remoteRelativePath);
                return new FileTransferResult(false, ex.Message);
            }
        }, cancellationToken);
    }

    public async Task<FileTransferResult> WriteFileAsync(
        string hostname,
        string content,
        string remoteRelativePath,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                var remotePath = BuildUncPath(hostname, remoteRelativePath);
                var remoteDir = Path.GetDirectoryName(remotePath);

                _logger.LogDebug("Writing content to {RemotePath}", remotePath);

                // Ensure directory exists
                if (!string.IsNullOrEmpty(remoteDir) && !Directory.Exists(remoteDir))
                {
                    Directory.CreateDirectory(remoteDir);
                }

                File.WriteAllText(remotePath, content);

                _logger.LogInformation("Wrote file to {Hostname}: {RemotePath}", hostname, remoteRelativePath);
                return new FileTransferResult(true, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write file to {Hostname}: {RemotePath}", hostname, remoteRelativePath);
                return new FileTransferResult(false, ex.Message);
            }
        }, cancellationToken);
    }

    public async Task<FileTransferResult> EnsureDirectoryAsync(
        string hostname,
        string remoteRelativePath,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                var remotePath = BuildUncPath(hostname, remoteRelativePath);

                _logger.LogDebug("Ensuring directory exists: {RemotePath}", remotePath);

                if (!Directory.Exists(remotePath))
                {
                    Directory.CreateDirectory(remotePath);
                    _logger.LogInformation("Created directory on {Hostname}: {RemotePath}", hostname, remoteRelativePath);
                }

                return new FileTransferResult(true, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create directory on {Hostname}: {RemotePath}", hostname, remoteRelativePath);
                return new FileTransferResult(false, ex.Message);
            }
        }, cancellationToken);
    }

    public async Task<bool> FileExistsAsync(
        string hostname,
        string remoteRelativePath,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                var remotePath = BuildUncPath(hostname, remoteRelativePath);
                return File.Exists(remotePath);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to check file existence on {Hostname}: {RemotePath}", hostname, remoteRelativePath);
                return false;
            }
        }, cancellationToken);
    }

    public async Task<FileTransferResult> DeleteFileAsync(
        string hostname,
        string remoteRelativePath,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                var remotePath = BuildUncPath(hostname, remoteRelativePath);

                if (File.Exists(remotePath))
                {
                    File.Delete(remotePath);
                    _logger.LogInformation("Deleted file on {Hostname}: {RemotePath}", hostname, remoteRelativePath);
                }

                return new FileTransferResult(true, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete file on {Hostname}: {RemotePath}", hostname, remoteRelativePath);
                return new FileTransferResult(false, ex.Message);
            }
        }, cancellationToken);
    }

    public async Task<FileTransferResult> DeleteDirectoryAsync(
        string hostname,
        string remoteRelativePath,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                var remotePath = BuildUncPath(hostname, remoteRelativePath);

                if (Directory.Exists(remotePath))
                {
                    Directory.Delete(remotePath, recursive: true);
                    _logger.LogInformation("Deleted directory on {Hostname}: {RemotePath}", hostname, remoteRelativePath);
                }

                return new FileTransferResult(true, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete directory on {Hostname}: {RemotePath}", hostname, remoteRelativePath);
                return new FileTransferResult(false, ex.Message);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Builds a UNC path from hostname and relative path.
    /// Example: hostname="PC01", relativePath="SysmonFiles\config.xml"
    /// Returns: \\PC01\C$\SysmonFiles\config.xml
    /// </summary>
    private static string BuildUncPath(string hostname, string relativePath)
    {
        // Normalize path separators
        relativePath = relativePath.Replace('/', '\\').TrimStart('\\');

        return $@"\\{hostname}\{DefaultAdminShare}\{relativePath}";
    }

    /// <summary>
    /// Gets the standard Sysmon files path on a remote host.
    /// </summary>
    public static string GetSysmonFilesPath(string hostname)
    {
        return $@"\\{hostname}\{DefaultAdminShare}\{DefaultSysmonPath}";
    }
}
