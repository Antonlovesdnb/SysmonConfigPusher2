namespace SysmonConfigPusher.Core.Models;

public class Computer
{
    public int Id { get; set; }
    public required string Hostname { get; set; }
    public string? DistinguishedName { get; set; }
    public string? OperatingSystem { get; set; }
    public DateTime? LastSeen { get; set; }
    public string? SysmonVersion { get; set; }
    public string? SysmonPath { get; set; }
    public string? ConfigHash { get; set; }
    public DateTime? LastDeployment { get; set; }
    public DateTime? LastInventoryScan { get; set; }

    public ICollection<ComputerGroupMember> GroupMemberships { get; set; } = [];
    public ICollection<DeploymentResult> DeploymentResults { get; set; } = [];
}
