using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
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
    private readonly IConfigValidationService _validationService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ConfigsController> _logger;

    // Maximum config file size (5 MB)
    private const int MaxConfigSize = 5 * 1024 * 1024;

    public ConfigsController(
        SysmonDbContext db,
        IAuditService auditService,
        IConfigValidationService validationService,
        IHttpClientFactory httpClientFactory,
        ILogger<ConfigsController> logger)
    {
        _db = db;
        _auditService = auditService;
        _validationService = validationService;
        _httpClientFactory = httpClientFactory;
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
                c.UploadedAt,
                c.IsValid,
                c.ValidationMessage,
                c.SourceUrl))
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

        // Validate the config
        var validationResult = _validationService.Validate(content);

        var config = new Config
        {
            Filename = file.FileName,
            Tag = tag,
            Content = content,
            Hash = hash,
            UploadedBy = User.Identity?.Name,
            IsValid = validationResult.IsValid,
            ValidationMessage = validationResult.Message
        };

        _db.Configs.Add(config);
        await _db.SaveChangesAsync();

        await _auditService.LogAsync(User.Identity?.Name, AuditAction.ConfigUpload,
            new { ConfigId = config.Id, Filename = file.FileName, Tag = tag, IsValid = validationResult.IsValid });

        _logger.LogInformation("User {User} uploaded config {Filename} with tag {Tag} (valid: {IsValid})",
            User.Identity?.Name, file.FileName, tag, validationResult.IsValid);

        return CreatedAtAction(nameof(GetConfig), new { id = config.Id },
            new ConfigDto(config.Id, config.Filename, config.Tag, config.Hash, config.UploadedBy, config.UploadedAt,
                config.IsValid, config.ValidationMessage));
    }

    /// <summary>
    /// Import a Sysmon configuration from a remote URL.
    /// </summary>
    [HttpPost("from-url")]
    [Authorize(Policy = "RequireOperator")]
    public async Task<ActionResult<ConfigDto>> ImportFromUrl([FromBody] ImportFromUrlRequest request)
    {
        // Validate URL
        if (string.IsNullOrWhiteSpace(request.Url))
        {
            return BadRequest(new ImportFromUrlResponse(false, null, "URL is required"));
        }

        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri))
        {
            return BadRequest(new ImportFromUrlResponse(false, null, "Invalid URL format"));
        }

        // Only allow HTTP and HTTPS schemes
        if (uri.Scheme != "http" && uri.Scheme != "https")
        {
            return BadRequest(new ImportFromUrlResponse(false, null, "Only HTTP and HTTPS URLs are supported"));
        }

        // Block private/local IP ranges to prevent SSRF
        if (IsPrivateOrLocalAddress(uri.Host))
        {
            return BadRequest(new ImportFromUrlResponse(false, null, "URLs pointing to private or local addresses are not allowed"));
        }

        string content;
        try
        {
            var client = _httpClientFactory.CreateClient("ConfigImport");
            client.Timeout = TimeSpan.FromSeconds(30);

            // Set a reasonable max response size
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            // Check content length if available
            if (response.Content.Headers.ContentLength > MaxConfigSize)
            {
                return BadRequest(new ImportFromUrlResponse(false, null, $"File too large. Maximum size is {MaxConfigSize / 1024 / 1024} MB"));
            }

            // Read content as string (handles encoding properly)
            content = await response.Content.ReadAsStringAsync();

            if (content.Length > MaxConfigSize)
            {
                return BadRequest(new ImportFromUrlResponse(false, null, $"File too large. Maximum size is {MaxConfigSize / 1024 / 1024} MB"));
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to fetch config from URL: {Url}", request.Url);
            return BadRequest(new ImportFromUrlResponse(false, null, $"Failed to fetch URL: {ex.Message}"));
        }
        catch (TaskCanceledException)
        {
            return BadRequest(new ImportFromUrlResponse(false, null, "Request timed out"));
        }

        // Parse XML securely (prevent XXE)
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersFromEntities = 0
            };

            using var stringReader = new StringReader(content);
            using var xmlReader = XmlReader.Create(stringReader, settings);

            // Try to load to verify it's valid XML
            var doc = XDocument.Load(xmlReader);

            // Re-serialize to normalize the content
            content = doc.ToString(SaveOptions.None);
        }
        catch (XmlException ex)
        {
            return BadRequest(new ImportFromUrlResponse(false, null, $"Invalid XML: {ex.Message}"));
        }

        // Validate it's a valid Sysmon config
        var validationResult = _validationService.Validate(content);
        if (!validationResult.IsValid)
        {
            return BadRequest(new ImportFromUrlResponse(false, null, $"Invalid Sysmon configuration: {validationResult.Message}"));
        }

        // Extract filename from URL or use default
        var filename = GetFilenameFromUrl(uri) ?? "imported-config.xml";

        // Parse SCPTAG from config
        var tag = ParseScpTag(content);

        // Calculate hash
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

        var config = new Config
        {
            Filename = filename,
            Tag = tag,
            Content = content,
            Hash = hash,
            UploadedBy = User.Identity?.Name,
            IsValid = validationResult.IsValid,
            ValidationMessage = validationResult.Message,
            SourceUrl = request.Url
        };

        _db.Configs.Add(config);
        await _db.SaveChangesAsync();

        await _auditService.LogAsync(User.Identity?.Name, AuditAction.ConfigUpload,
            new { ConfigId = config.Id, Filename = filename, Tag = tag, SourceUrl = request.Url });

        _logger.LogInformation("User {User} imported config from URL {Url} as {Filename} with tag {Tag}",
            User.Identity?.Name, request.Url, filename, tag);

        return CreatedAtAction(nameof(GetConfig), new { id = config.Id },
            new ConfigDto(config.Id, config.Filename, config.Tag, config.Hash, config.UploadedBy, config.UploadedAt,
                config.IsValid, config.ValidationMessage, config.SourceUrl));
    }

    private static bool IsPrivateOrLocalAddress(string host)
    {
        // Check for localhost variations
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("::1", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Try to parse as IP address
        if (System.Net.IPAddress.TryParse(host, out var ip))
        {
            // Check for private IP ranges
            var bytes = ip.GetAddressBytes();

            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                // 10.0.0.0/8
                if (bytes[0] == 10) return true;
                // 172.16.0.0/12
                if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
                // 192.168.0.0/16
                if (bytes[0] == 192 && bytes[1] == 168) return true;
                // 127.0.0.0/8 (loopback)
                if (bytes[0] == 127) return true;
                // 169.254.0.0/16 (link-local)
                if (bytes[0] == 169 && bytes[1] == 254) return true;
            }
        }

        return false;
    }

    private static string? GetFilenameFromUrl(Uri uri)
    {
        var path = uri.AbsolutePath;
        if (string.IsNullOrEmpty(path) || path == "/")
            return null;

        var filename = Path.GetFileName(path);
        if (string.IsNullOrEmpty(filename))
            return null;

        // Sanitize filename - remove any potentially dangerous characters
        filename = string.Concat(filename.Split(Path.GetInvalidFileNameChars()));

        // Ensure it has .xml extension
        if (!filename.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            filename += ".xml";

        return filename;
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

        // Re-validate the config
        var validationResult = _validationService.Validate(request.Content);
        config.IsValid = validationResult.IsValid;
        config.ValidationMessage = validationResult.Message;

        await _db.SaveChangesAsync();

        await _auditService.LogAsync(User.Identity?.Name, AuditAction.ConfigUpdate,
            new { ConfigId = id, Filename = config.Filename, OldHash = oldHash, NewHash = config.Hash, IsValid = validationResult.IsValid });

        _logger.LogInformation("User {User} updated config {Id} (new hash: {Hash}, valid: {IsValid})",
            User.Identity?.Name, id, config.Hash, validationResult.IsValid);

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
    DateTime UploadedAt,
    bool IsValid = true,
    string? ValidationMessage = null,
    string? SourceUrl = null);

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

public record ImportFromUrlRequest(
    string Url);

public record ImportFromUrlResponse(
    bool Success,
    ConfigDto? Config,
    string? Error);
