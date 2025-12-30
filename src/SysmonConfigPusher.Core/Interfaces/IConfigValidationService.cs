namespace SysmonConfigPusher.Core.Interfaces;

/// <summary>
/// Service for validating Sysmon configuration XML files.
/// </summary>
public interface IConfigValidationService
{
    /// <summary>
    /// Validates a Sysmon configuration XML string.
    /// </summary>
    /// <param name="xmlContent">The XML content to validate.</param>
    /// <returns>A validation result with success status and any error messages.</returns>
    ConfigValidationResult Validate(string xmlContent);
}

/// <summary>
/// Result of a Sysmon config validation.
/// </summary>
public record ConfigValidationResult(bool IsValid, string? Message = null)
{
    public static ConfigValidationResult Valid() => new(true, "Valid Sysmon configuration");
    public static ConfigValidationResult Invalid(string message) => new(false, message);
}
