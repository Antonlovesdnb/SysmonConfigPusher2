using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SysmonConfigPusher.Core.Interfaces;
using SysmonConfigPusher.Data;

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

        // Query each host in parallel
        var tasks = computers.Select(async computer =>
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

        var results = await Task.WhenAll(tasks);

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

        var stats = SysmonEventTypes.SupportedEventIds
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
            stats,
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

        var accessible = await _eventLogService.TestEventLogAccessAsync(
            computer.Hostname, cancellationToken);

        return Ok(new EventLogAccessTestResult(
            computerId,
            computer.Hostname,
            accessible,
            accessible ? null : "Unable to access Sysmon event log. Check permissions or Sysmon installation."));
    }
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
