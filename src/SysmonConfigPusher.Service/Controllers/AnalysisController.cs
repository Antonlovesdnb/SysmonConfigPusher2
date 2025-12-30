using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SysmonConfigPusher.Core.Interfaces;

namespace SysmonConfigPusher.Service.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "RequireViewer")]
public class AnalysisController : ControllerBase
{
    private readonly INoiseAnalysisService _noiseAnalysisService;
    private readonly IAuditService _auditService;
    private readonly ILogger<AnalysisController> _logger;

    public AnalysisController(
        INoiseAnalysisService noiseAnalysisService,
        IAuditService auditService,
        ILogger<AnalysisController> logger)
    {
        _noiseAnalysisService = noiseAnalysisService;
        _auditService = auditService;
        _logger = logger;
    }

    /// <summary>
    /// Start a noise analysis for a single host.
    /// </summary>
    [HttpPost("noise")]
    [Authorize(Policy = "RequireOperator")]
    public async Task<ActionResult<NoiseAnalysisResponse>> StartNoiseAnalysis(
        [FromBody] StartNoiseAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting noise analysis for computer {ComputerId} ({Hours}h) by {User}",
            request.ComputerId, request.TimeRangeHours, User.Identity?.Name);

        var result = await _noiseAnalysisService.AnalyzeHostAsync(
            request.ComputerId, request.TimeRangeHours, cancellationToken);

        if (!result.Success)
        {
            return BadRequest(new NoiseAnalysisResponse(
                false, null, [], [], result.ErrorMessage));
        }

        await _auditService.LogAsync(User.Identity?.Name, AuditAction.NoiseAnalysisStart,
            new { ComputerId = request.ComputerId, TimeRangeHours = request.TimeRangeHours, RunId = result.Run?.Id });

        var allResults = result.Results.Select(r => new NoiseResultResponseDto(
            r.Id,
            r.EventId,
            r.EventType,
            r.GroupingKey,
            r.EventCount,
            r.EventsPerHour,
            r.NoiseScore,
            r.NoiseLevel.ToString(),
            r.SuggestedExclusion,
            r.AvailableFields)).ToList();

        var eventSummaries = BuildEventSummaries(allResults, result.Run?.TimeRangeHours ?? 24);

