namespace SysmonConfigPusher.Core.Models;

public class NoiseAnalysisRun
{
    public int Id { get; set; }
    public int ComputerId { get; set; }
    public Computer Computer { get; set; } = null!;
    public double TimeRangeHours { get; set; }
    public int TotalEvents { get; set; }
    public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;

    public ICollection<NoiseResult> Results { get; set; } = [];
}

public class NoiseResult
{
    public int Id { get; set; }
    public int RunId { get; set; }
    public NoiseAnalysisRun Run { get; set; } = null!;
    public int EventId { get; set; }
    public required string GroupingKey { get; set; }
    public int EventCount { get; set; }
    public double NoiseScore { get; set; }
    public string? SuggestedExclusion { get; set; }
}
