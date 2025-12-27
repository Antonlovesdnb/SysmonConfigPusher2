namespace SysmonConfigPusher.Core.Models;

public class DeploymentJob
{
    public int Id { get; set; }
    public required string Operation { get; set; }
    public int? ConfigId { get; set; }
    public Config? Config { get; set; }
    public string? StartedBy { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public required string Status { get; set; }

    public ICollection<DeploymentResult> Results { get; set; } = [];
}

public class DeploymentResult
{
    public int Id { get; set; }
    public int JobId { get; set; }
    public DeploymentJob Job { get; set; } = null!;
    public int ComputerId { get; set; }
    public Computer Computer { get; set; } = null!;
    public bool Success { get; set; }
    public string? Message { get; set; }
    public DateTime? CompletedAt { get; set; }
}
