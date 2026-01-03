namespace SysmonConfigPusher.Core.Interfaces;

public interface IFileTransferService
{
    /// <summary>
    /// Indicates if this service is available in the current deployment mode.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Copies a file to a remote host via SMB admin share (C$).
    /// </summary>
    Task<FileTransferResult> CopyFileAsync(
        string hostname,
        string localFilePath,
        string remoteRelativePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies content directly to a remote file via SMB admin share (C$).
    /// </summary>
    Task<FileTransferResult> WriteFileAsync(
        string hostname,
        string content,
        string remoteRelativePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a directory exists on a remote host.
    /// </summary>
    Task<FileTransferResult> EnsureDirectoryAsync(
        string hostname,
        string remoteRelativePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a file exists on a remote host.
    /// </summary>
    Task<bool> FileExistsAsync(
        string hostname,
        string remoteRelativePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a file from a remote host.
    /// </summary>
    Task<FileTransferResult> DeleteFileAsync(
        string hostname,
        string remoteRelativePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a directory and its contents from a remote host.
    /// </summary>
    Task<FileTransferResult> DeleteDirectoryAsync(
        string hostname,
        string remoteRelativePath,
        CancellationToken cancellationToken = default);
}

public record FileTransferResult(
    bool Success,
    string? ErrorMessage);
