using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SysmonConfigPusher.Core.Interfaces;
using SysmonConfigPusher.Core.Models;
using SysmonConfigPusher.Data;
using SysmonConfigPusher.Shared;

namespace SysmonConfigPusher.Infrastructure.NoiseAnalysis;

/// <summary>
/// Implementation of noise analysis service.
/// </summary>
public class NoiseAnalysisService : INoiseAnalysisService
{
    private readonly SysmonDbContext _db;
    private readonly IEventLogService _eventLogService;
    private readonly ILogger<NoiseAnalysisService> _logger;

    // Timeout for agent command responses
    private const int AgentTimeoutSeconds = 120;
    private const int AgentPollIntervalMs = 500;

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

            EventAggregationResult aggregationResult;

            if (computer.IsAgentManaged)
            {
                // Query events via agent and aggregate server-side
                aggregationResult = await GetAgentEventAggregationsAsync(computer, timeRangeHours, cancellationToken);
            }
            else
            {
                // Get event aggregations from remote host via WMI
                aggregationResult = await _eventLogService.GetEventAggregationsAsync(
                    computer.Hostname, timeRangeHours, cancellationToken);
            }

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

    /// <summary>
    /// Query events from an agent-managed computer and aggregate them server-side.
    /// </summary>
    private async Task<EventAggregationResult> GetAgentEventAggregationsAsync(
        Computer computer,
        double timeRangeHours,
        CancellationToken cancellationToken)
    {
        // Create the query command - request more events for noise analysis
        var queryPayload = new QueryEventsPayload
        {
            TimeRangeHours = (int)Math.Ceiling(timeRangeHours),
            MaxEvents = 10000, // Get a good sample for noise analysis
            EventIds = null // All event types
        };

        var pendingCommand = new AgentPendingCommand
        {
            ComputerId = computer.Id,
            CommandId = Guid.NewGuid().ToString(),
            CommandType = AgentCommandType.QueryEvents.ToString(),
            Payload = JsonSerializer.Serialize(queryPayload),
            CreatedAt = DateTime.UtcNow
        };

        _db.AgentPendingCommands.Add(pendingCommand);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Queued QueryEvents command for noise analysis on agent {Hostname}, waiting for result...",
            computer.Hostname);

        // Wait for result with timeout
        var deadline = DateTime.UtcNow.AddSeconds(AgentTimeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(AgentPollIntervalMs, cancellationToken);

            var cmd = await _db.AgentPendingCommands
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.CommandId == pendingCommand.CommandId, cancellationToken);

            if (cmd?.CompletedAt != null)
            {
                if (cmd.ResultStatus == CommandResultStatus.Success.ToString() && cmd.ResultPayload != null)
                {
                    try
                    {
                        var eventResult = JsonSerializer.Deserialize<AgentEventQueryResult>(
                            cmd.ResultPayload,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                        if (eventResult?.Events != null)
                        {
                            // Aggregate the events server-side
                            var (aggregations, firstSeen, lastSeen) = AggregateAgentEvents(eventResult.Events);
                            var period = lastSeen - firstSeen;
                            return new EventAggregationResult(true, aggregations, eventResult.TotalCount, period, null);
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogError(ex, "Failed to parse event query result from agent");
                        return new EventAggregationResult(false, [], 0, TimeSpan.Zero, $"Failed to parse agent response: {ex.Message}");
                    }
                }
                else
                {
                    return new EventAggregationResult(false, [], 0, TimeSpan.Zero, cmd.ResultMessage ?? "Query failed");
                }
            }
        }

        return new EventAggregationResult(false, [], 0, TimeSpan.Zero,
            "Timeout waiting for agent response. The agent may be offline or busy.");
    }

    /// <summary>
    /// Aggregate raw events from agent into EventAggregation objects for noise analysis.
    /// </summary>
    private static (List<EventAggregation> Aggregations, DateTime FirstSeen, DateTime LastSeen) AggregateAgentEvents(List<AgentSysmonEvent> events)
    {
        if (events.Count == 0)
        {
            return ([], DateTime.UtcNow, DateTime.UtcNow);
        }

        var aggregations = new Dictionary<string, (EventAggregation Agg, DateTime First, DateTime Last, List<string> Samples)>();
        var globalFirstSeen = DateTime.MaxValue;
        var globalLastSeen = DateTime.MinValue;

        foreach (var evt in events)
        {
            if (evt.TimeCreated < globalFirstSeen) globalFirstSeen = evt.TimeCreated;
            if (evt.TimeCreated > globalLastSeen) globalLastSeen = evt.TimeCreated;

            var groupingKey = GetGroupingKey(evt);
            var key = $"{evt.EventId}|{groupingKey}";

            if (!aggregations.TryGetValue(key, out var existing))
            {
                var agg = new EventAggregation(
                    evt.EventId,
                    SysmonEventTypes.GetEventTypeName(evt.EventId),
                    groupingKey,
                    1,
                    evt.TimeCreated,
                    evt.TimeCreated,
                    [],
                    GetAvailableFields(evt));
                aggregations[key] = (agg, evt.TimeCreated, evt.TimeCreated, []);
            }
            else
            {
                var firstSeen = evt.TimeCreated < existing.First ? evt.TimeCreated : existing.First;
                var lastSeen = evt.TimeCreated > existing.Last ? evt.TimeCreated : existing.Last;
                aggregations[key] = (existing.Agg with { Count = existing.Agg.Count + 1, FirstSeen = firstSeen, LastSeen = lastSeen },
                    firstSeen, lastSeen, existing.Samples);
            }
        }

        var result = aggregations.Values.Select(x => x.Agg).ToList();
        return (result, globalFirstSeen, globalLastSeen);
    }

