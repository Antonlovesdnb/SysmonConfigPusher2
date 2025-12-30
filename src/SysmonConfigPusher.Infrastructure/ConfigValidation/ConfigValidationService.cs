using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using SysmonConfigPusher.Core.Interfaces;

namespace SysmonConfigPusher.Infrastructure.ConfigValidation;

/// <summary>
/// Validates Sysmon configuration XML files.
/// </summary>
public class ConfigValidationService : IConfigValidationService
{
    private readonly ILogger<ConfigValidationService> _logger;

    // Known Sysmon event types that can appear in EventFiltering
    private static readonly HashSet<string> ValidEventTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ProcessCreate", "FileCreateTime", "NetworkConnect", "ProcessTerminate",
        "DriverLoad", "ImageLoad", "CreateRemoteThread", "RawAccessRead",
        "ProcessAccess", "FileCreate", "RegistryEvent", "FileCreateStreamHash",
        "PipeEvent", "WmiEvent", "DnsQuery", "FileDelete", "ClipboardChange",
        "ProcessTampering", "FileDeleteDetected", "FileBlockExecutable",
        "FileBlockShredding", "RuleGroup"
    };

    public ConfigValidationService(ILogger<ConfigValidationService> logger)
    {
        _logger = logger;
    }

    public ConfigValidationResult Validate(string xmlContent)
    {
        if (string.IsNullOrWhiteSpace(xmlContent))
        {
            return ConfigValidationResult.Invalid("Configuration content is empty");
        }

        try
        {
            // Parse as XML
            XDocument doc;
            try
            {
                doc = XDocument.Parse(xmlContent);
            }
            catch (XmlException ex)
            {
                return ConfigValidationResult.Invalid($"Invalid XML: {ex.Message}");
            }

            // Check root element is Sysmon
            var root = doc.Root;
            if (root == null)
            {
                return ConfigValidationResult.Invalid("Missing root element");
            }

            if (!root.Name.LocalName.Equals("Sysmon", StringComparison.OrdinalIgnoreCase))
            {
                return ConfigValidationResult.Invalid($"Root element must be 'Sysmon', found '{root.Name.LocalName}'");
            }

            // Check schemaversion attribute
            var schemaVersion = root.Attribute("schemaversion")?.Value;
            if (string.IsNullOrEmpty(schemaVersion))
            {
                return ConfigValidationResult.Invalid("Missing 'schemaversion' attribute on Sysmon element");
            }

            if (!Version.TryParse(schemaVersion, out var version))
            {
                return ConfigValidationResult.Invalid($"Invalid schemaversion format: '{schemaVersion}'");
            }

            // Check for EventFiltering section
            var eventFiltering = root.Element("EventFiltering");
            if (eventFiltering == null)
            {
                return ConfigValidationResult.Invalid("Missing 'EventFiltering' element");
            }

            // Validate EventFiltering has at least one rule
            var ruleElements = eventFiltering.Elements().ToList();
            if (ruleElements.Count == 0)
            {
                return ConfigValidationResult.Invalid("EventFiltering section is empty");
            }

            // Validate rule elements are known event types or RuleGroup
            foreach (var element in ruleElements)
            {
                var elementName = element.Name.LocalName;
                if (!ValidEventTypes.Contains(elementName))
                {
                    _logger.LogWarning("Unknown event type in config: {EventType}", elementName);
                    // Don't fail on unknown types - Sysmon may have added new ones
                }
            }

            // Check for common issues
            var hashAlgorithms = root.Element("HashAlgorithms")?.Value;
            var checkRevocation = root.Element("CheckRevocation");
            var dnsLookup = root.Element("DnsLookup");

            // Build success message with summary
            var ruleCount = CountRules(eventFiltering);
            var message = $"Valid Sysmon v{schemaVersion} configuration with {ruleCount} rule(s)";

            _logger.LogDebug("Config validation passed: {Message}", message);
            return ConfigValidationResult.Valid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error validating config");
            return ConfigValidationResult.Invalid($"Validation error: {ex.Message}");
        }
    }

    private static int CountRules(XElement eventFiltering)
    {
        var count = 0;
        foreach (var element in eventFiltering.Elements())
        {
            if (element.Name.LocalName == "RuleGroup")
            {
                count += element.Elements().Count();
            }
            else
            {
                count++;
            }
        }
        return count;
    }
}
