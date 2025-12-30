using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SysmonConfigPusher.Core.Interfaces;
using SysmonConfigPusher.Core.Models;
using SysmonConfigPusher.Data;

namespace SysmonConfigPusher.Infrastructure.NoiseAnalysis;

/// <summary>
/// Implementation of noise analysis service.
/// </summary>
public class NoiseAnalysisService : INoiseAnalysisService
{
    private readonly SysmonDbContext _db;
    private readonly IEventLogService _eventLogService;
    private readonly ILogger<NoiseAnalysisService> _logger;

    // Role-based thresholds from CLAUDE.md spec
    private static readonly NoiseThresholds WorkstationThresholds = new(200, 500, 2000, 1000, 300);
    private static readonly NoiseThresholds ServerThresholds = new(500, 2000, 5000, 5000, 500);
    private static readonly NoiseThresholds DomainControllerThresholds = new(1000, 5000, 10000, 10000, 2000);

    public NoiseAnalysisService(
        SysmonDbContext db,
        IEventLogService eventLogService,
        ILogger<NoiseAnalysisService> logger)
    {
        _db = db;
        _eventLogService = eventLogService;
        _logger = logger;
    }

    public async Task<NoiseAnalysisRunResult> AnalyzeHostAsync(
        int computerId,
        double timeRangeHours,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var computer = await _db.Computers.FindAsync([computerId], cancellationToken);
            if (computer == null)
            {
                return new NoiseAnalysisRunResult(false, null, [], "Computer not found");
            }

            _logger.LogInformation("Starting noise analysis for {Hostname} ({Hours}h)", computer.Hostname, timeRangeHours);

            // Get event aggregations from remote host
            var aggregationResult = await _eventLogService.GetEventAggregationsAsync(
                computer.Hostname, timeRangeHours, cancellationToken);

            if (!aggregationResult.Success)
            {
                return new NoiseAnalysisRunResult(false, null, [], aggregationResult.ErrorMessage);
            }

            // Determine host role and thresholds
            var hostRole = await DetermineHostRoleAsync(computerId, cancellationToken);
            var thresholds = GetThresholdsForRole(hostRole);

            // Create analysis run
            var run = new NoiseAnalysisRun
            {
                ComputerId = computerId,
                TimeRangeHours = timeRangeHours,
                TotalEvents = aggregationResult.TotalEventsAnalyzed,
                AnalyzedAt = DateTime.UtcNow
            };

            _db.NoiseAnalysisRuns.Add(run);
            await _db.SaveChangesAsync(cancellationToken);

            // Calculate noise scores and create results
            var results = new List<NoiseResultDto>();

            foreach (var aggregation in aggregationResult.Aggregations)
            {
                var threshold = thresholds.GetThreshold(aggregation.EventId);
                var eventsPerHour = (double)aggregation.Count / timeRangeHours;
                var noiseScore = CalculateNoiseScore(eventsPerHour, threshold);
                var noiseLevel = GetNoiseLevel(noiseScore);

                var suggestedExclusion = noiseScore >= 0.5
                    ? GenerateExclusionRule(aggregation)
                    : null;

                var noiseResult = new NoiseResult
                {
                    RunId = run.Id,
                    EventId = aggregation.EventId,
                    GroupingKey = aggregation.GroupingKey,
                    EventCount = aggregation.Count,
                    NoiseScore = noiseScore,
                    SuggestedExclusion = suggestedExclusion
                };

                _db.NoiseResults.Add(noiseResult);

                results.Add(new NoiseResultDto(
                    noiseResult.Id,
                    aggregation.EventId,
                    aggregation.EventType,
                    aggregation.GroupingKey,
                    aggregation.Count,
                    eventsPerHour,
                    noiseScore,
                    noiseLevel,
                    suggestedExclusion,
                    aggregation.AvailableFields));
            }

            await _db.SaveChangesAsync(cancellationToken);

            // Sort by noise score descending
            results = results.OrderByDescending(r => r.NoiseScore).ToList();

            _logger.LogInformation("Completed noise analysis for {Hostname}: {Total} events, {Noisy} noisy patterns",
                computer.Hostname, run.TotalEvents, results.Count(r => r.NoiseLevel != NoiseLevel.Normal));

            return new NoiseAnalysisRunResult(true, run, results, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to analyze noise for computer {ComputerId}", computerId);
            return new NoiseAnalysisRunResult(false, null, [], ex.Message);
        }
    }

