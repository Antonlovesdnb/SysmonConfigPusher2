using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SysmonConfigPusher.Data;
using SysmonConfigPusher.Core.Models;

namespace SysmonConfigPusher.Service.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public partial class ConfigsController : ControllerBase
{
    private readonly SysmonDbContext _db;
    private readonly ILogger<ConfigsController> _logger;

    public ConfigsController(SysmonDbContext db, ILogger<ConfigsController> logger)
    {
        _db = db;
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

        _logger.LogInformation("User {User} uploaded config {Filename} with tag {Tag}",
            User.Identity?.Name, file.FileName, tag);

        return CreatedAtAction(nameof(GetConfig), new { id = config.Id },
            new ConfigDto(config.Id, config.Filename, config.Tag, config.Hash, config.UploadedBy, config.UploadedAt));
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteConfig(int id)
    {
        var config = await _db.Configs.FindAsync(id);
        if (config == null)
            return NotFound();

        config.IsActive = false;
        await _db.SaveChangesAsync();

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
