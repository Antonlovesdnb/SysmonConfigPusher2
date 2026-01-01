using System.Diagnostics.Eventing.Reader;
using System.Xml.Linq;
using SysmonConfigPusher.Shared;

namespace SysmonConfigPusher.Agent.Services;

/// <summary>
/// Queries local Sysmon event logs
/// </summary>
public class EventLogService
{
    private readonly ILogger<EventLogService> _logger;
    private const string SysmonLogPath = "Microsoft-Windows-Sysmon/Operational";

    public EventLogService(ILogger<EventLogService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Query Sysmon events based on provided criteria
    /// </summary>
    public QueryEventsResultPayload QueryEvents(QueryEventsPayload query)
    {
        var result = new QueryEventsResultPayload();

        try
        {
            var startTime = DateTime.UtcNow.AddHours(-query.TimeRangeHours);

            // Build XPath query
            var xpath = BuildXPathQuery(startTime, query.EventIds, query.XPathFilter);
            _logger.LogDebug("Querying events with XPath: {XPath}", xpath);

            var eventQuery = new EventLogQuery(SysmonLogPath, PathType.LogName, xpath)
            {
                ReverseDirection = true // Most recent first
            };

            using var reader = new EventLogReader(eventQuery);

            EventRecord? record;
            while ((record = reader.ReadEvent()) != null && result.ReturnedCount < query.MaxEvents)
            {
                using (record)
                {
                    result.TotalCount++;

                    var eventDto = ParseEvent(record);
                    if (eventDto != null)
                    {
                        result.Events.Add(eventDto);
                        result.ReturnedCount++;
                    }
                }
            }

            _logger.LogInformation("Returned {Count} of {Total} matching events",
                result.ReturnedCount, result.TotalCount);
        }
        catch (EventLogNotFoundException)
        {
            _logger.LogWarning("Sysmon event log not found - Sysmon may not be installed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying Sysmon events");
        }

        return result;
    }

    private static string BuildXPathQuery(DateTime startTime, int[]? eventIds, string? customFilter)
    {
        var conditions = new List<string>();

        // Time filter
        var startTimeStr = startTime.ToUniversalTime().ToString("o");
        conditions.Add($"TimeCreated[@SystemTime>='{startTimeStr}']");

        // Event ID filter
        if (eventIds != null && eventIds.Length > 0)
        {
            if (eventIds.Length == 1)
            {
                conditions.Add($"EventID={eventIds[0]}");
            }
            else
            {
                var idConditions = string.Join(" or ", eventIds.Select(id => $"EventID={id}"));
                conditions.Add($"({idConditions})");
            }
        }

        var baseQuery = $"*[System[{string.Join(" and ", conditions)}]]";

        // Append custom filter if provided
        if (!string.IsNullOrWhiteSpace(customFilter))
        {
            return $"{baseQuery} and {customFilter}";
        }

        return baseQuery;
    }

    private SysmonEventDto? ParseEvent(EventRecord record)
    {
        try
        {
            var rawXml = record.ToXml();
            var dto = new SysmonEventDto
            {
                EventId = record.Id,
                EventType = GetEventTypeName(record.Id),
                TimeCreated = record.TimeCreated?.ToUniversalTime() ?? DateTime.UtcNow,
                RawXml = rawXml
            };

            // Parse event data from XML
            if (!string.IsNullOrEmpty(rawXml))
            {
                try
                {
                    var doc = XDocument.Parse(rawXml);
                    var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

                    var eventData = doc.Descendants(ns + "EventData").FirstOrDefault();
                    if (eventData != null)
                    {
                        foreach (var data in eventData.Elements(ns + "Data"))
                        {
                            var name = data.Attribute("Name")?.Value;
                            var value = data.Value;
                            if (!string.IsNullOrEmpty(name))
                            {
                                dto.EventData[name] = value ?? string.Empty;
                            }
                        }

                        // Populate flat properties from EventData
                        PopulateFlatProperties(dto);
                    }
                }
                catch (Exception ex)
                {
                    // Log but don't fail - we still have the raw XML
                    System.Diagnostics.Debug.WriteLine($"Failed to parse event XML: {ex.Message}");
                }
            }

            return dto;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to parse event record: {ex.Message}");
            return null;
        }
    }

    private static void PopulateFlatProperties(SysmonEventDto dto)
    {
        var data = dto.EventData;

        // Process info
        if (data.TryGetValue("Image", out var image))
        {
            dto.Image = image;
            dto.ProcessName = Path.GetFileName(image);
        }
        if (data.TryGetValue("ProcessId", out var pidStr) && int.TryParse(pidStr, out var pid))
            dto.ProcessId = pid;
        if (data.TryGetValue("CommandLine", out var cmdLine))
            dto.CommandLine = cmdLine;
        if (data.TryGetValue("User", out var user))
            dto.User = user;

        // Parent process info
        if (data.TryGetValue("ParentImage", out var parentImage))
        {
            dto.ParentImage = parentImage;
            dto.ParentProcessName = Path.GetFileName(parentImage);
        }
        if (data.TryGetValue("ParentProcessId", out var ppidStr) && int.TryParse(ppidStr, out var ppid))
            dto.ParentProcessId = ppid;
        if (data.TryGetValue("ParentCommandLine", out var parentCmdLine))
            dto.ParentCommandLine = parentCmdLine;

        // Network connection info
        if (data.TryGetValue("DestinationIp", out var destIp))
            dto.DestinationIp = destIp;
        if (data.TryGetValue("DestinationPort", out var destPortStr) && int.TryParse(destPortStr, out var destPort))
            dto.DestinationPort = destPort;
        if (data.TryGetValue("DestinationHostname", out var destHost))
            dto.DestinationHostname = destHost;
        if (data.TryGetValue("SourceIp", out var srcIp))
            dto.SourceIp = srcIp;
        if (data.TryGetValue("SourcePort", out var srcPortStr) && int.TryParse(srcPortStr, out var srcPort))
            dto.SourcePort = srcPort;
        if (data.TryGetValue("Protocol", out var protocol))
            dto.Protocol = protocol;

        // File operations
        if (data.TryGetValue("TargetFilename", out var targetFile))
            dto.TargetFilename = targetFile;

        // DNS queries
        if (data.TryGetValue("QueryName", out var queryName))
            dto.QueryName = queryName;
        if (data.TryGetValue("QueryResults", out var queryResults))
            dto.QueryResults = queryResults;

        // Image loaded
        if (data.TryGetValue("ImageLoaded", out var imageLoaded))
            dto.ImageLoaded = imageLoaded;
        if (data.TryGetValue("Signature", out var signature))
            dto.Signature = signature;

        // ProcessAccess-specific fields (Event ID 10)
        if (data.TryGetValue("SourceImage", out var sourceImage))
            dto.SourceImage = sourceImage;
        if (data.TryGetValue("TargetImage", out var targetImage))
            dto.TargetImage = targetImage;
        if (data.TryGetValue("GrantedAccess", out var grantedAccess))
            dto.GrantedAccess = grantedAccess;
        if (data.TryGetValue("CallTrace", out var callTrace))
            dto.CallTrace = callTrace;
    }

    private static string GetEventTypeName(int eventId) => eventId switch
    {
        1 => "Process Create",
        2 => "File Creation Time Changed",
        3 => "Network Connection",
        4 => "Sysmon Service State Changed",
        5 => "Process Terminated",
        6 => "Driver Loaded",
        7 => "Image Loaded",
        8 => "CreateRemoteThread",
        9 => "RawAccessRead",
        10 => "Process Accessed",
        11 => "File Created",
        12 => "Registry Object Added/Deleted",
        13 => "Registry Value Set",
        14 => "Registry Key Renamed",
        15 => "File Stream Created",
        16 => "Sysmon Config State Changed",
        17 => "Pipe Created",
        18 => "Pipe Connected",
        19 => "WMI Event Filter",
        20 => "WMI Event Consumer",
        21 => "WMI Event Consumer To Filter",
        22 => "DNS Query",
        23 => "File Delete Archived",
        24 => "Clipboard Changed",
        25 => "Process Tampering",
        26 => "File Delete Logged",
        27 => "File Block Executable",
        28 => "File Block Shredding",
        29 => "File Executable Detected",
        255 => "Error",
        _ => $"Event {eventId}"
    };
}