        return Ok(new NoiseAnalysisResponse(
            true,
            result.Run != null ? new NoiseAnalysisRunDto(
                result.Run.Id,
                result.Run.ComputerId,
                result.Run.Computer?.Hostname ?? "Unknown",
                result.Run.TimeRangeHours,
                result.Run.TotalEvents,
                result.Run.AnalyzedAt) : null,
            allResults,
            eventSummaries,
            null));
    }

    /// <summary>
    /// Get a previous noise analysis run.
    /// </summary>
    [HttpGet("noise/{runId}")]
    public async Task<ActionResult<NoiseAnalysisResponse>> GetNoiseAnalysis(
        int runId,
        CancellationToken cancellationToken)
    {
        var result = await _noiseAnalysisService.GetAnalysisRunAsync(runId, cancellationToken);

        if (result == null)
            return NotFound();

        var allResults = result.Results.Select(r => new NoiseResultResponseDto(
            r.Id,
            r.EventId,
            r.EventType,
            r.GroupingKey,
            r.EventCount,
            r.EventsPerHour,
            r.NoiseScore,
            r.NoiseLevel.ToString(),
            r.SuggestedExclusion,
            r.AvailableFields)).ToList();

        var eventSummaries = BuildEventSummaries(allResults, result.Run?.TimeRangeHours ?? 24);

        return Ok(new NoiseAnalysisResponse(
            true,
            result.Run != null ? new NoiseAnalysisRunDto(
                result.Run.Id,
                result.Run.ComputerId,
                result.Run.Computer?.Hostname ?? "Unknown",
                result.Run.TimeRangeHours,
                result.Run.TotalEvents,
                result.Run.AnalyzedAt) : null,
            allResults,
            eventSummaries,
            null));
    }

    /// <summary>
    /// Get noise analysis history.
    /// </summary>
    [HttpGet("noise/history")]
    public async Task<ActionResult<List<NoiseAnalysisRunDto>>> GetNoiseAnalysisHistory(
        [FromQuery] int? computerId = null,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var runs = await _noiseAnalysisService.GetAnalysisHistoryAsync(
            computerId, limit, cancellationToken);

        return Ok(runs.Select(r => new NoiseAnalysisRunDto(
            r.Id,
            r.ComputerId,
            r.Computer?.Hostname ?? "Unknown",
            r.TimeRangeHours,
            r.TotalEvents,
            r.AnalyzedAt)).ToList());
    }

    /// <summary>
    /// Compare noise across multiple hosts.
    /// </summary>
    [HttpPost("compare")]
    [Authorize(Policy = "RequireOperator")]
    public async Task<ActionResult<CrossHostAnalysisResponse>> CompareHosts(
        [FromBody] CompareHostsRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ComputerIds.Length < 2)
            return BadRequest("At least 2 computers required for comparison");

        _logger.LogInformation("Starting cross-host analysis for {Count} computers by {User}",
            request.ComputerIds.Length, User.Identity?.Name);

        var result = await _noiseAnalysisService.AnalyzeMultipleHostsAsync(
            request.ComputerIds, request.TimeRangeHours, cancellationToken);

        if (!result.Success)
        {
            return BadRequest(new CrossHostAnalysisResponse(
                false, [], [], result.ErrorMessage));
        }

        return Ok(new CrossHostAnalysisResponse(
            true,
            result.Comparisons.Select(c => new HostComparisonDto(
                c.ComputerId,
                c.Hostname,
                c.TotalEvents,
                c.NoisyPatterns,
                c.VeryNoisyPatterns,
                c.OverallNoiseScore,
                c.TopNoisePatterns.Take(5).Select(r => new NoiseResultResponseDto(
                    r.Id,
                    r.EventId,
                    r.EventType,
                    r.GroupingKey,
                    r.EventCount,
                    r.EventsPerHour,
                    r.NoiseScore,
                    r.NoiseLevel.ToString(),
                    r.SuggestedExclusion,
                    r.AvailableFields)).ToList())).ToList(),
            result.CommonNoisePatterns.ToList(),
            null));
    }

    /// <summary>
    /// Generate exclusion XML from analysis results.
    /// </summary>
    [HttpGet("exclusions/{runId}")]
    public async Task<ActionResult<ExclusionXmlResponse>> GetExclusionXml(
        int runId,
        [FromQuery] double minimumNoiseScore = 0.5,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var xml = await _noiseAnalysisService.GenerateExclusionXmlAsync(
                runId, minimumNoiseScore, cancellationToken);

            return Ok(new ExclusionXmlResponse(true, xml, null));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new ExclusionXmlResponse(false, null, ex.Message));
        }
    }

    /// <summary>
    /// Get noise thresholds for a host role.
    /// </summary>
    [HttpGet("thresholds/{role}")]
    public ActionResult<NoiseThresholdsDto> GetThresholds(string role)
    {
        if (!Enum.TryParse<HostRole>(role, true, out var hostRole))
        {
            return BadRequest($"Invalid role: {role}. Valid values: Workstation, Server, DomainController");
        }

        var thresholds = _noiseAnalysisService.GetThresholdsForRole(hostRole);

        return Ok(new NoiseThresholdsDto(
            role,
            thresholds.ProcessCreatePerHour,
            thresholds.NetworkConnectionPerHour,
            thresholds.ImageLoadedPerHour,
            thresholds.FileCreatePerHour,
            thresholds.DnsQueryPerHour));
    }

    /// <summary>
    /// Delete a specific noise analysis run.
    /// </summary>
    [HttpDelete("noise/{runId}")]
    [Authorize(Policy = "RequireAdmin")]
    public async Task<ActionResult<DeleteAnalysisResponse>> DeleteNoiseAnalysis(
        int runId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Deleting noise analysis run {RunId} by {User}",
            runId, User.Identity?.Name);

        var success = await _noiseAnalysisService.DeleteAnalysisRunAsync(runId, cancellationToken);

        if (!success)
        {
            return NotFound(new DeleteAnalysisResponse(false, "Analysis run not found"));
        }

        await _auditService.LogAsync(User.Identity?.Name, AuditAction.NoiseAnalysisDelete,
            new { RunId = runId });

        return Ok(new DeleteAnalysisResponse(true, "Analysis run deleted"));
    }

    /// <summary>
    /// Purge noise analysis runs older than specified days.
    /// </summary>
    [HttpDelete("noise/purge")]
    [Authorize(Policy = "RequireAdmin")]
    public async Task<ActionResult<PurgeAnalysisResponse>> PurgeNoiseAnalysis(
        [FromQuery] int olderThanDays = 0,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Purging noise analysis runs older than {Days} days by {User}",
            olderThanDays, User.Identity?.Name);

        var count = await _noiseAnalysisService.PurgeAnalysisRunsAsync(olderThanDays, cancellationToken);

        await _auditService.LogAsync(User.Identity?.Name, AuditAction.NoiseAnalysisPurge,
            new { DeletedCount = count, OlderThanDays = olderThanDays });

        var message = olderThanDays > 0
            ? $"Purged {count} analysis runs older than {olderThanDays} days"
            : $"Purged {count} analysis runs";
        return Ok(new PurgeAnalysisResponse(true, count, message));
    }

    private static List<EventTypeSummaryDto> BuildEventSummaries(
        List<NoiseResultResponseDto> results,
        double timeRangeHours)
    {
        return results
            .GroupBy(r => r.EventId)
            .Select(g => new EventTypeSummaryDto(
                g.Key,
                g.First().EventType,
                g.Sum(r => r.EventCount),
                (double)g.Sum(r => r.EventCount) / timeRangeHours,
                g.Count(),
                g.OrderByDescending(r => r.EventCount).Take(10).ToList()))
            .OrderByDescending(s => s.TotalCount)
            .ToList();
    }
}

