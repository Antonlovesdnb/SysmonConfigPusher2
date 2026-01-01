using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SysmonConfigPusher.Data;
using SysmonConfigPusher.Core.Models;
using SysmonConfigPusher.Shared;

namespace SysmonConfigPusher.Service.Controllers;

/// <summary>
/// Controller for agent communication endpoints.
/// These endpoints are NOT protected by Windows auth - they use token-based auth.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[AllowAnonymous] // Agent endpoints use token auth, not Windows auth
public class AgentController : ControllerBase
{
    private readonly SysmonDbContext _db;
    private readonly ILogger<AgentController> _logger;
    private readonly IConfiguration _configuration;

    public AgentController(
        SysmonDbContext db,
        ILogger<AgentController> logger,
        IConfiguration configuration)
    {
        _db = db;
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Register a new agent
    /// </summary>
    [HttpPost("register")]
    public async Task<ActionResult<AgentRegistrationResponse>> Register(
        [FromBody] AgentRegistrationRequest request)
    {
        _logger.LogInformation("Agent registration request from {Hostname} (AgentId: {AgentId})",
            request.Hostname, request.AgentId);

        // Validate registration token
        var validToken = _configuration["Agent:RegistrationToken"];
        if (string.IsNullOrEmpty(validToken))
        {
            _logger.LogWarning("Agent registration attempted but no registration token configured");
            return Ok(new AgentRegistrationResponse
            {
                Accepted = false,
                Message = "Agent registration not enabled on this server"
            });
        }

        if (!string.Equals(request.RegistrationToken, validToken, StringComparison.Ordinal))
        {
            _logger.LogWarning("Agent registration failed: invalid token from {Hostname}", request.Hostname);
            return Ok(new AgentRegistrationResponse
            {
                Accepted = false,
                Message = "Invalid registration token"
            });
        }

        // Check if agent is already registered
        var existingComputer = await _db.Computers
            .FirstOrDefaultAsync(c => c.AgentId == request.AgentId);

        if (existingComputer != null)
        {
            // Re-registration - update and return existing token
            existingComputer.Hostname = request.Hostname;
            existingComputer.OperatingSystem = request.OperatingSystem;
            existingComputer.AgentVersion = request.AgentVersion;
            existingComputer.AgentLastHeartbeat = DateTime.UtcNow;
            existingComputer.LastSeen = DateTime.UtcNow;
            existingComputer.AgentTags = request.Tags.Count > 0 ? string.Join(",", request.Tags) : null;

            await _db.SaveChangesAsync();

            _logger.LogInformation("Agent re-registered: {Hostname} (Id: {Id})",
                request.Hostname, existingComputer.Id);

            return Ok(new AgentRegistrationResponse
            {
                Accepted = true,
                ComputerId = existingComputer.Id,
                AuthToken = existingComputer.AgentAuthToken,
                PollIntervalSeconds = GetPollInterval()
            });
        }

        // Check if computer exists by hostname (might be WMI-managed, convert to agent)
        var computerByHostname = await _db.Computers
            .FirstOrDefaultAsync(c => c.Hostname.ToLower() == request.Hostname.ToLower());

        if (computerByHostname != null)
        {
            // Convert existing computer to agent-managed
            computerByHostname.IsAgentManaged = true;
            computerByHostname.AgentId = request.AgentId;
            computerByHostname.AgentVersion = request.AgentVersion;
            computerByHostname.AgentAuthToken = GenerateAuthToken();
            computerByHostname.AgentLastHeartbeat = DateTime.UtcNow;
            computerByHostname.OperatingSystem = request.OperatingSystem;
            computerByHostname.LastSeen = DateTime.UtcNow;
            computerByHostname.AgentTags = request.Tags.Count > 0 ? string.Join(",", request.Tags) : null;

            await _db.SaveChangesAsync();

            _logger.LogInformation("Computer {Hostname} converted to agent-managed (Id: {Id})",
                request.Hostname, computerByHostname.Id);

            return Ok(new AgentRegistrationResponse
            {
                Accepted = true,
                ComputerId = computerByHostname.Id,
                AuthToken = computerByHostname.AgentAuthToken,
                PollIntervalSeconds = GetPollInterval()
            });
        }

        // New agent registration
        var newComputer = new Computer
        {
            Hostname = request.Hostname,
            OperatingSystem = request.OperatingSystem,
            IsAgentManaged = true,
            AgentId = request.AgentId,
            AgentVersion = request.AgentVersion,
            AgentAuthToken = GenerateAuthToken(),
            AgentLastHeartbeat = DateTime.UtcNow,
            LastSeen = DateTime.UtcNow,
            AgentTags = request.Tags.Count > 0 ? string.Join(",", request.Tags) : null
        };

        _db.Computers.Add(newComputer);
        await _db.SaveChangesAsync();

        _logger.LogInformation("New agent registered: {Hostname} (Id: {Id})",
            request.Hostname, newComputer.Id);

        return Ok(new AgentRegistrationResponse
        {
            Accepted = true,
            ComputerId = newComputer.Id,
            AuthToken = newComputer.AgentAuthToken,
            PollIntervalSeconds = GetPollInterval()
        });
    }

    /// <summary>
    /// Agent heartbeat - returns pending commands
    /// </summary>
    [HttpPost("heartbeat")]
    public async Task<ActionResult<HeartbeatResponse>> Heartbeat(
        [FromBody] AgentHeartbeat heartbeat)
    {
        // Validate auth token
        var authToken = Request.Headers[AgentConstants.Headers.AuthToken].FirstOrDefault();
        if (string.IsNullOrEmpty(authToken))
        {
            return Unauthorized(new HeartbeatResponse { Registered = false });
        }

        var computer = await _db.Computers
            .Include(c => c.PendingCommands.Where(cmd => cmd.CompletedAt == null))
            .FirstOrDefaultAsync(c => c.AgentId == heartbeat.AgentId && c.AgentAuthToken == authToken);

        if (computer == null)
        {
            _logger.LogWarning("Heartbeat from unknown/unauthorized agent: {AgentId}", heartbeat.AgentId);
            return Ok(new HeartbeatResponse { Registered = false });
        }

        // Update computer status from heartbeat
        computer.AgentLastHeartbeat = DateTime.UtcNow;
        computer.LastSeen = DateTime.UtcNow;
        computer.LastInventoryScan = DateTime.UtcNow; // Heartbeat acts as an inventory scan for agents
        computer.AgentVersion = heartbeat.Status.AgentVersion;
        computer.SysmonVersion = heartbeat.Status.SysmonVersion;
        computer.SysmonPath = heartbeat.Status.SysmonPath;
        computer.ConfigHash = heartbeat.Status.ConfigHash;
        computer.OperatingSystem = heartbeat.Status.OperatingSystem;
        // Agent is online if responding - the frontend uses SysmonPath to determine if Sysmon is installed
        computer.LastScanStatus = "Online";

        // Get pending commands
        var pendingCommands = computer.PendingCommands
            .Where(c => c.SentAt == null)
            .OrderBy(c => c.CreatedAt)
            .Take(10) // Limit to 10 commands per heartbeat
            .ToList();

        // Mark commands as sent
        var now = DateTime.UtcNow;
        foreach (var cmd in pendingCommands)
        {
            cmd.SentAt = now;
        }

        await _db.SaveChangesAsync();

        // Convert to shared DTOs
        var commands = pendingCommands.Select(c => new AgentCommand
        {
            CommandId = c.CommandId,
            Type = Enum.Parse<AgentCommandType>(c.CommandType),
            IssuedAt = c.CreatedAt,
            Payload = c.Payload
        }).ToList();

        return Ok(new HeartbeatResponse
        {
            Registered = true,
            PendingCommands = commands,
            NewPollIntervalSeconds = GetPollInterval()
        });
    }

    /// <summary>
    /// Receive command result from agent
    /// </summary>
    [HttpPost("command-result")]
    public async Task<IActionResult> CommandResult([FromBody] AgentResponse result)
    {
        // Validate auth token
        var authToken = Request.Headers[AgentConstants.Headers.AuthToken].FirstOrDefault();
        var agentId = Request.Headers[AgentConstants.Headers.AgentId].FirstOrDefault();

        if (string.IsNullOrEmpty(authToken) || string.IsNullOrEmpty(agentId))
        {
            return Unauthorized();
        }

        var computer = await _db.Computers
            .FirstOrDefaultAsync(c => c.AgentId == agentId && c.AgentAuthToken == authToken);

        if (computer == null)
        {
            _logger.LogWarning("Command result from unknown/unauthorized agent: {AgentId}", agentId);
            return Unauthorized();
        }

        // Find the command
        var command = await _db.AgentPendingCommands
            .FirstOrDefaultAsync(c => c.CommandId == result.CommandId && c.ComputerId == computer.Id);

        if (command == null)
        {
            _logger.LogWarning("Command result for unknown command: {CommandId}", result.CommandId);
            return NotFound();
        }

        // Update command with result
        command.CompletedAt = DateTime.UtcNow;
        command.ResultStatus = result.Status.ToString();
        command.ResultMessage = result.Message;
        command.ResultPayload = result.Payload;

        // If this was associated with a deployment job, update the deployment result
        if (command.DeploymentJobId.HasValue)
        {
            var deploymentResult = await _db.DeploymentResults
                .FirstOrDefaultAsync(r => r.JobId == command.DeploymentJobId && r.ComputerId == computer.Id);

            if (deploymentResult != null)
            {
                deploymentResult.Success = result.Status == CommandResultStatus.Success;
                deploymentResult.Message = result.Message;
                deploymentResult.CompletedAt = DateTime.UtcNow;
            }
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation("Command {CommandId} completed with status {Status}",
            result.CommandId, result.Status);

        return Ok();
    }

    private static string GenerateAuthToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes);
    }

    private int GetPollInterval()
    {
        return _configuration.GetValue<int>("Agent:PollIntervalSeconds", 30);
    }
}
