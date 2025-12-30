namespace SysmonConfigPusher.Core.Interfaces;

/// <summary>
/// Service for querying Sysmon event logs from remote hosts.
/// </summary>
public interface IEventLogService
{
    /// <summary>
    /// Queries Sysmon events from a remote host with filtering.
    /// </summary>
    Task<EventQueryResult> QueryEventsAsync(
        string hostname,
        EventQueryFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams Sysmon events from a remote host.
    /// </summary>
    IAsyncEnumerable<SysmonEvent> StreamEventsAsync(
        string hostname,
        EventQueryFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets event aggregations for noise analysis.
    /// </summary>
    Task<EventAggregationResult> GetEventAggregationsAsync(
        string hostname,
        double timeRangeHours,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests if event log access is available on a remote host.
    /// </summary>
    Task<bool> TestEventLogAccessAsync(
        string hostname,
        CancellationToken cancellationToken = default);
}

public record EventQueryFilter(
    int? EventId = null,
    DateTime? StartTime = null,
    DateTime? EndTime = null,
    string? ProcessName = null,
    string? ImagePath = null,
    string? DestinationIp = null,
    string? DnsQueryName = null,
    int MaxResults = 500);

public record SysmonEvent(
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

public record EventQueryResult(
    bool Success,
    IReadOnlyList<SysmonEvent> Events,
    int TotalCount,
    string? ErrorMessage);

public record EventAggregation(
    int EventId,
    string EventType,
    string GroupingKey,
    int Count,
    DateTime FirstSeen,
    DateTime LastSeen,
    List<string> SampleValues,
    Dictionary<string, string> AvailableFields);

public record EventAggregationResult(
    bool Success,
    IReadOnlyList<EventAggregation> Aggregations,
    int TotalEventsAnalyzed,
    TimeSpan AnalysisPeriod,
    string? ErrorMessage);

/// <summary>
/// Host role for determining noise thresholds.
/// </summary>
public enum HostRole
{
    Workstation,
    Server,
    DomainController
}

/// <summary>
/// Sysmon event type constants.
/// </summary>
public static class SysmonEventTypes
{
    public const int ProcessCreate = 1;
    public const int FileCreationTimeChanged = 2;
    public const int NetworkConnection = 3;
    public const int ServiceStateChanged = 4;
    public const int ProcessTerminated = 5;
    public const int DriverLoaded = 6;
    public const int ImageLoaded = 7;
    public const int CreateRemoteThread = 8;
    public const int RawAccessRead = 9;
    public const int ProcessAccess = 10;
    public const int FileCreate = 11;
    public const int RegistryObjectCreateDelete = 12;
    public const int RegistryValueSet = 13;
    public const int RegistryKeyValueRename = 14;
    public const int FileCreateStreamHash = 15;
    public const int ConfigChange = 16;
    public const int PipeCreated = 17;
    public const int PipeConnected = 18;
    public const int WmiFilterActivity = 19;
    public const int WmiConsumerActivity = 20;
    public const int WmiConsumerFilterBinding = 21;
    public const int DnsQuery = 22;
    public const int FileDeleteArchived = 23;
    public const int ClipboardChange = 24;
    public const int ProcessTampering = 25;
    public const int FileDeleteLogged = 26;
    public const int FileBlockExecutable = 27;
    public const int FileBlockShredding = 28;
    public const int FileExecutableDetected = 29;

    public static string GetEventTypeName(int eventId) => eventId switch
    {
        ProcessCreate => "Process Create",
        FileCreationTimeChanged => "File Creation Time Changed",
        NetworkConnection => "Network Connection",
        ServiceStateChanged => "Sysmon Service State Changed",
        ProcessTerminated => "Process Terminated",
        DriverLoaded => "Driver Loaded",
        ImageLoaded => "Image Loaded",
        CreateRemoteThread => "CreateRemoteThread",
        RawAccessRead => "RawAccessRead",
        ProcessAccess => "Process Access",
        FileCreate => "File Create",
        RegistryObjectCreateDelete => "Registry Object Create/Delete",
        RegistryValueSet => "Registry Value Set",
        RegistryKeyValueRename => "Registry Key/Value Rename",
        FileCreateStreamHash => "File Create Stream Hash",
        ConfigChange => "Sysmon Config Change",
        PipeCreated => "Pipe Created",
        PipeConnected => "Pipe Connected",
        WmiFilterActivity => "WMI Filter Activity",
        WmiConsumerActivity => "WMI Consumer Activity",
        WmiConsumerFilterBinding => "WMI Consumer-Filter Binding",
        DnsQuery => "DNS Query",
        FileDeleteArchived => "File Delete Archived",
        ClipboardChange => "Clipboard Change",
        ProcessTampering => "Process Tampering",
        FileDeleteLogged => "File Delete Logged",
        FileBlockExecutable => "File Block Executable",
        FileBlockShredding => "File Block Shredding",
        FileExecutableDetected => "File Executable Detected",
        _ => $"Event {eventId}"
    };

    public static readonly int[] SupportedEventIds = [
        ProcessCreate, FileCreationTimeChanged, NetworkConnection, ServiceStateChanged,
        ProcessTerminated, DriverLoaded, ImageLoaded, CreateRemoteThread, RawAccessRead,
        ProcessAccess, FileCreate, RegistryObjectCreateDelete, RegistryValueSet,
        RegistryKeyValueRename, FileCreateStreamHash, ConfigChange, PipeCreated,
        PipeConnected, WmiFilterActivity, WmiConsumerActivity, WmiConsumerFilterBinding,
        DnsQuery, FileDeleteArchived, ClipboardChange, ProcessTampering, FileDeleteLogged,
        FileBlockExecutable, FileBlockShredding, FileExecutableDetected
    ];
}
