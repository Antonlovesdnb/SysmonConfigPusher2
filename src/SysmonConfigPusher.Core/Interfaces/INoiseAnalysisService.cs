using SysmonConfigPusher.Core.Models;

namespace SysmonConfigPusher.Core.Interfaces;

/// <summary>
/// Service for analyzing Sysmon event noise and generating exclusion rules.
/// </summary>
public interface INoiseAnalysisService
{
    /// <summary>
    /// Analyzes event noise for a single host.
    /// </summary>
    Task<NoiseAnalysisRunResult> AnalyzeHostAsync(
        int computerId,
        double timeRangeHours,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyzes event noise across multiple hosts for comparison.
    /// </summary>
    Task<CrossHostAnalysisResult> AnalyzeMultipleHostsAsync(
        IEnumerable<int> computerIds,
        double timeRangeHours,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates Sysmon exclusion XML from noise analysis results.
    /// </summary>
    Task<string> GenerateExclusionXmlAsync(
        int runId,
        double minimumNoiseScore = 0.5,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets previous analysis runs.
    /// </summary>
    Task<IReadOnlyList<NoiseAnalysisRun>> GetAnalysisHistoryAsync(
        int? computerId = null,
        int limit = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific analysis run with results.
    /// </summary>
    Task<NoiseAnalysisRunResult?> GetAnalysisRunAsync(
        int runId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets noise thresholds for a specific host role.
    /// </summary>
    NoiseThresholds GetThresholdsForRole(HostRole role);

    /// <summary>
    /// Determines the role of a host based on its properties.
    /// </summary>
    Task<HostRole> DetermineHostRoleAsync(
        int computerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a specific analysis run and its results.
    /// </summary>
    Task<bool> DeleteAnalysisRunAsync(
        int runId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Purges analysis runs and results from the database.
    /// </summary>
    /// <param name="olderThanDays">Delete runs older than this many days. 0 means all runs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<int> PurgeAnalysisRunsAsync(
        int olderThanDays = 0,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Noise thresholds per event type (events per hour).
/// </summary>
public record NoiseThresholds(
    int ProcessCreatePerHour,
    int NetworkConnectionPerHour,
    int ImageLoadedPerHour,
    int FileCreatePerHour,
    int DnsQueryPerHour)
{
    /// <summary>
    /// Gets the threshold for a specific event type.
    /// </summary>
    public int GetThreshold(int eventId) => eventId switch
    {
        SysmonEventTypes.ProcessCreate => ProcessCreatePerHour,
        SysmonEventTypes.NetworkConnection => NetworkConnectionPerHour,
        SysmonEventTypes.ImageLoaded => ImageLoadedPerHour,
        SysmonEventTypes.FileCreate => FileCreatePerHour,
        SysmonEventTypes.DnsQuery => DnsQueryPerHour,
        _ => 100 // Default threshold
    };
}

/// <summary>
/// Result of a noise analysis run.
/// </summary>
public record NoiseAnalysisRunResult(
    bool Success,
    NoiseAnalysisRun? Run,
    IReadOnlyList<NoiseResultDto> Results,
    string? ErrorMessage);

/// <summary>
/// DTO for noise result with additional computed fields.
/// </summary>
public record NoiseResultDto(
    int Id,
    int EventId,
    string EventType,
    string GroupingKey,
    int EventCount,
    double EventsPerHour,
    double NoiseScore,
    NoiseLevel NoiseLevel,
    string? SuggestedExclusion,
    Dictionary<string, string> AvailableFields);

/// <summary>
/// Noise level classification.
/// </summary>
public enum NoiseLevel
{
    /// <summary>
    /// Normal event volume (score 0 - 0.5).
    /// </summary>
    Normal,

    /// <summary>
    /// Noisy - exceeds threshold by 2x (score 0.5 - 0.7).
    /// </summary>
    Noisy,

    /// <summary>
    /// Very noisy - exceeds threshold by 5x (score 0.7 - 1.0).
    /// </summary>
    VeryNoisy
}

/// <summary>
/// Result of cross-host analysis.
/// </summary>
public record CrossHostAnalysisResult(
    bool Success,
    IReadOnlyList<CrossHostComparison> Comparisons,
    IReadOnlyList<string> CommonNoisePatterns,
    string? ErrorMessage);

/// <summary>
/// Comparison data for a single host in cross-host analysis.
/// </summary>
public record CrossHostComparison(
    int ComputerId,
    string Hostname,
    int TotalEvents,
    int NoisyPatterns,
    int VeryNoisyPatterns,
    double OverallNoiseScore,
    IReadOnlyList<NoiseResultDto> TopNoisePatterns);
