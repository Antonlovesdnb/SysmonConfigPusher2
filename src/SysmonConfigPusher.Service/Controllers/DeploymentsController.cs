using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SysmonConfigPusher.Data;
using SysmonConfigPusher.Core.Models;
using SysmonConfigPusher.Core.Interfaces;

namespace SysmonConfigPusher.Service.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DeploymentsController : ControllerBase
{
    private readonly SysmonDbContext _db;
    private readonly IDeploymentQueue _queue;
    private readonly ILogger<DeploymentsController> _logger;

    public DeploymentsController(
        SysmonDbContext db,
        IDeploymentQueue queue,
        ILogger<DeploymentsController> logger)
    {
        _db = db;
        _queue = queue;
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
    public async Task<ActionResult<DeploymentJobDto>> StartDeployment([FromBody] StartDeploymentRequest request)
    {
        if (request.ComputerIds.Length == 0)
            return BadRequest("No computers specified");

        var job = new DeploymentJob
        {
            Operation = request.Operation,
            ConfigId = request.ConfigId,
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

        _logger.LogInformation("User {User} cancelled deployment job {JobId}", User.Identity?.Name, id);

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
    int[] ComputerIds);
