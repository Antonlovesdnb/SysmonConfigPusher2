using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SysmonConfigPusher.Data;
using SysmonConfigPusher.Core.Models;
using SysmonConfigPusher.Core.Interfaces;

namespace SysmonConfigPusher.Service.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "RequireViewer")]
public partial class ConfigsController : ControllerBase
{
    private readonly SysmonDbContext _db;
    private readonly IAuditService _auditService;
    private readonly ILogger<ConfigsController> _logger;

    public ConfigsController(SysmonDbContext db, IAuditService auditService, ILogger<ConfigsController> logger)
    {
        _db = db;
        _auditService = auditService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ConfigDto>>> GetConfigs()
    {
        var configs = await _db.Configs
            .Where(c => c.IsActive)
            .OrderByDescending(c => c.UploadedAt)
            .Select(c => new ConfigDto(
                c.Id,
                c.Filename,
                c.Tag,
                c.Hash,
                c.UploadedBy,
                c.UploadedAt))
            .ToListAsync();

        return Ok(configs);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ConfigDetailDto>> GetConfig(int id)
    {
        var config = await _db.Configs.FindAsync(id);
        if (config == null)
            return NotFound();

        return Ok(new ConfigDetailDto(
            config.Id,
            config.Filename,
            config.Tag,
            config.Content,
            config.Hash,
            config.UploadedBy,
            config.UploadedAt,
            config.IsActive));
    }

    [HttpPost]
    [Authorize(Policy = "RequireOperator")]
    public async Task<ActionResult<ConfigDto>> UploadConfig(IFormFile file)
    {
        if (file.Length == 0)
            return BadRequest("Empty file");

        using var reader = new StreamReader(file.OpenReadStream());
        var content = await reader.ReadToEndAsync();

        // Parse SCPTAG from config
        var tag = ParseScpTag(content);

        // Calculate hash
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

        var config = new Config
        {
            Filename = file.FileName,
            Tag = tag,
            Content = content,
            Hash = hash,
            UploadedBy = User.Identity?.Name
        };

        _db.Configs.Add(config);
        await _db.SaveChangesAsync();

        await _auditService.LogAsync(User.Identity?.Name, AuditAction.ConfigUpload,
            new { ConfigId = config.Id, Filename = file.FileName, Tag = tag });

        _logger.LogInformation("User {User} uploaded config {Filename} with tag {Tag}",
            User.Identity?.Name, file.FileName, tag);

        return CreatedAtAction(nameof(GetConfig), new { id = config.Id },
            new ConfigDto(config.Id, config.Filename, config.Tag, config.Hash, config.UploadedBy, config.UploadedAt));
    }

    [HttpPut("{id}")]
    [Authorize(Policy = "RequireOperator")]
    public async Task<ActionResult<ConfigDetailDto>> UpdateConfig(int id, [FromBody] UpdateConfigRequest request)
    {
        var config = await _db.Configs.FindAsync(id);
        if (config == null)
            return NotFound();

        var oldHash = config.Hash;

        // Update content
        config.Content = request.Content;

        // Re-parse SCPTAG
        config.Tag = ParseScpTag(request.Content);

        // Recalculate hash
        config.Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Content)));

        await _db.SaveChangesAsync();

        await _auditService.LogAsync(User.Identity?.Name, AuditAction.ConfigUpdate,
            new { ConfigId = id, Filename = config.Filename, OldHash = oldHash, NewHash = config.Hash });

        _logger.LogInformation("User {User} updated config {Id} (new hash: {Hash})",
            User.Identity?.Name, id, config.Hash);

