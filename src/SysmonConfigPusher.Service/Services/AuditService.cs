using System.Text.Json;
using SysmonConfigPusher.Core.Interfaces;
using SysmonConfigPusher.Core.Models;
using SysmonConfigPusher.Data;

namespace SysmonConfigPusher.Service.Services;

/// <summary>
/// Service for logging user actions to the audit trail database.
/// </summary>
public class AuditService : IAuditService
{
    private readonly SysmonDbContext _db;
    private readonly ILogger<AuditService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AuditService(SysmonDbContext db, ILogger<AuditService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task LogAsync(string? user, string action, string? details = null, CancellationToken ct = default)
    {
        var log = new AuditLog
        {
            Timestamp = DateTime.UtcNow,
            User = user ?? "Unknown",
            Action = action,
            Details = details
        };

        _db.AuditLogs.Add(log);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Audit: {User} performed {Action}: {Details}", user, action, details);
    }

    public async Task LogAsync(string? user, AuditAction action, object? details = null, CancellationToken ct = default)
    {
        var detailsJson = details != null
            ? JsonSerializer.Serialize(details, JsonOptions)
            : null;

        await LogAsync(user, action.ToString(), detailsJson, ct);
    }
}