    /// <summary>
    /// Generate a grouping key for an event based on its type.
    /// </summary>
    private static string GetGroupingKey(AgentSysmonEvent evt)
    {
        return evt.EventId switch
        {
            SysmonEventTypes.ProcessCreate =>
                $"Image: {evt.Image ?? "Unknown"}",

            SysmonEventTypes.NetworkConnection =>
                $"Image: {evt.Image ?? "Unknown"} | DestinationIp: {evt.DestinationIp ?? "Unknown"}",

            SysmonEventTypes.ImageLoaded =>
                $"Image: {evt.Image ?? "Unknown"} | ImageLoaded: {evt.ImageLoaded ?? "Unknown"}",

            SysmonEventTypes.FileCreate =>
                $"Image: {evt.Image ?? "Unknown"} | TargetFilename: {GetFileDirectory(evt.TargetFilename) ?? "Unknown"}",

            SysmonEventTypes.DnsQuery =>
                $"Image: {evt.Image ?? "Unknown"} | QueryName: {evt.QueryName ?? "Unknown"}",

            SysmonEventTypes.RegistryObjectCreateDelete or SysmonEventTypes.RegistryValueSet or SysmonEventTypes.RegistryKeyValueRename =>
                $"Image: {evt.Image ?? "Unknown"}",

            SysmonEventTypes.FileCreateStreamHash =>
                $"Image: {evt.Image ?? "Unknown"} | TargetFilename: {GetFileDirectory(evt.TargetFilename) ?? "Unknown"}",

            SysmonEventTypes.CreateRemoteThread =>
                $"SourceImage: {evt.SourceImage ?? evt.Image ?? "Unknown"}",

            SysmonEventTypes.ProcessAccess =>
                $"SourceImage: {evt.SourceImage ?? evt.Image ?? "Unknown"} | TargetImage: {evt.TargetImage ?? "Unknown"}",

            _ => $"Image: {evt.Image ?? "Unknown"}"
        };
    }

    /// <summary>
    /// Get the directory portion of a file path for grouping.
    /// </summary>
    private static string? GetFileDirectory(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return null;
        try
        {
            var dir = Path.GetDirectoryName(filePath);
            return string.IsNullOrEmpty(dir) ? filePath : dir;
        }
        catch
        {
            return filePath;
        }
    }

    /// <summary>
    /// Extract available fields from an event for display.
    /// </summary>
    private static Dictionary<string, string> GetAvailableFields(AgentSysmonEvent evt)
    {
        var fields = new Dictionary<string, string>();

        if (!string.IsNullOrEmpty(evt.Image))
            fields["Image"] = evt.Image;
        if (!string.IsNullOrEmpty(evt.ProcessName))
            fields["ProcessName"] = evt.ProcessName;
        if (!string.IsNullOrEmpty(evt.CommandLine))
            fields["CommandLine"] = evt.CommandLine;
        if (!string.IsNullOrEmpty(evt.User))
            fields["User"] = evt.User;
        if (!string.IsNullOrEmpty(evt.ParentImage))
            fields["ParentImage"] = evt.ParentImage;
        if (!string.IsNullOrEmpty(evt.DestinationIp))
            fields["DestinationIp"] = evt.DestinationIp;
        if (evt.DestinationPort.HasValue)
            fields["DestinationPort"] = evt.DestinationPort.Value.ToString();
        if (!string.IsNullOrEmpty(evt.DestinationHostname))
            fields["DestinationHostname"] = evt.DestinationHostname;
        if (!string.IsNullOrEmpty(evt.TargetFilename))
            fields["TargetFilename"] = evt.TargetFilename;
        if (!string.IsNullOrEmpty(evt.QueryName))
            fields["QueryName"] = evt.QueryName;
        if (!string.IsNullOrEmpty(evt.ImageLoaded))
            fields["ImageLoaded"] = evt.ImageLoaded;

        // ProcessAccess-specific fields
        if (!string.IsNullOrEmpty(evt.SourceImage))
            fields["SourceImage"] = evt.SourceImage;
        if (!string.IsNullOrEmpty(evt.TargetImage))
            fields["TargetImage"] = evt.TargetImage;
        if (!string.IsNullOrEmpty(evt.GrantedAccess))
            fields["GrantedAccess"] = evt.GrantedAccess;

        return fields;
    }
}

/// <summary>
/// Result from agent event query (duplicated from EventsController for now)
/// </summary>
internal class AgentEventQueryResult
{
    public int TotalCount { get; set; }
    public int ReturnedCount { get; set; }
    public List<AgentSysmonEvent> Events { get; set; } = [];
}

internal class AgentSysmonEvent
{
    public int EventId { get; set; }
    public string? EventType { get; set; }
    public DateTime TimeCreated { get; set; }
    public string? ProcessName { get; set; }
    public int? ProcessId { get; set; }
    public string? Image { get; set; }
    public string? CommandLine { get; set; }
    public string? User { get; set; }
    public string? ParentProcessName { get; set; }
    public int? ParentProcessId { get; set; }
    public string? ParentImage { get; set; }
    public string? ParentCommandLine { get; set; }
    public string? DestinationIp { get; set; }
    public int? DestinationPort { get; set; }
    public string? DestinationHostname { get; set; }
    public string? SourceIp { get; set; }
    public int? SourcePort { get; set; }
    public string? Protocol { get; set; }
    public string? TargetFilename { get; set; }
    public string? QueryName { get; set; }
    public string? QueryResults { get; set; }
    public string? ImageLoaded { get; set; }
    public string? Signature { get; set; }
    public string? RawXml { get; set; }

    // ProcessAccess-specific fields
    public string? SourceImage { get; set; }
    public string? TargetImage { get; set; }
    public string? GrantedAccess { get; set; }
    public string? CallTrace { get; set; }
}
