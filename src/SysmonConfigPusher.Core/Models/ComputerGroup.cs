namespace SysmonConfigPusher.Core.Models;

public class ComputerGroup
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ComputerGroupMember> Members { get; set; } = [];
}

public class ComputerGroupMember
{
    public int GroupId { get; set; }
    public ComputerGroup Group { get; set; } = null!;

    public int ComputerId { get; set; }
    public Computer Computer { get; set; } = null!;
}
