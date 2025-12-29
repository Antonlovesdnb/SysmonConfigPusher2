using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SysmonConfigPusher.Core.Interfaces;
using SysmonConfigPusher.Core.Models;
using SysmonConfigPusher.Data;
using SysmonConfigPusher.Service.Hubs;

namespace SysmonConfigPusher.Service.BackgroundServices;

public class DeploymentWorker : BackgroundService
{
    private readonly IDeploymentQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<DeploymentHub, IDeploymentHubClient> _hubContext;
    private readonly ISysmonBinaryCacheService _binaryCacheService;
    private readonly ILogger<DeploymentWorker> _logger;

    // Configurable parallelism based on target count
    private const int SmallDeploymentThreshold = 10;
    private const int MediumDeploymentThreshold = 100;
    private const int SmallParallelism = 5;
    private const int MediumParallelism = 20;
    private const int LargeParallelism = 50;

    public DeploymentWorker(
        IDeploymentQueue queue,
        IServiceScopeFactory scopeFactory,
        IHubContext<DeploymentHub, IDeploymentHubClient> hubContext,
        ISysmonBinaryCacheService binaryCacheService,
        ILogger<DeploymentWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
        _binaryCacheService = binaryCacheService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Deployment worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            var jobId = await _queue.DequeueAsync(stoppingToken);
            if (jobId == null) continue;

            try
            {
                await ProcessDeploymentJobAsync(jobId.Value, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing deployment job {JobId}", jobId);
            }
        }

        _logger.LogInformation("Deployment worker stopped");
    }

    private async Task ProcessDeploymentJobAsync(int jobId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SysmonDbContext>();
        var remoteExec = scope.ServiceProvider.GetRequiredService<IRemoteExecutionService>();
        var fileTransfer = scope.ServiceProvider.GetRequiredService<IFileTransferService>();

        var job = await db.DeploymentJobs
            .Include(j => j.Config)
            .Include(j => j.Results)
                .ThenInclude(r => r.Computer)
            .FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);

        if (job == null)
        {
            _logger.LogWarning("Deployment job {JobId} not found", jobId);
            return;
        }

        if (job.Status == "Cancelled")
        {
            _logger.LogInformation("Deployment job {JobId} was cancelled", jobId);
            return;
        }

        _logger.LogInformation("Starting deployment job {JobId}: {Operation} on {Count} computers",
            jobId, job.Operation, job.Results.Count);

        job.Status = "Running";
        await db.SaveChangesAsync(cancellationToken);

        var parallelism = GetParallelism(job.Results.Count);
        var completed = 0;
        var successCount = 0;
        var failureCount = 0;

