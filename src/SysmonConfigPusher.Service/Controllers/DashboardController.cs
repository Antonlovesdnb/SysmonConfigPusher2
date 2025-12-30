using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SysmonConfigPusher.Data;

namespace SysmonConfigPusher.Service.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "RequireViewer")]
public class DashboardController : ControllerBase
{
    private readonly SysmonDbContext _db;
    private readonly ILogger<DashboardController> _logger;

    public DashboardController(SysmonDbContext db, ILogger<DashboardController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Get dashboard statistics.
    /// </summary>
    [HttpGet("stats")]
    public async Task<ActionResult<DashboardStatsResponse>> GetStats(CancellationToken cancellationToken)
    {
        try
        {
            // Computer stats
            var totalComputers = await _db.Computers.CountAsync(cancellationToken);
            var computersWithSysmon = await _db.Computers
                .CountAsync(c => c.SysmonVersion != null, cancellationToken);
            var computersWithoutSysmon = totalComputers - computersWithSysmon;

            // Config stats
            var totalConfigs = await _db.Configs.CountAsync(c => c.IsActive, cancellationToken);

            // Deployment stats (last 24 hours and last 7 days)
            var now = DateTime.UtcNow;
            var last24Hours = now.AddHours(-24);
            var last7Days = now.AddDays(-7);

            var deploymentsLast24h = await _db.DeploymentJobs
                .CountAsync(j => j.StartedAt >= last24Hours, cancellationToken);
            var deploymentsLast7d = await _db.DeploymentJobs
                .CountAsync(j => j.StartedAt >= last7Days, cancellationToken);

            // Recent deployments
            var recentDeployments = await _db.DeploymentJobs
                .OrderByDescending(j => j.StartedAt)
                .Take(5)
                .Select(j => new RecentDeploymentDto(
                    j.Id,
                    j.Operation,
                    j.StartedBy ?? "Unknown",
                    j.StartedAt,
                    j.CompletedAt,
                    j.Status ?? "Unknown"))
                .ToListAsync(cancellationToken);

            // Noise analysis stats
            var noiseAnalysesLast7d = await _db.NoiseAnalysisRuns
                .CountAsync(r => r.AnalyzedAt >= last7Days, cancellationToken);

            var recentNoiseAnalyses = await _db.NoiseAnalysisRuns
                .Include(r => r.Computer)
                .OrderByDescending(r => r.AnalyzedAt)
                .Take(5)
                .Select(r => new RecentNoiseAnalysisDto(
                    r.Id,
                    r.Computer != null ? r.Computer.Hostname : "Unknown",
                    r.TotalEvents,
                    r.AnalyzedAt))
                .ToListAsync(cancellationToken);

            // Deployment success rate (last 7 days)
            var recentResults = await _db.DeploymentResults
                .Where(r => r.CompletedAt >= last7Days)
                .ToListAsync(cancellationToken);

            var totalResults = recentResults.Count;
            var successfulResults = recentResults.Count(r => r.Success);
            var successRate = totalResults > 0
                ? Math.Round((double)successfulResults / totalResults * 100, 1)
                : 100.0;

            // Sysmon version distribution (fetch then group in memory)
            var computersWithVersions = await _db.Computers
                .Where(c => c.SysmonVersion != null)
                .Select(c => c.SysmonVersion!)
                .ToListAsync(cancellationToken);

            var versionDistribution = computersWithVersions
                .GroupBy(v => v)
                .Select(g => new VersionDistributionDto(g.Key, g.Count()))
                .OrderByDescending(v => v.Count)
                .Take(5)
                .ToList();

            return Ok(new DashboardStatsResponse(
                new ComputerStatsDto(totalComputers, computersWithSysmon, computersWithoutSysmon),
                totalConfigs,
                new DeploymentStatsDto(deploymentsLast24h, deploymentsLast7d, successRate),
                recentDeployments,
                new NoiseAnalysisStatsDto(noiseAnalysesLast7d),
                recentNoiseAnalyses,
                versionDistribution));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get dashboard stats");
            return StatusCode(500, "Failed to retrieve dashboard statistics");
        }
    }
}

public record DashboardStatsResponse(
    ComputerStatsDto Computers,
    int TotalConfigs,
    DeploymentStatsDto Deployments,
    List<RecentDeploymentDto> RecentDeployments,
    NoiseAnalysisStatsDto NoiseAnalysis,
    List<RecentNoiseAnalysisDto> RecentNoiseAnalyses,
    List<VersionDistributionDto> SysmonVersions);

public record ComputerStatsDto(
    int Total,
    int WithSysmon,
    int WithoutSysmon);

public record DeploymentStatsDto(
    int Last24Hours,
    int Last7Days,
    double SuccessRate);

public record RecentDeploymentDto(
    int Id,
    string Operation,
    string StartedBy,
    DateTime StartedAt,
    DateTime? CompletedAt,
    string Status);

public record NoiseAnalysisStatsDto(
    int Last7Days);

public record RecentNoiseAnalysisDto(
    int Id,
    string Hostname,
    int TotalEvents,
    DateTime AnalyzedAt);

public record VersionDistributionDto(
    string Version,
    int Count);
