using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SysmonConfigPusher.Core.Interfaces;
using SysmonConfigPusher.Core.Models;
using SysmonConfigPusher.Data;
using SysmonConfigPusher.Shared;

namespace SysmonConfigPusher.Service.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "RequireViewer")]
public class EventsController : ControllerBase
{
    private readonly SysmonDbContext _db;
    private readonly IEventLogService _eventLogService;
    private readonly ILogger<EventsController> _logger;

    public EventsController(
        SysmonDbContext db,
        IEventLogService eventLogService,
        ILogger<EventsController> logger)
    {
        _db = db;
        _eventLogService = eventLogService;
        _logger = logger;
    }

    /// <summary>
    /// Query Sysmon events from one or more hosts.
    /// </summary>
    [HttpPost("query")]
    public async Task<ActionResult<EventQueryResponse>> QueryEvents(
        [FromBody] EventQueryRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ComputerIds.Length == 0)
            return BadRequest("No computers specified");

        var computers = await _db.Computers
            .Where(c => request.ComputerIds.Contains(c.Id))
            .ToListAsync(cancellationToken);

        if (computers.Count == 0)
            return NotFound("No computers found");

        _logger.LogInformation("Querying events from {Count} computers by {User}",
            computers.Count, User.Identity?.Name);

        var allEvents = new List<SysmonEventDto>();
        var errors = new List<string>();

        var filter = new EventQueryFilter(
            EventId: request.EventId,
            StartTime: request.StartTime,
            EndTime: request.EndTime,
            ProcessName: request.ProcessName,
            ImagePath: request.ImagePath,
            DestinationIp: request.DestinationIp,
            DnsQueryName: request.DnsQueryName,
            MaxResults: request.MaxResults ?? 500);

        // Separate agent-managed and WMI-managed computers
        var wmiComputers = computers.Where(c => !c.IsAgentManaged).ToList();
        var agentComputers = computers.Where(c => c.IsAgentManaged).ToList();

        // Query WMI-managed hosts in parallel
        var wmiTasks = wmiComputers.Select(async computer =>
        {
            var result = await _eventLogService.QueryEventsAsync(
                computer.Hostname, filter, cancellationToken);

            if (result.Success)
            {
                return (computer.Hostname, Events: result.Events
                    .Select(e => new SysmonEventDto(
                        computer.Id,
                        computer.Hostname,
                        e.EventId,
                        e.EventType,
                        e.TimeCreated,
                        e.ProcessName,
                        e.ProcessId,
                        e.Image,
                        e.CommandLine,
                        e.User,
                        e.ParentProcessName,
                        e.ParentProcessId,
                        e.ParentImage,
                        e.ParentCommandLine,
                        e.DestinationIp,
                        e.DestinationPort,
                        e.DestinationHostname,
                        e.SourceIp,
                        e.SourcePort,
                        e.Protocol,
                        e.TargetFilename,
                        e.QueryName,
                        e.QueryResults,
                        e.ImageLoaded,
                        e.Signature,
                        e.RawXml))
                    .ToList(), Error: (string?)null);
            }
            else
            {
                return (computer.Hostname, Events: new List<SysmonEventDto>(), Error: result.ErrorMessage);
            }
        });

        // Query agent-managed hosts via command queue
        var agentTasks = agentComputers.Select(async computer =>
        {
            var result = await QueryAgentEventsAsync(computer, filter, cancellationToken);
            return (computer.Hostname, Events: result.Events, Error: result.Error);
        });

        var results = await Task.WhenAll(wmiTasks.Concat(agentTasks));

        foreach (var (hostname, events, error) in results)
        {
            allEvents.AddRange(events);
            if (error != null)
            {
                errors.Add($"{hostname}: {error}");
            }
        }

        // Sort by time descending
        allEvents = allEvents.OrderByDescending(e => e.TimeCreated).ToList();