        await Parallel.ForEachAsync(
            job.Results,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = parallelism,
                CancellationToken = cancellationToken
            },
            async (result, ct) =>
            {
                // Check if job was cancelled
                var currentJob = await db.DeploymentJobs
                    .AsNoTracking()
                    .FirstOrDefaultAsync(j => j.Id == jobId, ct);

                if (currentJob?.Status == "Cancelled")
                {
                    return;
                }

                var success = false;
                string? message = null;

                try
                {
                    (success, message) = await ExecuteOperationAsync(
                        job.Operation,
                        result.Computer,
                        job.Config,
                        remoteExec,
                        fileTransfer,
                        ct);
                }
                catch (Exception ex)
                {
                    success = false;
                    message = ex.Message;
                    _logger.LogError(ex, "Operation failed on {Hostname}", result.Computer.Hostname);
                }

                result.Success = success;
                result.Message = message;
                result.CompletedAt = DateTime.UtcNow;

                Interlocked.Increment(ref completed);
                if (success)
                    Interlocked.Increment(ref successCount);
                else
                    Interlocked.Increment(ref failureCount);

                // Send progress update via SignalR
                await _hubContext.Clients
                    .Group($"deployment-{jobId}")
                    .DeploymentProgress(new DeploymentProgressMessage(
                        jobId,
                        result.ComputerId,
                        result.Computer.Hostname,
                        success ? "Success" : "Failed",
                        success,
                        message,
                        completed,
                        job.Results.Count));
            });

        // Save all results
        await db.SaveChangesAsync(cancellationToken);

        // Update job status
        job.Status = failureCount == 0 ? "Completed" : "CompletedWithErrors";
        job.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        // Send completion notification
        await _hubContext.Clients
            .Group($"deployment-{jobId}")
            .DeploymentCompleted(jobId, failureCount == 0, $"Completed: {successCount} succeeded, {failureCount} failed");

        _logger.LogInformation("Deployment job {JobId} completed: {Success} succeeded, {Failed} failed",
            jobId, successCount, failureCount);
    }

    private async Task<(bool Success, string? Message)> ExecuteOperationAsync(
        string operation,
        Computer computer,
        Config? config,
        IRemoteExecutionService remoteExec,
        IFileTransferService fileTransfer,
        CancellationToken cancellationToken)
    {
        return operation.ToLowerInvariant() switch
        {
            "install" => await InstallSysmonAsync(computer.Hostname, config, remoteExec, fileTransfer, cancellationToken),
            "update" or "pushconfig" => await UpdateConfigAsync(computer, config, remoteExec, fileTransfer, cancellationToken),
            "uninstall" => await UninstallSysmonAsync(computer, remoteExec, cancellationToken),
            "test" => await TestConnectivityAsync(computer.Hostname, remoteExec, cancellationToken),
            _ => (false, $"Unknown operation: {operation}")
        };
    }

    private async Task<(bool, string?)> InstallSysmonAsync(
        string hostname,
        Config? config,
        IRemoteExecutionService remoteExec,
        IFileTransferService fileTransfer,
        CancellationToken cancellationToken)
    {
        const string sysmonDir = "SysmonFiles";
        const string configFile = "sysmonconfig.xml";
        var sysmonExe = _binaryCacheService.BinaryFilename;

        // 1. Ensure directory exists
        var dirResult = await fileTransfer.EnsureDirectoryAsync(hostname, sysmonDir, cancellationToken);
        if (!dirResult.Success)
            return (false, $"Failed to create directory: {dirResult.ErrorMessage}");

        // 2. Copy Sysmon binary from cache
        if (!_binaryCacheService.IsCached)
            return (false, "Sysmon binary not found in cache. Download it from the Settings page first.");

        var copyResult = await fileTransfer.CopyFileAsync(
            hostname, _binaryCacheService.CachePath, Path.Combine(sysmonDir, sysmonExe), cancellationToken);
        if (!copyResult.Success)
            return (false, $"Failed to copy Sysmon binary: {copyResult.ErrorMessage}");

        // 3. Copy config if provided
        if (config != null)
        {
            var configResult = await fileTransfer.WriteFileAsync(
                hostname, config.Content, Path.Combine(sysmonDir, configFile), cancellationToken);
            if (!configResult.Success)
                return (false, $"Failed to write config: {configResult.ErrorMessage}");
        }

        // 4. Install Sysmon
        var installCmd = config != null
            ? $@"C:\{sysmonDir}\{sysmonExe} -accepteula -i C:\{sysmonDir}\{configFile}"
            : $@"C:\{sysmonDir}\{sysmonExe} -accepteula -i";

        var execResult = await remoteExec.ExecuteCommandAsync(hostname, installCmd, cancellationToken);
        if (!execResult.Success)
            return (false, $"Failed to install Sysmon: {execResult.ErrorMessage}");

        return (true, "Sysmon installed successfully");
    }

    private async Task<(bool, string?)> UpdateConfigAsync(
        Computer computer,
        Config? config,
        IRemoteExecutionService remoteExec,
        IFileTransferService fileTransfer,
        CancellationToken cancellationToken)
    {
        if (config == null)
            return (false, "No config specified");

        // 1. Use cached Sysmon path if available, otherwise auto-detect
        var sysmonPath = computer.SysmonPath;
        if (string.IsNullOrEmpty(sysmonPath))
        {
            sysmonPath = await remoteExec.GetSysmonPathAsync(computer.Hostname, cancellationToken);
        }
        if (string.IsNullOrEmpty(sysmonPath))
            return (false, "Sysmon is not installed on this host");

        // 2. Write config file to same directory as Sysmon executable
        var sysmonDir = Path.GetDirectoryName(sysmonPath) ?? @"C:\Windows";
        var configFilePath = Path.Combine(sysmonDir, "sysmonconfig.xml");

        // Convert to UNC-style path for SMB (remove drive letter, use relative path)
        var smbConfigPath = configFilePath.Substring(3); // Remove "C:\"

        var configResult = await fileTransfer.WriteFileAsync(
            computer.Hostname, config.Content, smbConfigPath, cancellationToken);
        if (!configResult.Success)
            return (false, $"Failed to write config: {configResult.ErrorMessage}");

        // 3. Update Sysmon config using detected path
        var updateCmd = $"\"{sysmonPath}\" -c \"{configFilePath}\"";
        var execResult = await remoteExec.ExecuteCommandAsync(computer.Hostname, updateCmd, cancellationToken);
        if (!execResult.Success)
            return (false, $"Failed to update config: {execResult.ErrorMessage}");

        return (true, "Config updated successfully");
    }

    private async Task<(bool, string?)> UninstallSysmonAsync(
        Computer computer,
        IRemoteExecutionService remoteExec,
        CancellationToken cancellationToken)
    {
        // Use cached Sysmon path if available, otherwise auto-detect
        var sysmonPath = computer.SysmonPath;
        if (string.IsNullOrEmpty(sysmonPath))
        {
            sysmonPath = await remoteExec.GetSysmonPathAsync(computer.Hostname, cancellationToken);
        }
        if (string.IsNullOrEmpty(sysmonPath))
            return (false, "Sysmon is not installed on this host");

        var uninstallCmd = $"\"{sysmonPath}\" -u force";
        var execResult = await remoteExec.ExecuteCommandAsync(computer.Hostname, uninstallCmd, cancellationToken);

        if (!execResult.Success)
            return (false, $"Failed to uninstall Sysmon: {execResult.ErrorMessage}");

        return (true, "Sysmon uninstalled successfully");
    }

    private async Task<(bool, string?)> TestConnectivityAsync(
        string hostname,
        IRemoteExecutionService remoteExec,
        CancellationToken cancellationToken)
    {
        var reachable = await remoteExec.TestConnectivityAsync(hostname, cancellationToken);
        return reachable
            ? (true, "Host is reachable via WMI")
            : (false, "Host is not reachable via WMI");
    }

    private static int GetParallelism(int targetCount)
    {
        return targetCount switch
        {
            <= SmallDeploymentThreshold => SmallParallelism,
            <= MediumDeploymentThreshold => MediumParallelism,
            _ => LargeParallelism
        };
    }
}
