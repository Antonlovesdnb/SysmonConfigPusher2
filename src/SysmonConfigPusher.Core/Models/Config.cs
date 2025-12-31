namespace SysmonConfigPusher.Core.Models;

public class Config
{
    public int Id { get; set; }
    public required string Filename { get; set; }
    public string? Tag { get; set; }
    public required string Content { get; set; }
    public required string Hash { get; set; }
    public string? UploadedBy { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    // Validation status (set at upload time)
    public bool IsValid { get; set; } = true;
    public string? ValidationMessage { get; set; }

    // Source URL if imported from remote location
    public string? SourceUrl { get; set; }

    public ICollection<DeploymentJob> DeploymentJobs { get; set; } = [];
}