        return Ok(new EventQueryResponse(
            true,
            allEvents,
            allEvents.Count,
            errors.Count > 0 ? string.Join("; ", errors) : null));
    }

    /// <summary>
    /// Get event statistics for a single computer.
    /// </summary>
    [HttpGet("stats/{computerId}")]
    public async Task<ActionResult<EventStatsResponse>> GetEventStats(
        int computerId,
        [FromQuery] int hours = 24,
        CancellationToken cancellationToken = default)
    {
        var computer = await _db.Computers.FindAsync([computerId], cancellationToken);
        if (computer == null)
            return NotFound();

        // For agent-managed computers, use QueryAgentEventsAsync and aggregate results
        if (computer.IsAgentManaged)
        {
            var filter = new EventQueryFilter(
                EventId: null,
                StartTime: DateTime.UtcNow.AddHours(-hours),
                EndTime: DateTime.UtcNow,
                ProcessName: null,
                ImagePath: null,
                DestinationIp: null,
                DnsQueryName: null,
                MaxResults: 10000);

            var (events, error) = await QueryAgentEventsAsync(computer, filter, cancellationToken);

            if (error != null)
            {
                return Ok(new EventStatsResponse(
                    false, computerId, computer.Hostname, 0, [], error));
            }

            var stats = SysmonEventTypes.SupportedEventIds
                .Select(eventId => new EventTypeStat(
                    eventId,
                    SysmonEventTypes.GetEventTypeName(eventId),
                    events.Count(e => e.EventId == eventId)))
                .ToList();

            return Ok(new EventStatsResponse(
                true, computerId, computer.Hostname, events.Count, stats, null));
        }

        var aggregations = await _eventLogService.GetEventAggregationsAsync(
            computer.Hostname, hours, cancellationToken);

        if (!aggregations.Success)
        {
            return Ok(new EventStatsResponse(
                false,
                computerId,
                computer.Hostname,
                0,
                [],
                aggregations.ErrorMessage));
        }

        var wmiStats = SysmonEventTypes.SupportedEventIds
            .Select(eventId =>
            {
                var count = aggregations.Aggregations
                    .Where(a => a.EventId == eventId)
                    .Sum(a => a.Count);
                return new EventTypeStat(
                    eventId,
                    SysmonEventTypes.GetEventTypeName(eventId),
                    count);
            })
            .ToList();

        return Ok(new EventStatsResponse(
            true,
            computerId,
            computer.Hostname,
            aggregations.TotalEventsAnalyzed,
            wmiStats,
            null));
    }

    /// <summary>
    /// Test event log access on a computer.
    /// </summary>
    [HttpGet("test/{computerId}")]
    public async Task<ActionResult<EventLogAccessTestResult>> TestEventLogAccess(
        int computerId,
        CancellationToken cancellationToken)
    {
        var computer = await _db.Computers.FindAsync([computerId], cancellationToken);
        if (computer == null)
            return NotFound();

        if (computer.IsAgentManaged)
        {
            // For agent-managed computers, check if agent is online
            var isOnline = computer.AgentLastHeartbeat.HasValue &&
                (DateTime.UtcNow - computer.AgentLastHeartbeat.Value).TotalMinutes < 5;
            return Ok(new EventLogAccessTestResult(
                computerId,
                computer.Hostname,
                isOnline,
                isOnline ? null : "Agent is offline or hasn't sent a heartbeat recently."));
        }

        var accessible = await _eventLogService.TestEventLogAccessAsync(
            computer.Hostname, cancellationToken);

        return Ok(new EventLogAccessTestResult(
            computerId,
            computer.Hostname,
            accessible,
            accessible ? null : "Unable to access Sysmon event log. Check permissions or Sysmon installation."));
    }

    /// <summary>
    /// Query events from an agent-managed computer by queueing a command and waiting for results.
    /// </summary>
    private async Task<(List<SysmonEventDto> Events, string? Error)> QueryAgentEventsAsync(
        Computer computer,
        EventQueryFilter filter,
        CancellationToken cancellationToken)
    {
        const int timeoutSeconds = 60;
        const int pollIntervalMs = 500;

        // Create the query command
        var queryPayload = new QueryEventsPayload
        {
            TimeRangeHours = filter.StartTime.HasValue
                ? (int)(DateTime.UtcNow - filter.StartTime.Value).TotalHours
                : 24,
            MaxEvents = filter.MaxResults,
            EventIds = filter.EventId.HasValue ? [filter.EventId.Value] : null
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

        _logger.LogInformation("Queued QueryEvents command for agent {Hostname}, waiting for result...",
            computer.Hostname);

        // Wait for result with timeout
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(pollIntervalMs, cancellationToken);

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
                            var events = eventResult.Events.Select(e => new SysmonEventDto(
                                computer.Id,
                                computer.Hostname,
                                e.EventId,
                                e.EventType ?? SysmonEventTypes.GetEventTypeName(e.EventId),
                                e.TimeCreated,
                                e.ProcessName,
                                e.ProcessId,
                                e.Image,
                                e.CommandLine,
                                e.User,
                                e.ParentProcessName,
                                e.ParentProcessId,
                                e.ParentImage,
                                e.ParentCommandLine,
                                e.DestinationIp,
                                e.DestinationPort,
                                e.DestinationHostname,
                                e.SourceIp,
                                e.SourcePort,
                                e.Protocol,
                                e.TargetFilename,
                                e.QueryName,
                                e.QueryResults,
                                e.ImageLoaded,
                                e.Signature,
                                e.RawXml)).ToList();

                            return (events, null);
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogError(ex, "Failed to parse event query result from agent");
                        return ([], $"Failed to parse agent response: {ex.Message}");
                    }
                }
                else
                {
                    return ([], cmd.ResultMessage ?? "Query failed");
                }
            }
        }

        return ([], "Timeout waiting for agent response. The agent may be offline or busy.");
    }
}