        return Ok(new ConfigDetailDto(
            config.Id,
            config.Filename,
            config.Tag,
            config.Content,
            config.Hash,
            config.UploadedBy,
            config.UploadedAt,
            config.IsActive));
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = "RequireOperator")]
    public async Task<ActionResult> DeleteConfig(int id)
    {
        var config = await _db.Configs.FindAsync(id);
        if (config == null)
            return NotFound();

        config.IsActive = false;
        await _db.SaveChangesAsync();

        await _auditService.LogAsync(User.Identity?.Name, AuditAction.ConfigDelete,
            new { ConfigId = id, Filename = config.Filename });

        _logger.LogInformation("User {User} deleted config {Id}", User.Identity?.Name, id);

        return NoContent();
    }

    [HttpGet("{id}/diff/{otherId}")]
    public async Task<ActionResult<ConfigDiffDto>> DiffConfigs(int id, int otherId)
    {
        var config1 = await _db.Configs.FindAsync(id);
        var config2 = await _db.Configs.FindAsync(otherId);

        if (config1 == null || config2 == null)
            return NotFound();

        // Simple line-by-line diff
        var lines1 = config1.Content.Split('\n');
        var lines2 = config2.Content.Split('\n');

        return Ok(new ConfigDiffDto(
            new ConfigDto(config1.Id, config1.Filename, config1.Tag, config1.Hash, config1.UploadedBy, config1.UploadedAt),
            new ConfigDto(config2.Id, config2.Filename, config2.Tag, config2.Hash, config2.UploadedBy, config2.UploadedAt),
            lines1,
            lines2));
    }

    /// <summary>
    /// Add an exclusion rule to a config.
    /// </summary>
    [HttpPost("{id}/exclusions")]
    [Authorize(Policy = "RequireOperator")]
    public async Task<ActionResult<AddExclusionResponse>> AddExclusion(int id, [FromBody] AddExclusionRequest request)
    {
        var config = await _db.Configs.FindAsync(id);
        if (config == null)
            return NotFound();

        try
        {
            var doc = XDocument.Parse(config.Content);
            var sysmon = doc.Root;
            if (sysmon == null || sysmon.Name.LocalName != "Sysmon")
            {
                return BadRequest(new AddExclusionResponse(false, null, "Invalid Sysmon config: root element must be 'Sysmon'"));
            }

            // Get the event filter element name for this event ID
            var eventFilterName = GetEventFilterName(request.EventId);
            if (eventFilterName == null)
            {
                return BadRequest(new AddExclusionResponse(false, null, $"Unknown event ID: {request.EventId}"));
            }

            // Find or create EventFiltering element
            var eventFiltering = sysmon.Element("EventFiltering");
            if (eventFiltering == null)
            {
                eventFiltering = new XElement("EventFiltering");
                sysmon.Add(eventFiltering);
            }

            // Find or create the RuleGroup for this event type
            var ruleGroup = eventFiltering.Elements("RuleGroup")
                .FirstOrDefault(rg => rg.Element(eventFilterName) != null);

            XElement eventElement;
            if (ruleGroup != null)
            {
                eventElement = ruleGroup.Element(eventFilterName)!;
            }
            else
            {
                // Create new RuleGroup with event element
                ruleGroup = new XElement("RuleGroup",
                    new XAttribute("name", ""),
                    new XAttribute("groupRelation", "or"));
                eventElement = new XElement(eventFilterName,
                    new XAttribute("onmatch", "exclude"));
                ruleGroup.Add(eventElement);
                eventFiltering.Add(ruleGroup);
            }

            // Check if onmatch is "exclude" - if it's "include", we need to handle differently
            var onmatchAttr = eventElement.Attribute("onmatch");
            if (onmatchAttr?.Value.ToLower() != "exclude")
            {
                // For include-based configs, we add the exclusion as a separate RuleGroup
                var excludeRuleGroup = eventFiltering.Elements("RuleGroup")
                    .FirstOrDefault(rg =>
                    {
                        var el = rg.Element(eventFilterName);
                        return el != null && el.Attribute("onmatch")?.Value.ToLower() == "exclude";
                    });

                if (excludeRuleGroup != null)
                {
                    eventElement = excludeRuleGroup.Element(eventFilterName)!;
                }
                else
                {
                    // Create new exclude RuleGroup
                    excludeRuleGroup = new XElement("RuleGroup",
                        new XAttribute("name", "Exclusions"),
                        new XAttribute("groupRelation", "or"));
                    eventElement = new XElement(eventFilterName,
                        new XAttribute("onmatch", "exclude"));
                    excludeRuleGroup.Add(eventElement);
                    eventFiltering.Add(excludeRuleGroup);
                }
            }

            // Add the exclusion rule
            var exclusionElement = new XElement(request.FieldName,
                new XAttribute("condition", request.Condition ?? "is"),
                request.Value);

            // Check for duplicate
            var existingRule = eventElement.Elements(request.FieldName)
                .FirstOrDefault(e => e.Value == request.Value &&
                    (e.Attribute("condition")?.Value ?? "is") == (request.Condition ?? "is"));

            if (existingRule != null)
            {
                return Ok(new AddExclusionResponse(true, config.Content, "Exclusion rule already exists"));
            }

            eventElement.Add(exclusionElement);

            // Update the config
            var newContent = doc.ToString(SaveOptions.None);
            var newHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(newContent)));

            config.Content = newContent;
            config.Hash = newHash;
            await _db.SaveChangesAsync();

            _logger.LogInformation("User {User} added exclusion to config {Id}: {EventFilter}.{Field} {Condition} '{Value}'",
                User.Identity?.Name, id, eventFilterName, request.FieldName, request.Condition ?? "is", request.Value);

            return Ok(new AddExclusionResponse(true, newContent, null));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add exclusion to config {Id}", id);
            return BadRequest(new AddExclusionResponse(false, null, $"Failed to parse config XML: {ex.Message}"));
        }
    }

    private static string? GetEventFilterName(int eventId)
    {
        return eventId switch
        {
            SysmonEventTypes.ProcessCreate => "ProcessCreate",
            SysmonEventTypes.FileCreationTimeChanged => "FileCreateTime",
            SysmonEventTypes.NetworkConnection => "NetworkConnect",
            SysmonEventTypes.ServiceStateChanged => "SysmonStatusChange",
            SysmonEventTypes.ProcessTerminated => "ProcessTerminate",
            SysmonEventTypes.DriverLoaded => "DriverLoad",
            SysmonEventTypes.ImageLoaded => "ImageLoad",
            SysmonEventTypes.CreateRemoteThread => "CreateRemoteThread",
            SysmonEventTypes.RawAccessRead => "RawAccessRead",
            SysmonEventTypes.ProcessAccess => "ProcessAccess",
            SysmonEventTypes.FileCreate => "FileCreate",
            SysmonEventTypes.RegistryObjectCreateDelete => "RegistryEvent",
            SysmonEventTypes.RegistryValueSet => "RegistryEvent",
            SysmonEventTypes.RegistryKeyValueRename => "RegistryEvent",
            SysmonEventTypes.FileCreateStreamHash => "FileCreateStreamHash",
            SysmonEventTypes.ConfigChange => null, // Cannot filter config changes
            SysmonEventTypes.PipeCreated => "PipeEvent",
            SysmonEventTypes.PipeConnected => "PipeEvent",
            SysmonEventTypes.WmiFilterActivity => "WmiEvent",
            SysmonEventTypes.WmiConsumerActivity => "WmiEvent",
            SysmonEventTypes.WmiConsumerFilterBinding => "WmiEvent",
            SysmonEventTypes.DnsQuery => "DnsQuery",
            SysmonEventTypes.FileDeleteArchived => "FileDelete",
            SysmonEventTypes.ClipboardChange => "ClipboardChange",
            SysmonEventTypes.ProcessTampering => "ProcessTampering",
            SysmonEventTypes.FileDeleteLogged => "FileDelete",
            SysmonEventTypes.FileBlockExecutable => "FileBlockExecutable",
            SysmonEventTypes.FileBlockShredding => "FileBlockShredding",
            SysmonEventTypes.FileExecutableDetected => "FileExecutableDetected",
            _ => null
        };
    }

    private static string? ParseScpTag(string content)
    {
        var match = ScpTagRegex().Match(content);
        return match.Success ? match.Groups["tag"].Value.Trim() : null;
    }

    [GeneratedRegex(@"<!--\s*SCPTAG:\s*(?<tag>[^-]+)\s*-->", RegexOptions.IgnoreCase)]
    private static partial Regex ScpTagRegex();
}

public record ConfigDto(
    int Id,
    string Filename,
    string? Tag,
    string Hash,
    string? UploadedBy,
    DateTime UploadedAt);

public record ConfigDetailDto(
    int Id,
    string Filename,
    string? Tag,
    string Content,
    string Hash,
    string? UploadedBy,
    DateTime UploadedAt,
    bool IsActive);

public record ConfigDiffDto(
    ConfigDto Config1,
    ConfigDto Config2,
    string[] Lines1,
    string[] Lines2);

public record AddExclusionRequest(
    int EventId,
    string FieldName,
    string Value,
    string? Condition = "is");

public record AddExclusionResponse(
    bool Success,
    string? UpdatedContent,
    string? Message);

public record UpdateConfigRequest(
    string Content);
