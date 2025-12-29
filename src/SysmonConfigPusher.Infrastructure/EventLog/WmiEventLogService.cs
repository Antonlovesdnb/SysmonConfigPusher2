using System.Diagnostics.Eventing.Reader;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using SysmonConfigPusher.Core.Interfaces;

namespace SysmonConfigPusher.Infrastructure.EventLog;

/// <summary>
/// Implementation of IEventLogService using remote EventLog queries.
/// </summary>
public class WmiEventLogService : IEventLogService
{
    private readonly ILogger<WmiEventLogService> _logger;
    private const string SysmonLogPath = "Microsoft-Windows-Sysmon/Operational";

    public WmiEventLogService(ILogger<WmiEventLogService> logger)
    {
        _logger = logger;
    }

    public async Task<EventQueryResult> QueryEventsAsync(
        string hostname,
        EventQueryFilter filter,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                _logger.LogDebug("Querying Sysmon events from {Hostname}", hostname);

                var query = BuildXPathQuery(filter);
                var session = new EventLogSession(hostname);
                var eventQuery = new EventLogQuery(SysmonLogPath, PathType.LogName, query)
                {
                    Session = session,
                    ReverseDirection = true
                };

                var events = new List<SysmonEvent>();
                using var reader = new EventLogReader(eventQuery);

                EventRecord? record;
                var count = 0;
                while ((record = reader.ReadEvent()) != null && count < filter.MaxResults)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    using (record)
                    {
                        var sysmonEvent = ParseEventRecord(record);
                        if (sysmonEvent != null && MatchesFilter(sysmonEvent, filter))
                        {
                            events.Add(sysmonEvent);
                            count++;
                        }
                    }
                }

