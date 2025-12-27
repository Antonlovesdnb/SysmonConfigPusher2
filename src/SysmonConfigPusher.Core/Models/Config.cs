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

    public ICollection<DeploymentJob> DeploymentJobs { get; set; } = [];
}
