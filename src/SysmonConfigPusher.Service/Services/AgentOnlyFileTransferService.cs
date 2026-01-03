using SysmonConfigPusher.Core.Interfaces;

namespace SysmonConfigPusher.Service.Services;

/// <summary>
/// File transfer service for AgentOnly mode.
/// Returns not-available for SMB operations - computers must use agents.
/// </summary>
public class AgentOnlyFileTransferService : IFileTransferService
{
    private readonly ILogger<AgentOnlyFileTransferService> _logger;
    private const string NotAvailableMessage = "SMB file transfer is not available in AgentOnly mode. This computer must be managed via an agent.";

    public AgentOnlyFileTransferService(ILogger<AgentOnlyFileTransferService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsAvailable => false;

    public Task<FileTransferResult> CopyFileAsync(
        string hostname,
        string localFilePath,
        string remoteRelativePath,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("SMB file copy attempted to {Hostname} but not available in AgentOnly mode", hostname);
        return Task.FromResult(new FileTransferResult(
            Success: false,
            ErrorMessage: NotAvailableMessage));
    }

    public Task<FileTransferResult> WriteFileAsync(
        string hostname,
        string content,
        string remoteRelativePath,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("SMB file write attempted to {Hostname} but not available in AgentOnly mode", hostname);
        return Task.FromResult(new FileTransferResult(
            Success: false,
            ErrorMessage: NotAvailableMessage));
    }

    public Task<FileTransferResult> EnsureDirectoryAsync(
        string hostname,
        string remoteRelativePath,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("SMB directory creation not available in AgentOnly mode for {Hostname}", hostname);
        return Task.FromResult(new FileTransferResult(
            Success: false,
            ErrorMessage: NotAvailableMessage));
    }

    public Task<bool> FileExistsAsync(
        string hostname,
        string remoteRelativePath,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("SMB file existence check not available in AgentOnly mode for {Hostname}", hostname);
        return Task.FromResult(false);
    }

    public Task<FileTransferResult> DeleteFileAsync(
        string hostname,
        string remoteRelativePath,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("SMB file deletion not available in AgentOnly mode for {Hostname}", hostname);
        return Task.FromResult(new FileTransferResult(
            Success: false,
            ErrorMessage: NotAvailableMessage));
    }

    public Task<FileTransferResult> DeleteDirectoryAsync(
        string hostname,
        string remoteRelativePath,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("SMB directory deletion not available in AgentOnly mode for {Hostname}", hostname);
        return Task.FromResult(new FileTransferResult(
            Success: false,
            ErrorMessage: NotAvailableMessage));
    }
}
