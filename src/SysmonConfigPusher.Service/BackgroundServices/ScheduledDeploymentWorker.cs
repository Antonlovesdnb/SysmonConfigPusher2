using Microsoft.EntityFrameworkCore;
using SysmonConfigPusher.Core.Interfaces;
using SysmonConfigPusher.Core.Models;
using SysmonConfigPusher.Data;

namespace SysmonConfigPusher.Service.BackgroundServices;

public class ScheduledDeploymentWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDeploymentQueue _deploymentQueue;
    private readonly ILogger<ScheduledDeploymentWorker> _logger;

    // Check every 30 seconds for due deployments
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);

    public ScheduledDeploymentWorker(
        IServiceScopeFactory scopeFactory,
        IDeploymentQueue deploymentQueue,
        ILogger<ScheduledDeploymentWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _deploymentQueue = deploymentQueue;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Scheduled deployment worker started");

        using var timer = new PeriodicTimer(CheckInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDueDeploymentsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing scheduled deployments");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Scheduled deployment worker stopped");
    }

    private async Task ProcessDueDeploymentsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SysmonDbContext>();
        var auditService = scope.ServiceProvider.GetRequiredService<IAuditService>();

        var now = DateTime.UtcNow;

        // Find all pending deployments that are due
        var dueDeployments = await db.ScheduledDeployments
            .Include(s => s.Config)
            .Include(s => s.Computers)
            .Where(s => s.Status == "Pending" && s.ScheduledAt <= now)
            .ToListAsync(cancellationToken);

        if (dueDeployments.Count == 0) return;

        _logger.LogInformation("Found {Count} scheduled deployments due for execution", dueDeployments.Count);

        foreach (var scheduled in dueDeployments)
        {
            try
            {
                await ExecuteScheduledDeploymentAsync(db, auditService, scheduled, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute scheduled deployment {Id}", scheduled.Id);
                scheduled.Status = "Failed";
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task ExecuteScheduledDeploymentAsync(
        SysmonDbContext db,
        IAuditService auditService,
        ScheduledDeployment scheduled,
        CancellationToken cancellationToken)
    {
        var computerIds = scheduled.Computers.Select(c => c.ComputerId).ToList();

        if (computerIds.Count == 0)
        {
            _logger.LogWarning("Scheduled deployment {Id} has no target computers, marking as failed", scheduled.Id);
            scheduled.Status = "Failed";
            return;
        }

        // Create the deployment job
        var job = new DeploymentJob
        {
            Operation = scheduled.Operation,
            ConfigId = scheduled.ConfigId,
            StartedBy = $"Scheduled ({scheduled.CreatedBy})",
            Status = "Pending"
        };

        db.DeploymentJobs.Add(job);
        await db.SaveChangesAsync(cancellationToken);

        // Add pending results for each computer
        foreach (var computerId in computerIds)
        {
            job.Results.Add(new DeploymentResult
            {
                JobId = job.Id,
                ComputerId = computerId,
                Success = false,
                Message = "Pending"
            });
        }

        job.Status = "Running";
        await db.SaveChangesAsync(cancellationToken);

        // Update the scheduled deployment
        scheduled.Status = "Running";
        scheduled.DeploymentJobId = job.Id;

        // Log the audit event
        await auditService.LogAsync(
            scheduled.CreatedBy,
            AuditAction.DeploymentStart,
            new
            {
                JobId = job.Id,
                ScheduledDeploymentId = scheduled.Id,
                Operation = scheduled.Operation,
                ComputerCount = computerIds.Count,
                ConfigId = scheduled.ConfigId,
                Scheduled = true
            });

        _logger.LogInformation(
            "Started scheduled deployment {ScheduledId} as job {JobId} ({Operation}) on {Count} computers",
            scheduled.Id, job.Id, scheduled.Operation, computerIds.Count);

        // Enqueue to the deployment worker
        _deploymentQueue.Enqueue(job.Id);
    }
}