    public async Task<CrossHostAnalysisResult> AnalyzeMultipleHostsAsync(
        IEnumerable<int> computerIds,
        double timeRangeHours,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var comparisons = new List<CrossHostComparison>();
            var allNoisePatterns = new Dictionary<string, int>(); // Pattern -> count of hosts with this pattern

            foreach (var computerId in computerIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var computer = await _db.Computers.FindAsync([computerId], cancellationToken);
                if (computer == null) continue;

                var result = await AnalyzeHostAsync(computerId, timeRangeHours, cancellationToken);
                if (!result.Success || result.Run == null) continue;

                var noisyPatterns = result.Results.Where(r => r.NoiseLevel == NoiseLevel.Noisy).ToList();
                var veryNoisyPatterns = result.Results.Where(r => r.NoiseLevel == NoiseLevel.VeryNoisy).ToList();

                // Track common patterns
                foreach (var pattern in result.Results.Where(r => r.NoiseScore >= 0.5))
                {
                    var key = $"{pattern.EventId}:{pattern.GroupingKey}";
                    allNoisePatterns[key] = allNoisePatterns.GetValueOrDefault(key) + 1;
                }

                comparisons.Add(new CrossHostComparison(
                    computerId,
                    computer.Hostname,
                    result.Run.TotalEvents,
                    noisyPatterns.Count,
                    veryNoisyPatterns.Count,
                    result.Results.Count > 0 ? result.Results.Average(r => r.NoiseScore) : 0,
                    result.Results.Take(10).ToList()));
            }

            // Identify common noise patterns (appearing in more than half of hosts)
            var hostCount = comparisons.Count;
            var commonPatterns = allNoisePatterns
                .Where(kvp => kvp.Value > hostCount / 2)
                .OrderByDescending(kvp => kvp.Value)
                .Select(kvp => kvp.Key)
                .ToList();

            return new CrossHostAnalysisResult(true, comparisons, commonPatterns, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform cross-host analysis");
            return new CrossHostAnalysisResult(false, [], [], ex.Message);
        }
    }

    public async Task<string> GenerateExclusionXmlAsync(
        int runId,
        double minimumNoiseScore = 0.5,
        CancellationToken cancellationToken = default)
    {
        var run = await _db.NoiseAnalysisRuns
            .Include(r => r.Results)
            .FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);

        if (run == null)
        {
            throw new InvalidOperationException($"Analysis run {runId} not found");
        }

        var noisyResults = run.Results
            .Where(r => r.NoiseScore >= minimumNoiseScore)
            .OrderByDescending(r => r.NoiseScore)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("<!-- Generated Sysmon Exclusion Rules -->");
        sb.AppendLine($"<!-- Based on noise analysis run {runId} -->");
        sb.AppendLine($"<!-- Minimum noise score: {minimumNoiseScore:F2} -->");
        sb.AppendLine();

        // Group by event type
        var byEventType = noisyResults.GroupBy(r => r.EventId);

        foreach (var group in byEventType)
        {
            var eventTypeName = SysmonEventTypes.GetEventTypeName(group.Key);
            sb.AppendLine($"<!-- {eventTypeName} exclusions -->");

            foreach (var result in group)
            {
                if (!string.IsNullOrEmpty(result.SuggestedExclusion))
                {
                    sb.AppendLine($"<!-- Score: {result.NoiseScore:F2}, Count: {result.EventCount} -->");
                    sb.AppendLine(result.SuggestedExclusion);
                    sb.AppendLine();
                }
            }
        }

