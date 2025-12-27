namespace SysmonConfigPusher.Core.Models;

public class AuditLog
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? User { get; set; }
    public required string Action { get; set; }
    public string? Details { get; set; }
}
