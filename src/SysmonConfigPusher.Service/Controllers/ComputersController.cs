using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SysmonConfigPusher.Data;
using SysmonConfigPusher.Core.Models;
using SysmonConfigPusher.Core.Interfaces;

namespace SysmonConfigPusher.Service.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ComputersController : ControllerBase
{
    private readonly SysmonDbContext _db;
    private readonly IActiveDirectoryService _adService;
    private readonly IInventoryScanQueue _scanQueue;
    private readonly ILogger<ComputersController> _logger;

    public ComputersController(
        SysmonDbContext db,
        IActiveDirectoryService adService,
        IInventoryScanQueue scanQueue,
        ILogger<ComputersController> logger)
    {
        _db = db;
        _adService = adService;
        _scanQueue = scanQueue;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ComputerDto>>> GetComputers(
        [FromQuery] string? search = null,
        [FromQuery] int? groupId = null)
    {
        var query = _db.Computers.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(c => c.Hostname.Contains(search));
        }

        if (groupId.HasValue)
        {
            query = query.Where(c => c.GroupMemberships.Any(m => m.GroupId == groupId.Value));
        }

        var computers = await query
            .OrderBy(c => c.Hostname)
            .Select(c => new ComputerDto(
                c.Id,
                c.Hostname,
                c.DistinguishedName,
                c.OperatingSystem,
                c.LastSeen,
                c.SysmonVersion,
                c.SysmonPath,
                c.ConfigHash,
                c.LastDeployment,
                c.LastInventoryScan))
            .ToListAsync();

        return Ok(computers);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ComputerDto>> GetComputer(int id)
    {
        var computer = await _db.Computers.FindAsync(id);
        if (computer == null)
            return NotFound();

        return Ok(new ComputerDto(
            computer.Id,
            computer.Hostname,
            computer.DistinguishedName,
            computer.OperatingSystem,
            computer.LastSeen,
            computer.SysmonVersion,
            computer.SysmonPath,
            computer.ConfigHash,
            computer.LastDeployment,
            computer.LastInventoryScan));
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<RefreshResultDto>> RefreshFromAD(CancellationToken cancellationToken)
    {
        _logger.LogInformation("User {User} requested AD refresh", User.Identity?.Name);

        IEnumerable<AdComputer> adComputers;
        try
        {
            adComputers = await _adService.EnumerateComputersAsync(cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enumerate computers from Active Directory");
            return StatusCode(500, new { error = "Failed to connect to Active Directory", details = ex.Message });
        }

        var added = 0;
        var updated = 0;

        foreach (var adComputer in adComputers)
        {
            var existing = await _db.Computers
                .FirstOrDefaultAsync(c => c.Hostname == adComputer.Hostname, cancellationToken);

            if (existing == null)
            {
                _db.Computers.Add(new Computer
                {
                    Hostname = adComputer.Hostname,
                    DistinguishedName = adComputer.DistinguishedName,
                    OperatingSystem = adComputer.OperatingSystem,
                    LastSeen = adComputer.LastLogon
                });
                added++;
            }
            else
            {
                existing.DistinguishedName = adComputer.DistinguishedName;
                existing.OperatingSystem = adComputer.OperatingSystem;
                existing.LastSeen = adComputer.LastLogon;
                updated++;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("AD refresh complete: {Added} added, {Updated} updated", added, updated);

        return Ok(new RefreshResultDto(added, updated, $"Refresh complete: {added} added, {updated} updated"));
    }

    [HttpPost("test-connectivity")]
    public async Task<ActionResult<IEnumerable<ConnectivityResultDto>>> TestConnectivity(
        [FromBody] int[] computerIds,
        [FromServices] IRemoteExecutionService remoteExec,
        CancellationToken cancellationToken)
    {
        var computers = await _db.Computers
            .Where(c => computerIds.Contains(c.Id))
            .ToListAsync(cancellationToken);

        var results = new List<ConnectivityResultDto>();

        await Parallel.ForEachAsync(
            computers,
            new ParallelOptions { MaxDegreeOfParallelism = 10, CancellationToken = cancellationToken },
            async (computer, ct) =>
            {
                var reachable = await remoteExec.TestConnectivityAsync(computer.Hostname, ct);
                lock (results)
                {
                    results.Add(new ConnectivityResultDto(
                        computer.Id,
                        reachable,
                        reachable ? "WMI connection successful" : "WMI connection failed"));
                }
            });

        return Ok(results);
    }

    [HttpPost("scan")]
    public ActionResult<ScanResultDto> ScanComputers([FromBody] ScanRequest request)
    {
        _logger.LogInformation("User {User} requested inventory scan for {Count} computers",
            User.Identity?.Name, request.ComputerIds?.Length ?? -1);

        if (request.ComputerIds == null || request.ComputerIds.Length == 0)
        {
            _scanQueue.EnqueueAll();
            return Accepted(new ScanResultDto("Scan queued for all computers"));
        }

        _scanQueue.Enqueue(request.ComputerIds);
        return Accepted(new ScanResultDto($"Scan queued for {request.ComputerIds.Length} computers"));
    }

    [HttpPost("scan/all")]
    public ActionResult<ScanResultDto> ScanAllComputers()
    {
        _logger.LogInformation("User {User} requested full inventory scan", User.Identity?.Name);
        _scanQueue.EnqueueAll();
        return Accepted(new ScanResultDto("Scan queued for all computers"));
    }

    [HttpGet("groups")]
    public async Task<ActionResult<IEnumerable<ComputerGroupDto>>> GetGroups()
    {
        var groups = await _db.ComputerGroups
            .Select(g => new ComputerGroupDto(
                g.Id,
                g.Name,
                g.CreatedBy,
                g.CreatedAt,
                g.Members.Count))
            .ToListAsync();

        return Ok(groups);
    }

    [HttpPost("groups")]
    public async Task<ActionResult<ComputerGroupDto>> CreateGroup([FromBody] CreateGroupRequest request)
    {
        var group = new ComputerGroup
        {
            Name = request.Name,
            CreatedBy = User.Identity?.Name
        };

        _db.ComputerGroups.Add(group);

        if (request.ComputerIds?.Length > 0)
        {
            foreach (var computerId in request.ComputerIds)
            {
                group.Members.Add(new ComputerGroupMember
                {
                    ComputerId = computerId
                });
            }
        }

        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetGroups), new { id = group.Id },
            new ComputerGroupDto(group.Id, group.Name, group.CreatedBy, group.CreatedAt, group.Members.Count));
    }
}

public record ComputerDto(
    int Id,
    string Hostname,
    string? DistinguishedName,
    string? OperatingSystem,
    DateTime? LastSeen,
    string? SysmonVersion,
    string? SysmonPath,
    string? ConfigHash,
    DateTime? LastDeployment,
    DateTime? LastInventoryScan);

public record ComputerGroupDto(
    int Id,
    string Name,
    string? CreatedBy,
    DateTime CreatedAt,
    int MemberCount);

public record CreateGroupRequest(string Name, int[]? ComputerIds);

public record RefreshResultDto(int Added, int Updated, string Message);

public record ConnectivityResultDto(int ComputerId, bool Reachable, string? Message);

public record ScanRequest(int[]? ComputerIds);

public record ScanResultDto(string Message);