        return sb.ToString();
    }

    public async Task<IReadOnlyList<NoiseAnalysisRun>> GetAnalysisHistoryAsync(
        int? computerId = null,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var query = _db.NoiseAnalysisRuns
            .Include(r => r.Computer)
            .AsQueryable();

        if (computerId.HasValue)
        {
            query = query.Where(r => r.ComputerId == computerId.Value);
        }

        return await query
            .OrderByDescending(r => r.AnalyzedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<NoiseAnalysisRunResult?> GetAnalysisRunAsync(
        int runId,
        CancellationToken cancellationToken = default)
    {
        var run = await _db.NoiseAnalysisRuns
            .Include(r => r.Computer)
            .Include(r => r.Results)
            .FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);

        if (run == null)
        {
            return null;
        }

        var hostRole = await DetermineHostRoleAsync(run.ComputerId, cancellationToken);
        var thresholds = GetThresholdsForRole(hostRole);

        var results = run.Results
            .Select(r =>
            {
                var eventsPerHour = (double)r.EventCount / run.TimeRangeHours;
                // Parse grouping key to extract fields for historical data
                var fields = ParseGroupingKeyToFields(r.GroupingKey);
                return new NoiseResultDto(
                    r.Id,
                    r.EventId,
                    SysmonEventTypes.GetEventTypeName(r.EventId),
                    r.GroupingKey,
                    r.EventCount,
                    eventsPerHour,
                    r.NoiseScore,
                    GetNoiseLevel(r.NoiseScore),
                    r.SuggestedExclusion,
                    fields);
            })
            .OrderByDescending(r => r.NoiseScore)
            .ToList();

        return new NoiseAnalysisRunResult(true, run, results, null);
    }

    public NoiseThresholds GetThresholdsForRole(HostRole role)
    {
        return role switch
        {
            HostRole.DomainController => DomainControllerThresholds,
            HostRole.Server => ServerThresholds,
            _ => WorkstationThresholds
        };
    }

    public async Task<HostRole> DetermineHostRoleAsync(
        int computerId,
        CancellationToken cancellationToken = default)
    {
        var computer = await _db.Computers.FindAsync([computerId], cancellationToken);
        if (computer == null)
        {
            return HostRole.Workstation;
        }

        // Check if it's a domain controller
        if (computer.OperatingSystem?.Contains("Domain Controller", StringComparison.OrdinalIgnoreCase) == true ||
            computer.DistinguishedName?.Contains("Domain Controllers", StringComparison.OrdinalIgnoreCase) == true)
        {
            return HostRole.DomainController;
        }

        // Check if it's a server
        if (computer.OperatingSystem?.Contains("Server", StringComparison.OrdinalIgnoreCase) == true)
        {
            return HostRole.Server;
        }

        return HostRole.Workstation;
    }

    public async Task<bool> DeleteAnalysisRunAsync(
        int runId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var run = await _db.NoiseAnalysisRuns
                .Include(r => r.Results)
                .FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);

            if (run == null)
            {
                return false;
            }

            // Remove all results first
            _db.NoiseResults.RemoveRange(run.Results);

            // Remove the run
            _db.NoiseAnalysisRuns.Remove(run);

            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Deleted noise analysis run {RunId}", runId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete noise analysis run {RunId}", runId);
            return false;
        }
    }

    public async Task<int> PurgeAnalysisRunsAsync(
        int olderThanDays = 0,
        CancellationToken cancellationToken = default)
    {
        try
        {
            DateTime? cutoffDate = olderThanDays > 0
                ? DateTime.UtcNow.AddDays(-olderThanDays)
                : null;

            // Get runs to delete
            var runsQuery = _db.NoiseAnalysisRuns.AsQueryable();
            if (cutoffDate.HasValue)
            {
                runsQuery = runsQuery.Where(r => r.AnalyzedAt < cutoffDate.Value);
            }

            var runIds = await runsQuery.Select(r => r.Id).ToListAsync(cancellationToken);
            var count = runIds.Count;

            if (count == 0)
            {
                _logger.LogInformation("No noise analysis runs to purge (older than {Days} days)", olderThanDays);
                return 0;
            }

            // Delete results first (due to foreign key)
            await _db.NoiseResults
                .Where(r => runIds.Contains(r.RunId))
                .ExecuteDeleteAsync(cancellationToken);

            // Delete the runs
            await _db.NoiseAnalysisRuns
                .Where(r => runIds.Contains(r.Id))
                .ExecuteDeleteAsync(cancellationToken);

            _logger.LogInformation("Purged {Count} noise analysis runs (older than {Days} days)", count, olderThanDays);
            return count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to purge noise analysis runs");
            throw;
        }
    }

    private static double CalculateNoiseScore(double eventsPerHour, int threshold)
    {
        if (threshold <= 0) return 0;

        var ratio = eventsPerHour / threshold;

        // Score mapping:
        // < 1x threshold = 0 - 0.3 (normal)
        // 1x - 2x threshold = 0.3 - 0.5 (borderline)
        // 2x - 5x threshold = 0.5 - 0.7 (noisy)
        // > 5x threshold = 0.7 - 1.0 (very noisy)

        if (ratio < 1) return ratio * 0.3;
        if (ratio < 2) return 0.3 + (ratio - 1) * 0.2;
        if (ratio < 5) return 0.5 + (ratio - 2) / 3 * 0.2;
        return Math.Min(0.7 + (ratio - 5) / 10 * 0.3, 1.0);
    }

    private static NoiseLevel GetNoiseLevel(double noiseScore)
    {
        if (noiseScore >= 0.7) return NoiseLevel.VeryNoisy;
        if (noiseScore >= 0.5) return NoiseLevel.Noisy;
        return NoiseLevel.Normal;
    }

    private static string? GenerateExclusionRule(EventAggregation aggregation)
    {
        var parts = aggregation.GroupingKey.Split('|');
        var image = parts[0];

        return aggregation.EventId switch
        {
            SysmonEventTypes.ProcessCreate =>
                $"<Image condition=\"is\">{EscapeXml(image)}</Image>",

            SysmonEventTypes.NetworkConnection when parts.Length > 1 =>
                $"<Image condition=\"is\">{EscapeXml(image)}</Image>\n" +
                $"<!-- Destination: {EscapeXml(parts[1])} -->",

            SysmonEventTypes.ImageLoaded when parts.Length > 1 =>
                $"<Image condition=\"is\">{EscapeXml(image)}</Image>\n" +
                $"<ImageLoaded condition=\"is\">{EscapeXml(parts[1])}</ImageLoaded>",

            SysmonEventTypes.FileCreate when parts.Length > 1 =>
                $"<Image condition=\"is\">{EscapeXml(image)}</Image>\n" +
                $"<TargetFilename condition=\"begin with\">{EscapeXml(parts[1])}</TargetFilename>",

            SysmonEventTypes.DnsQuery when parts.Length > 1 =>
                $"<Image condition=\"is\">{EscapeXml(image)}</Image>\n" +
                $"<QueryName condition=\"is\">{EscapeXml(parts[1])}</QueryName>",

            _ => $"<Image condition=\"is\">{EscapeXml(image)}</Image>"
        };
    }

    private static string EscapeXml(string value)
    {
        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }

    /// <summary>
    /// Parses a grouping key string back into field:value pairs for historical data.
    /// </summary>
    private static Dictionary<string, string> ParseGroupingKeyToFields(string groupingKey)
    {
        var fields = new Dictionary<string, string>();

        // Format: "FieldName: value | FieldName2: value2"
        var parts = groupingKey.Split(" | ");
        foreach (var part in parts)
        {
            var colonIndex = part.IndexOf(": ", StringComparison.Ordinal);
            if (colonIndex > 0)
            {
                var fieldName = part[..colonIndex].Trim();
                var value = part[(colonIndex + 2)..].Trim();
                if (!string.IsNullOrEmpty(value) && value != "Unknown")
                {
                    fields[fieldName] = value;
                }
            }
        }

        return fields;
    }
}