                _logger.LogInformation("Retrieved {Count} events from {Hostname}", events.Count, hostname);
                return new EventQueryResult(true, events, events.Count, null);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Access denied querying events from {Hostname}", hostname);
                return new EventQueryResult(false, [], 0, "Access denied. Ensure you have permissions to read remote event logs.");
            }
            catch (EventLogNotFoundException ex)
            {
                _logger.LogWarning(ex, "Sysmon log not found on {Hostname}", hostname);
                return new EventQueryResult(false, [], 0, "Sysmon event log not found. Ensure Sysmon is installed.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to query events from {Hostname}", hostname);
                return new EventQueryResult(false, [], 0, ex.Message);
            }
        }, cancellationToken);
    }

    public async IAsyncEnumerable<SysmonEvent> StreamEventsAsync(
        string hostname,
        EventQueryFilter filter,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var query = BuildXPathQuery(filter);

        await Task.Yield(); // Ensure async context

        EventLogSession? session = null;
        EventLogReader? reader = null;

        try
        {
            session = new EventLogSession(hostname);
            var eventQuery = new EventLogQuery(SysmonLogPath, PathType.LogName, query)
            {
                Session = session,
                ReverseDirection = true
            };

            reader = new EventLogReader(eventQuery);

            EventRecord? record;
            var count = 0;
            while ((record = reader.ReadEvent()) != null && count < filter.MaxResults)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using (record)
                {
                    var sysmonEvent = ParseEventRecord(record);
                    if (sysmonEvent != null && MatchesFilter(sysmonEvent, filter))
                    {
                        yield return sysmonEvent;
                        count++;
                    }
                }
            }
        }
        finally
        {
            reader?.Dispose();
            session?.Dispose();
        }
    }

    public async Task<EventAggregationResult> GetEventAggregationsAsync(
        string hostname,
        double timeRangeHours,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                _logger.LogDebug("Getting event aggregations from {Hostname} for {Hours} hours", hostname, timeRangeHours);

                var startTime = DateTime.UtcNow.AddHours(-timeRangeHours);
                var filter = new EventQueryFilter(
                    StartTime: startTime,
                    MaxResults: 10000 // Higher limit for aggregation
                );

                var query = BuildXPathQuery(filter);
                var session = new EventLogSession(hostname);
                var eventQuery = new EventLogQuery(SysmonLogPath, PathType.LogName, query)
                {
                    Session = session,
                    ReverseDirection = true
                };

                // Group events by type and grouping key
                var aggregations = new Dictionary<string, EventAggregationBuilder>();
                var totalEvents = 0;

                using var reader = new EventLogReader(eventQuery);
                EventRecord? record;

                while ((record = reader.ReadEvent()) != null)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    using (record)
                    {
                        var sysmonEvent = ParseEventRecord(record);
                        if (sysmonEvent != null && SysmonEventTypes.SupportedEventIds.Contains(sysmonEvent.EventId))
                        {
                            totalEvents++;
                            var groupingKey = GetGroupingKey(sysmonEvent);
                            var key = $"{sysmonEvent.EventId}:{groupingKey}";

                            if (!aggregations.TryGetValue(key, out var builder))
                            {
                                builder = new EventAggregationBuilder(sysmonEvent.EventId, groupingKey);
                                aggregations[key] = builder;
                            }

                            builder.AddEvent(sysmonEvent.TimeCreated, GetSampleValue(sysmonEvent));
                        }
                    }
                }

                var result = aggregations.Values
                    .Select(b => b.Build())
                    .OrderByDescending(a => a.Count)
                    .ToList();

                _logger.LogInformation("Aggregated {Total} events into {Groups} groups from {Hostname}",
                    totalEvents, result.Count, hostname);

                return new EventAggregationResult(
                    true,
                    result,
                    totalEvents,
                    TimeSpan.FromHours(timeRangeHours),
                    null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get event aggregations from {Hostname}", hostname);
                return new EventAggregationResult(false, [], 0, TimeSpan.Zero, ex.Message);
            }
        }, cancellationToken);
    }

    public async Task<bool> TestEventLogAccessAsync(
        string hostname,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                _logger.LogDebug("Testing event log access on {Hostname}", hostname);

                var session = new EventLogSession(hostname);
                var query = new EventLogQuery(SysmonLogPath, PathType.LogName, "*[System[TimeCreated[timediff(@SystemTime) <= 86400000]]]")
                {
                    Session = session
                };

                using var reader = new EventLogReader(query);
                var record = reader.ReadEvent();
                record?.Dispose();

                _logger.LogDebug("Event log access test passed for {Hostname}", hostname);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Event log access test failed for {Hostname}", hostname);
                return false;
            }
        }, cancellationToken);
    }

    private static string BuildXPathQuery(EventQueryFilter filter)
    {
        var conditions = new List<string>();

        // Use timediff for time-based filtering (in milliseconds)
        // TimeCreated[timediff(@SystemTime) <= X] where X is milliseconds ago
        if (filter.StartTime.HasValue)
        {
            var millisAgo = (long)(DateTime.UtcNow - filter.StartTime.Value.ToUniversalTime()).TotalMilliseconds;
            if (millisAgo > 0)
            {
                conditions.Add($"TimeCreated[timediff(@SystemTime) <= {millisAgo}]");
            }
        }

        if (filter.EventId.HasValue)
        {
            conditions.Add($"EventID={filter.EventId.Value}");
        }

        // Build the query - if no event ID filter, don't add event ID condition
        // (let the parser filter by supported IDs to avoid overly long queries)
        string systemConditions;
        if (conditions.Count > 0)
        {
            systemConditions = string.Join(" and ", conditions);
        }
        else
        {
            // Default: get all events from last 24 hours if no filter
            systemConditions = "TimeCreated[timediff(@SystemTime) <= 86400000]";
        }

        return $"*[System[{systemConditions}]]";
    }

    private static bool MatchesFilter(SysmonEvent evt, EventQueryFilter filter)
    {
        if (!string.IsNullOrEmpty(filter.ProcessName) &&
            evt.ProcessName?.Contains(filter.ProcessName, StringComparison.OrdinalIgnoreCase) != true)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(filter.ImagePath) &&
            evt.Image?.Contains(filter.ImagePath, StringComparison.OrdinalIgnoreCase) != true)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(filter.DestinationIp) &&
            evt.DestinationIp?.Contains(filter.DestinationIp, StringComparison.OrdinalIgnoreCase) != true)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(filter.DnsQueryName) &&
            evt.QueryName?.Contains(filter.DnsQueryName, StringComparison.OrdinalIgnoreCase) != true)
        {
            return false;
        }

        return true;
    }

    private SysmonEvent? ParseEventRecord(EventRecord record)
    {
        try
        {
            var xml = record.ToXml();
            var doc = XDocument.Parse(xml);

            var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;
            var eventData = doc.Descendants(ns + "Data")
                .Where(d => d.Attribute("Name") != null)
                .ToDictionary(
                    d => d.Attribute("Name")!.Value,
                    d => d.Value);

            var eventId = record.Id;
            var eventType = SysmonEventTypes.GetEventTypeName(eventId);

            return new SysmonEvent(
                EventId: eventId,
                EventType: eventType,
                TimeCreated: record.TimeCreated ?? DateTime.UtcNow,
                ProcessName: GetProcessName(eventData.GetValueOrDefault("Image")),
                ProcessId: TryParseInt(eventData.GetValueOrDefault("ProcessId")),
                Image: eventData.GetValueOrDefault("Image"),
                CommandLine: eventData.GetValueOrDefault("CommandLine"),
                User: eventData.GetValueOrDefault("User"),
                ParentProcessName: GetProcessName(eventData.GetValueOrDefault("ParentImage")),
                ParentProcessId: TryParseInt(eventData.GetValueOrDefault("ParentProcessId")),
                ParentImage: eventData.GetValueOrDefault("ParentImage"),
                ParentCommandLine: eventData.GetValueOrDefault("ParentCommandLine"),
                DestinationIp: eventData.GetValueOrDefault("DestinationIp"),
                DestinationPort: TryParseInt(eventData.GetValueOrDefault("DestinationPort")),
                DestinationHostname: eventData.GetValueOrDefault("DestinationHostname"),
                SourceIp: eventData.GetValueOrDefault("SourceIp"),
                SourcePort: TryParseInt(eventData.GetValueOrDefault("SourcePort")),
                Protocol: eventData.GetValueOrDefault("Protocol"),
                TargetFilename: eventData.GetValueOrDefault("TargetFilename"),
                QueryName: eventData.GetValueOrDefault("QueryName"),
                QueryResults: eventData.GetValueOrDefault("QueryResults"),
                ImageLoaded: eventData.GetValueOrDefault("ImageLoaded"),
                Signature: eventData.GetValueOrDefault("Signature"),
                RawXml: xml);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to parse event record");
            return null;
        }
    }

    private static string? GetProcessName(string? imagePath)
    {
        if (string.IsNullOrEmpty(imagePath)) return null;
        return Path.GetFileName(imagePath);
    }

    private static int? TryParseInt(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        return int.TryParse(value, out var result) ? result : null;
    }

    private static string GetGroupingKey(SysmonEvent evt)
    {
        return evt.EventId switch
        {
            SysmonEventTypes.ProcessCreate => $"Image: {evt.Image ?? "Unknown"}",
            SysmonEventTypes.FileCreationTimeChanged => $"Image: {evt.Image ?? "Unknown"} | TargetFilename: {evt.TargetFilename ?? "Unknown"}",
            SysmonEventTypes.NetworkConnection => $"Image: {evt.Image ?? "Unknown"} | DestinationIp: {evt.DestinationIp ?? "Unknown"}:{evt.DestinationPort}",
            SysmonEventTypes.DriverLoaded => $"ImageLoaded: {evt.ImageLoaded ?? "Unknown"}",
            SysmonEventTypes.ImageLoaded => $"Image: {evt.Image ?? "Unknown"} | ImageLoaded: {evt.ImageLoaded ?? "Unknown"}",
            SysmonEventTypes.CreateRemoteThread => $"SourceImage: {evt.Image ?? "Unknown"}",
            SysmonEventTypes.RawAccessRead => $"Image: {evt.Image ?? "Unknown"}",
            SysmonEventTypes.ProcessAccess => $"SourceImage: {evt.Image ?? "Unknown"}",
            SysmonEventTypes.FileCreate => $"Image: {evt.Image ?? "Unknown"} | TargetFolder: {Path.GetDirectoryName(evt.TargetFilename) ?? "Unknown"}",
            SysmonEventTypes.RegistryObjectCreateDelete => $"Image: {evt.Image ?? "Unknown"}",
            SysmonEventTypes.RegistryValueSet => $"Image: {evt.Image ?? "Unknown"}",
            SysmonEventTypes.RegistryKeyValueRename => $"Image: {evt.Image ?? "Unknown"}",
            SysmonEventTypes.FileCreateStreamHash => $"Image: {evt.Image ?? "Unknown"} | TargetFilename: {evt.TargetFilename ?? "Unknown"}",
            SysmonEventTypes.PipeCreated => $"Image: {evt.Image ?? "Unknown"}",
            SysmonEventTypes.PipeConnected => $"Image: {evt.Image ?? "Unknown"}",
            SysmonEventTypes.DnsQuery => $"Image: {evt.Image ?? "Unknown"} | QueryName: {evt.QueryName ?? "Unknown"}",
            SysmonEventTypes.FileDeleteArchived => $"Image: {evt.Image ?? "Unknown"} | TargetFilename: {evt.TargetFilename ?? "Unknown"}",
            SysmonEventTypes.FileDeleteLogged => $"Image: {evt.Image ?? "Unknown"} | TargetFilename: {evt.TargetFilename ?? "Unknown"}",
            _ => $"Image: {evt.Image ?? "Unknown"}"
        };
    }

    private static string GetSampleValue(SysmonEvent evt)
    {
        return evt.EventId switch
        {
            SysmonEventTypes.ProcessCreate => evt.CommandLine ?? evt.Image ?? "",
            SysmonEventTypes.NetworkConnection => $"{evt.DestinationIp}:{evt.DestinationPort}",
            SysmonEventTypes.ImageLoaded => evt.ImageLoaded ?? "",
            SysmonEventTypes.FileCreate => evt.TargetFilename ?? "",
            SysmonEventTypes.DnsQuery => evt.QueryName ?? "",
            _ => ""
        };
    }

    private class EventAggregationBuilder
    {
        private readonly int _eventId;
        private readonly string _groupingKey;
        private int _count;
        private DateTime _firstSeen = DateTime.MaxValue;
        private DateTime _lastSeen = DateTime.MinValue;
        private readonly List<string> _sampleValues = new(5);

        public EventAggregationBuilder(int eventId, string groupingKey)
        {
            _eventId = eventId;
            _groupingKey = groupingKey;
        }

        public void AddEvent(DateTime timestamp, string sampleValue)
        {
            _count++;
            if (timestamp < _firstSeen) _firstSeen = timestamp;
            if (timestamp > _lastSeen) _lastSeen = timestamp;
            if (_sampleValues.Count < 5 && !string.IsNullOrEmpty(sampleValue))
            {
                _sampleValues.Add(sampleValue);
            }
        }

        public EventAggregation Build()
        {
            return new EventAggregation(
                _eventId,
                SysmonEventTypes.GetEventTypeName(_eventId),
                _groupingKey,
                _count,
                _firstSeen,
                _lastSeen,
                _sampleValues);
        }
    }
}