public record StartNoiseAnalysisRequest(
    int ComputerId,
    double TimeRangeHours = 24);

public record NoiseAnalysisResponse(
    bool Success,
    NoiseAnalysisRunDto? Run,
    List<NoiseResultResponseDto> Results,
    List<EventTypeSummaryDto> EventSummaries,
    string? ErrorMessage);

public record EventTypeSummaryDto(
    int EventId,
    string EventType,
    int TotalCount,
    double EventsPerHour,
    int PatternCount,
    List<NoiseResultResponseDto> TopPatterns);

public record NoiseAnalysisRunDto(
    int Id,
    int ComputerId,
    string Hostname,
    double TimeRangeHours,
    int TotalEvents,
    DateTime AnalyzedAt);

public record NoiseResultResponseDto(
    int Id,
    int EventId,
    string EventType,
    string GroupingKey,
    int EventCount,
    double EventsPerHour,
    double NoiseScore,
    string NoiseLevel,
    string? SuggestedExclusion,
    Dictionary<string, string> AvailableFields);

public record CompareHostsRequest(
    int[] ComputerIds,
    double TimeRangeHours = 24);

public record CrossHostAnalysisResponse(
    bool Success,
    List<HostComparisonDto> Comparisons,
    List<string> CommonNoisePatterns,
    string? ErrorMessage);

public record HostComparisonDto(
    int ComputerId,
    string Hostname,
    int TotalEvents,
    int NoisyPatterns,
    int VeryNoisyPatterns,
    double OverallNoiseScore,
    List<NoiseResultResponseDto> TopNoisePatterns);

public record ExclusionXmlResponse(
    bool Success,
    string? Xml,
    string? ErrorMessage);

public record NoiseThresholdsDto(
    string Role,
    int ProcessCreatePerHour,
    int NetworkConnectionPerHour,
    int ImageLoadedPerHour,
    int FileCreatePerHour,
    int DnsQueryPerHour);

public record DeleteAnalysisResponse(
    bool Success,
    string Message);

public record PurgeAnalysisResponse(
    bool Success,
    int DeletedCount,
    string Message);
