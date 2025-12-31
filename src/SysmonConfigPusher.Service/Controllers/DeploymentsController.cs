using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SysmonConfigPusher.Data;
using SysmonConfigPusher.Core.Models;
using SysmonConfigPusher.Core.Interfaces;

namespace SysmonConfigPusher.Service.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "RequireViewer")]
public class DeploymentsController : ControllerBase
{
    private readonly SysmonDbContext _db;
    private readonly IDeploymentQueue _queue;
    private readonly IAuditService _auditService;
    private readonly ILogger<DeploymentsController> _logger;

    public DeploymentsController(
        SysmonDbContext db,
        IDeploymentQueue queue,
        IAuditService auditService,
        ILogger<DeploymentsController> logger)
    {
        _db = db;
        _queue = queue;
        _auditService = auditService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<DeploymentJobDto>>> GetJobs(
        [FromQuery] int? limit = 50)
    {
        var jobs = await _db.DeploymentJobs
            .OrderByDescending(j => j.StartedAt)
            .Take(limit ?? 50)
            .Select(j => new DeploymentJobDto(
                j.Id,
                j.Operation,
                j.ConfigId,
                j.Config != null ? j.Config.Filename : null,
                j.StartedBy,
                j.StartedAt,
                j.CompletedAt,
                j.Status,
                j.Results.Count(r => r.Success),
                j.Results.Count(r => !r.Success),
                j.Results.Count))
            .ToListAsync();

        return Ok(jobs);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<DeploymentJobDetailDto>> GetJob(int id)
    {
        var job = await _db.DeploymentJobs
            .Include(j => j.Config)
            .Include(j => j.Results)
                .ThenInclude(r => r.Computer)
            .FirstOrDefaultAsync(j => j.Id == id);

        if (job == null)
            return NotFound();

        return Ok(new DeploymentJobDetailDto(
            job.Id,
            job.Operation,
            job.ConfigId,
            job.Config?.Filename,
            job.StartedBy,
            job.StartedAt,
            job.CompletedAt,
            job.Status,
            job.Results.Select(r => new DeploymentResultDto(
                r.Id,
                r.ComputerId,
                r.Computer.Hostname,
                r.Success,
                r.Message,
                r.CompletedAt)).ToList()));
    }

    [HttpPost]
    [Authorize(Policy = "RequireOperator")]
    public async Task<ActionResult<DeploymentJobDto>> StartDeployment([FromBody] StartDeploymentRequest request)
    {
        if (request.ComputerIds.Length == 0)
            return BadRequest("No computers specified");

        var job = new DeploymentJob
        {
            Operation = request.Operation,
            ConfigId = request.ConfigId,
            SysmonVersion = request.SysmonVersion,
            StartedBy = User.Identity?.Name,
            Status = "Pending"
        };

        _db.DeploymentJobs.Add(job);
        await _db.SaveChangesAsync();

        // Add pending results for each computer
        foreach (var computerId in request.ComputerIds)
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
        await _db.SaveChangesAsync();

        await _auditService.LogAsync(User.Identity?.Name, AuditAction.DeploymentStart,
            new { JobId = job.Id, Operation = request.Operation, ComputerCount = request.ComputerIds.Length, ConfigId = request.ConfigId, SysmonVersion = request.SysmonVersion });

        _logger.LogInformation("User {User} started deployment job {JobId} ({Operation}) on {Count} computers",
            User.Identity?.Name, job.Id, request.Operation, request.ComputerIds.Length);

        // Queue the job for background processing
        _queue.Enqueue(job.Id);

        return CreatedAtAction(nameof(GetJob), new { id = job.Id },
            new DeploymentJobDto(
                job.Id,
                job.Operation,
                job.ConfigId,
                null,
                job.StartedBy,
                job.StartedAt,
                job.CompletedAt,
                job.Status,
                0, 0,
                request.ComputerIds.Length));
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = "RequireOperator")]
    public async Task<ActionResult> CancelJob(int id)
    {
        var job = await _db.DeploymentJobs.FindAsync(id);
        if (job == null)
            return NotFound();

        if (job.Status == "Completed" || job.Status == "Cancelled")
            return BadRequest("Job already completed or cancelled");

        job.Status = "Cancelled";
        job.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _auditService.LogAsync(User.Identity?.Name, AuditAction.DeploymentCancel,
            new { JobId = id });

        _logger.LogInformation("User {User} cancelled deployment job {JobId}", User.Identity?.Name, id);

        return NoContent();
    }

    [HttpDelete]
    [Authorize(Policy = "RequireAdmin")]
    public async Task<ActionResult<PurgeResultDto>> PurgeHistory([FromQuery] int? olderThanDays = 30)
    {
        var cutoff = DateTime.UtcNow.AddDays(-(olderThanDays ?? 30));

        // Find completed jobs older than cutoff
        var jobsToDelete = await _db.DeploymentJobs
            .Where(j => j.CompletedAt != null && j.CompletedAt < cutoff)
            .Where(j => j.Status == "Completed" || j.Status == "CompletedWithErrors" || j.Status == "Cancelled")
            .Include(j => j.Results)
            .ToListAsync();

        var jobCount = jobsToDelete.Count;
        var resultCount = jobsToDelete.Sum(j => j.Results.Count);

        // Delete results first (due to FK), then jobs
        foreach (var job in jobsToDelete)
        {
            _db.DeploymentResults.RemoveRange(job.Results);
        }
        _db.DeploymentJobs.RemoveRange(jobsToDelete);

        await _db.SaveChangesAsync();

        await _auditService.LogAsync(User.Identity?.Name, AuditAction.DeploymentPurge,
            new { JobsDeleted = jobCount, ResultsDeleted = resultCount, OlderThanDays = olderThanDays ?? 30 });

        _logger.LogInformation("User {User} purged {JobCount} deployment jobs ({ResultCount} results) older than {Days} days",
            User.Identity?.Name, jobCount, resultCount, olderThanDays ?? 30);

        return Ok(new PurgeResultDto(jobCount, resultCount, $"Purged {jobCount} jobs and {resultCount} results older than {olderThanDays ?? 30} days"));
    }

    // Scheduled Deployments

    [HttpGet("schedule")]
    public async Task<ActionResult<IEnumerable<ScheduledDeploymentDto>>> GetScheduledDeployments()
    {
        var scheduled = await _db.ScheduledDeployments
            .Include(s => s.Config)
            .Include(s => s.Computers)
                .ThenInclude(c => c.Computer)
            .Where(s => s.Status == "Pending" || s.Status == "Running")
            .OrderBy(s => s.ScheduledAt)
            .Select(s => new ScheduledDeploymentDto(
                s.Id,
                s.Operation,
                s.ConfigId,
                s.Config != null ? s.Config.Filename : null,
                s.Config != null ? s.Config.Tag : null,
                s.ScheduledAt,
                s.CreatedBy,
                s.CreatedAt,
                s.Status,
                s.DeploymentJobId,
                s.Computers.Select(c => new ScheduledComputerDto(c.ComputerId, c.Computer.Hostname)).ToList()))
            .ToListAsync();

        return Ok(scheduled);
    }

    [HttpPost("schedule")]
    [Authorize(Policy = "RequireOperator")]
    public async Task<ActionResult<ScheduledDeploymentDto>> CreateScheduledDeployment([FromBody] CreateScheduledDeploymentRequest request)
    {
        if (request.ComputerIds.Length == 0)
            return BadRequest("No computers specified");

        if (request.ScheduledAt <= DateTime.UtcNow)
            return BadRequest("Scheduled time must be in the future");

        var scheduled = new ScheduledDeployment
        {
            Operation = request.Operation,
            ConfigId = request.ConfigId,
            ScheduledAt = request.ScheduledAt,
            CreatedBy = User.Identity?.Name,
            Status = "Pending"
        };

        _db.ScheduledDeployments.Add(scheduled);
        await _db.SaveChangesAsync();

        // Add computers
        foreach (var computerId in request.ComputerIds)
        {
            scheduled.Computers.Add(new ScheduledDeploymentComputer
            {
                ScheduledDeploymentId = scheduled.Id,
                ComputerId = computerId
            });
        }

        await _db.SaveChangesAsync();

        await _auditService.LogAsync(User.Identity?.Name, AuditAction.ScheduledDeploymentCreate,
            new { ScheduledDeploymentId = scheduled.Id, Operation = request.Operation, ComputerCount = request.ComputerIds.Length, ScheduledAt = request.ScheduledAt });

        _logger.LogInformation("User {User} scheduled deployment {Id} ({Operation}) for {ScheduledAt} on {Count} computers",
            User.Identity?.Name, scheduled.Id, request.Operation, request.ScheduledAt, request.ComputerIds.Length);

        // Fetch the full data for response
        var config = request.ConfigId.HasValue
            ? await _db.Configs.FindAsync(request.ConfigId.Value)
            : null;

        var computers = await _db.Computers
            .Where(c => request.ComputerIds.Contains(c.Id))
            .Select(c => new ScheduledComputerDto(c.Id, c.Hostname))
            .ToListAsync();

        return CreatedAtAction(nameof(GetScheduledDeployments), null,
            new ScheduledDeploymentDto(
                scheduled.Id,
                scheduled.Operation,
                scheduled.ConfigId,
                config?.Filename,
                config?.Tag,
                scheduled.ScheduledAt,
                scheduled.CreatedBy,
                scheduled.CreatedAt,
                scheduled.Status,
                null,
                computers));
    }

    [HttpDelete("schedule/{id}")]
    [Authorize(Policy = "RequireOperator")]
    public async Task<ActionResult> CancelScheduledDeployment(int id)
    {
        var scheduled = await _db.ScheduledDeployments.FindAsync(id);
        if (scheduled == null)
            return NotFound();

        if (scheduled.Status != "Pending")
            return BadRequest("Can only cancel pending scheduled deployments");

        scheduled.Status = "Cancelled";
        await _db.SaveChangesAsync();

        await _auditService.LogAsync(User.Identity?.Name, AuditAction.ScheduledDeploymentCancel,
            new { ScheduledDeploymentId = id });

        _logger.LogInformation("User {User} cancelled scheduled deployment {Id}", User.Identity?.Name, id);

        return NoContent();
    }
}

public record DeploymentJobDto(
    int Id,
    string Operation,
    int? ConfigId,
    string? ConfigFilename,
    string? StartedBy,
    DateTime StartedAt,
    DateTime? CompletedAt,
    string Status,
    int SuccessCount,
    int FailureCount,
    int TotalCount);

public record DeploymentJobDetailDto(
    int Id,
    string Operation,
    int? ConfigId,
    string? ConfigFilename,
    string? StartedBy,
    DateTime StartedAt,
    DateTime? CompletedAt,
    string Status,
    List<DeploymentResultDto> Results);

public record DeploymentResultDto(
    int Id,
    int ComputerId,
    string Hostname,
    bool Success,
    string? Message,
    DateTime? CompletedAt);

public record StartDeploymentRequest(
    string Operation,
    int? ConfigId,
    int[] ComputerIds,
    string? SysmonVersion = null);

public record PurgeResultDto(
    int JobsDeleted,
    int ResultsDeleted,
    string Message);

public record ScheduledDeploymentDto(
    int Id,
    string Operation,
    int? ConfigId,
    string? ConfigFilename,
    string? ConfigTag,
    DateTime ScheduledAt,
    string? CreatedBy,
    DateTime CreatedAt,
    string Status,
    int? DeploymentJobId,
    List<ScheduledComputerDto> Computers);

public record ScheduledComputerDto(int ComputerId, string Hostname);

public record CreateScheduledDeploymentRequest(
    string Operation,
    int? ConfigId,
    DateTime ScheduledAt,
    int[] ComputerIds);
