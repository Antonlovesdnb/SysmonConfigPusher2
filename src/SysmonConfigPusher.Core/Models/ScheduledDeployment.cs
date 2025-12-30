namespace SysmonConfigPusher.Core.Models;

public class ScheduledDeployment
{
    public int Id { get; set; }
    public required string Operation { get; set; }
    public int? ConfigId { get; set; }
    public Config? Config { get; set; }
    public DateTime ScheduledAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "Pending";
    public int? DeploymentJobId { get; set; }
    public DeploymentJob? DeploymentJob { get; set; }

    public ICollection<ScheduledDeploymentComputer> Computers { get; set; } = [];
}

public class ScheduledDeploymentComputer
{
    public int Id { get; set; }
    public int ScheduledDeploymentId { get; set; }
    public ScheduledDeployment ScheduledDeployment { get; set; } = null!;
    public int ComputerId { get; set; }
    public Computer Computer { get; set; } = null!;
}