/// <summary>
/// Result from agent event query
/// </summary>
public class AgentEventQueryResult
{
    public int TotalCount { get; set; }
    public int ReturnedCount { get; set; }
    public List<AgentSysmonEvent> Events { get; set; } = [];
}

public class AgentSysmonEvent
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
}

public record EventQueryRequest(
    int[] ComputerIds,
    int? EventId = null,
    DateTime? StartTime = null,
    DateTime? EndTime = null,
    string? ProcessName = null,
    string? ImagePath = null,
    string? DestinationIp = null,
    string? DnsQueryName = null,
    int? MaxResults = 500);

public record EventQueryResponse(
    bool Success,
    List<SysmonEventDto> Events,
    int TotalCount,
    string? ErrorMessage);

public record SysmonEventDto(
    int ComputerId,
    string Hostname,
    int EventId,
    string EventType,
    DateTime TimeCreated,
    string? ProcessName,
    int? ProcessId,
    string? Image,
    string? CommandLine,
    string? User,
    string? ParentProcessName,
    int? ParentProcessId,
    string? ParentImage,
    string? ParentCommandLine,
    string? DestinationIp,
    int? DestinationPort,
    string? DestinationHostname,
    string? SourceIp,
    int? SourcePort,
    string? Protocol,
    string? TargetFilename,
    string? QueryName,
    string? QueryResults,
    string? ImageLoaded,
    string? Signature,
    string? RawXml);

public record EventStatsResponse(
    bool Success,
    int ComputerId,
    string Hostname,
    int TotalEvents,
    List<EventTypeStat> EventTypeCounts,
    string? ErrorMessage);

public record EventTypeStat(
    int EventId,
    string EventType,
    int Count);

public record EventLogAccessTestResult(
    int ComputerId,
    string Hostname,
    bool Accessible,
    string? ErrorMessage);
