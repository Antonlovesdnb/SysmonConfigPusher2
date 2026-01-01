namespace SysmonConfigPusher.Core.Interfaces;

/// <summary>
/// Service for logging user actions to the audit trail.
/// </summary>
public interface IAuditService
{
    /// <summary>
    /// Log an action with a string action name.
    /// </summary>
    Task LogAsync(string? user, string action, string? details = null, CancellationToken ct = default);

    /// <summary>
    /// Log an action with a typed action enum and optional object details (serialized to JSON).
    /// </summary>
    Task LogAsync(string? user, AuditAction action, object? details = null, CancellationToken ct = default);
}

/// <summary>
/// Enumeration of auditable actions in the system.
/// </summary>
public enum AuditAction
{
    // Config operations
    ConfigUpload,
    ConfigUpdate,
    ConfigDelete,

    // Deployment operations
    DeploymentStart,
    DeploymentCancel,
    DeploymentPurge,
    ScheduledDeploymentCreate,
    ScheduledDeploymentCancel,

    // Computer/AD operations
    AdRefresh,
    InventoryScan,
    ComputerGroupCreate,
    ComputerGroupDelete,

    // Analysis operations
    NoiseAnalysisStart,
    NoiseAnalysisDelete,
    NoiseAnalysisPurge,

    // Auth events
    Login,
    AuthorizationDenied,

    // Settings operations
    SettingsUpdate,
    BinaryCacheUpdate,
    ServiceRestart,

    // Agent operations
    AgentRegistration,
    AgentCommandCompleted
}
