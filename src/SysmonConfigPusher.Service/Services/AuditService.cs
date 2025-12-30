using System.Text.Json;
using Microsoft.Extensions.Options;
using SysmonConfigPusher.Core.Configuration;
using SysmonConfigPusher.Core.Interfaces;
using SysmonConfigPusher.Core.Models;
using SysmonConfigPusher.Data;

namespace SysmonConfigPusher.Service.Services;

/// <summary>
/// Service for logging user actions to the audit trail database and optional JSON file.
/// </summary>
public class AuditService : IAuditService
{
    private readonly SysmonDbContext _db;
    private readonly IOptionsMonitor<SysmonConfigPusherSettings> _settings;
    private readonly ILogger<AuditService> _logger;
    private static readonly object _fileLock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AuditService(
        SysmonDbContext db,
        IOptionsMonitor<SysmonConfigPusherSettings> settings,
        ILogger<AuditService> logger)
    {
        _db = db;
        _settings = settings;
        _logger = logger;
    }

    public async Task LogAsync(string? user, string action, string? details = null, CancellationToken ct = default)
    {
        var timestamp = DateTime.UtcNow;
        var log = new AuditLog
        {
            Timestamp = timestamp,
            User = user ?? "Unknown",
            Action = action,
            Details = details
        };

        // Write to database
        _db.AuditLogs.Add(log);
        await _db.SaveChangesAsync(ct);

        // Write to JSON file if configured
        var auditLogPath = _settings.CurrentValue.AuditLogPath;
        if (!string.IsNullOrWhiteSpace(auditLogPath))
        {
            try
            {
                var jsonEntry = new
                {
                    timestamp = timestamp.ToString("o"),
                    user = user ?? "Unknown",
                    action,
                    details
                };
                var jsonLine = JsonSerializer.Serialize(jsonEntry, JsonOptions);

                // Ensure directory exists
                var directory = Path.GetDirectoryName(auditLogPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                // Thread-safe file append
                lock (_fileLock)
                {
                    File.AppendAllText(auditLogPath, jsonLine + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write audit log to JSON file: {Path}", auditLogPath);
            }
        }

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
