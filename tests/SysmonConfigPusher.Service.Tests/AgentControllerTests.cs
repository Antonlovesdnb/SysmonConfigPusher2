using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using SysmonConfigPusher.Core.Models;
using SysmonConfigPusher.Data;
using SysmonConfigPusher.Service.Controllers;
using SysmonConfigPusher.Shared;

namespace SysmonConfigPusher.Service.Tests;

public class AgentControllerTests : IDisposable
{
    private readonly SysmonDbContext _db;
    private readonly Mock<ILogger<AgentController>> _mockLogger;
    private readonly string _validToken = "test-registration-token";

    public AgentControllerTests()
    {
        var options = new DbContextOptionsBuilder<SysmonDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new SysmonDbContext(options);
        _mockLogger = new Mock<ILogger<AgentController>>();
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    private AgentController CreateController(string? registrationToken = "USE_DEFAULT", bool disableRegistration = false)
    {
        var configDict = new Dictionary<string, string?>
        {
            ["Agent:PollIntervalSeconds"] = "30"
        };

        if (!disableRegistration)
        {
            configDict["Agent:RegistrationToken"] = registrationToken == "USE_DEFAULT" ? _validToken : registrationToken;
        }
        // When disableRegistration is true, we don't add the token at all

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();

        return new AgentController(_db, _mockLogger.Object, config);
    }

    private AgentController CreateControllerWithHttpContext(string? authToken, string? agentId)
    {
        var controller = CreateController();
        var httpContext = new DefaultHttpContext();

        if (!string.IsNullOrEmpty(authToken))
            httpContext.Request.Headers[AgentConstants.Headers.AuthToken] = authToken;
        if (!string.IsNullOrEmpty(agentId))
            httpContext.Request.Headers[AgentConstants.Headers.AgentId] = agentId;

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        return controller;
    }

    #region Registration Tests

    [Fact]
    public async Task Register_WithValidToken_CreatesNewAgent()
    {
        // Arrange
        var controller = CreateController();
        var request = new AgentRegistrationRequest
        {
            AgentId = "agent-123",
            Hostname = "TEST-PC",
            RegistrationToken = _validToken,
            OperatingSystem = "Windows 11",
            AgentVersion = "1.0.0",
            Tags = new List<string> { "production", "web-server" }
        };

        // Act
        var result = await controller.Register(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<AgentRegistrationResponse>(okResult.Value);

        Assert.True(response.Accepted);
        Assert.NotNull(response.AuthToken);
        Assert.True(response.ComputerId > 0);
        Assert.Equal(30, response.PollIntervalSeconds);

        // Verify computer was created
        var computer = await _db.Computers.FirstOrDefaultAsync(c => c.AgentId == "agent-123");
        Assert.NotNull(computer);
        Assert.Equal("TEST-PC", computer.Hostname);
        Assert.True(computer.IsAgentManaged);
        Assert.Equal("production,web-server", computer.AgentTags);
    }

    [Fact]
    public async Task Register_WithInvalidToken_RejectsRegistration()
    {
        // Arrange
        var controller = CreateController();
        var request = new AgentRegistrationRequest
        {
            AgentId = "agent-123",
            Hostname = "TEST-PC",
            RegistrationToken = "invalid-token",
            OperatingSystem = "Windows 11",
            AgentVersion = "1.0.0"
        };

        // Act
        var result = await controller.Register(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<AgentRegistrationResponse>(okResult.Value);

        Assert.False(response.Accepted);
        Assert.Equal("Invalid registration token", response.Message);
    }

    [Fact]
    public async Task Register_WhenNotEnabled_RejectsRegistration()
    {
        // Arrange
        var controller = CreateController(disableRegistration: true);
        var request = new AgentRegistrationRequest
        {
            AgentId = "agent-123",
            Hostname = "TEST-PC",
            RegistrationToken = "any-token",
            OperatingSystem = "Windows 11",
            AgentVersion = "1.0.0"
        };

        // Act
        var result = await controller.Register(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<AgentRegistrationResponse>(okResult.Value);

        Assert.False(response.Accepted);
        Assert.Contains("not enabled", response.Message);
    }

    [Fact]
    public async Task Register_ExistingAgent_ReRegisters()
    {
        // Arrange
        var existingComputer = new Computer
        {
            Hostname = "OLD-HOSTNAME",
            AgentId = "agent-123",
            AgentAuthToken = "existing-token",
            IsAgentManaged = true,
            AgentVersion = "0.9.0"
        };
        _db.Computers.Add(existingComputer);
        await _db.SaveChangesAsync();

        var controller = CreateController();
        var request = new AgentRegistrationRequest
        {
            AgentId = "agent-123",
            Hostname = "NEW-HOSTNAME",
            RegistrationToken = _validToken,
            OperatingSystem = "Windows 11",
            AgentVersion = "1.0.0"
        };

        // Act
        var result = await controller.Register(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<AgentRegistrationResponse>(okResult.Value);

        Assert.True(response.Accepted);
        Assert.Equal("existing-token", response.AuthToken); // Should return existing token

        // Verify computer was updated
        var computer = await _db.Computers.FirstAsync(c => c.AgentId == "agent-123");
        Assert.Equal("NEW-HOSTNAME", computer.Hostname);
        Assert.Equal("1.0.0", computer.AgentVersion);
    }

    [Fact]
    public async Task Register_ExistingWmiComputer_ConvertsToAgent()
    {
        // Arrange - Computer exists from WMI scan
        var existingComputer = new Computer
        {
            Hostname = "TEST-PC",
            IsAgentManaged = false,
            DistinguishedName = "CN=TEST-PC,OU=Computers,DC=test,DC=local"
        };
        _db.Computers.Add(existingComputer);
        await _db.SaveChangesAsync();

        var controller = CreateController();
        var request = new AgentRegistrationRequest
        {
            AgentId = "agent-456",
            Hostname = "TEST-PC",
            RegistrationToken = _validToken,
            OperatingSystem = "Windows 11",
            AgentVersion = "1.0.0"
        };

        // Act
        var result = await controller.Register(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<AgentRegistrationResponse>(okResult.Value);

        Assert.True(response.Accepted);
        Assert.Equal(existingComputer.Id, response.ComputerId);

        // Verify computer was converted
        var computer = await _db.Computers.FirstAsync(c => c.Id == existingComputer.Id);
        Assert.True(computer.IsAgentManaged);
        Assert.Equal("agent-456", computer.AgentId);
        Assert.NotNull(computer.AgentAuthToken);
    }

    #endregion

    #region Heartbeat Tests

    [Fact]
    public async Task Heartbeat_WithValidAuth_UpdatesStatus()
    {
        // Arrange
        var computer = new Computer
        {
            Hostname = "TEST-PC",
            AgentId = "agent-123",
            AgentAuthToken = "valid-token",
            IsAgentManaged = true
        };
        _db.Computers.Add(computer);
        await _db.SaveChangesAsync();

        var controller = CreateControllerWithHttpContext("valid-token", "agent-123");
        var heartbeat = new AgentHeartbeat
        {
            AgentId = "agent-123",
            Status = new AgentStatusPayload
            {
                AgentVersion = "1.0.0",
                Hostname = "TEST-PC",
                SysmonInstalled = true,
                SysmonVersion = "15.15.0.0",
                SysmonPath = @"C:\Windows\Sysmon64.exe",
                ConfigHash = "abc123",
                OperatingSystem = "Windows 11"
            }
        };

        // Act
        var result = await controller.Heartbeat(heartbeat);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<HeartbeatResponse>(okResult.Value);

        Assert.True(response.Registered);
        Assert.Equal(30, response.NewPollIntervalSeconds);

        // Verify computer was updated
        await _db.Entry(computer).ReloadAsync();
        Assert.Equal("15.15.0.0", computer.SysmonVersion);
        Assert.Equal("abc123", computer.ConfigHash);
        Assert.NotNull(computer.AgentLastHeartbeat);
    }

    [Fact]
    public async Task Heartbeat_WithInvalidAuth_ReturnsNotRegistered()
    {
        // Arrange
        var computer = new Computer
        {
            Hostname = "TEST-PC",
            AgentId = "agent-123",
            AgentAuthToken = "valid-token",
            IsAgentManaged = true
        };
        _db.Computers.Add(computer);
        await _db.SaveChangesAsync();

        var controller = CreateControllerWithHttpContext("wrong-token", "agent-123");
        var heartbeat = new AgentHeartbeat
        {
            AgentId = "agent-123",
            Status = new AgentStatusPayload
            {
                AgentVersion = "1.0.0",
                Hostname = "TEST-PC"
            }
        };

        // Act
        var result = await controller.Heartbeat(heartbeat);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<HeartbeatResponse>(okResult.Value);

        Assert.False(response.Registered);
    }

    [Fact]
    public async Task Heartbeat_WithMissingAuth_ReturnsUnauthorized()
    {
        // Arrange
        var controller = CreateControllerWithHttpContext(null, null);
        var heartbeat = new AgentHeartbeat
        {
            AgentId = "agent-123",
            Status = new AgentStatusPayload
            {
                AgentVersion = "1.0.0",
                Hostname = "TEST-PC"
            }
        };

        // Act
        var result = await controller.Heartbeat(heartbeat);

        // Assert
        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Fact]
    public async Task Heartbeat_ReturnsPendingCommands()
    {
        // Arrange
        var computer = new Computer
        {
            Hostname = "TEST-PC",
            AgentId = "agent-123",
            AgentAuthToken = "valid-token",
            IsAgentManaged = true
        };
        _db.Computers.Add(computer);
        await _db.SaveChangesAsync();

        var pendingCommand = new AgentPendingCommand
        {
            ComputerId = computer.Id,
            CommandId = "cmd-1",
            CommandType = "GetStatus",
            CreatedAt = DateTime.UtcNow
        };
        _db.AgentPendingCommands.Add(pendingCommand);
        await _db.SaveChangesAsync();

        var controller = CreateControllerWithHttpContext("valid-token", "agent-123");
        var heartbeat = new AgentHeartbeat
        {
            AgentId = "agent-123",
            Status = new AgentStatusPayload
            {
                AgentVersion = "1.0.0",
                Hostname = "TEST-PC"
            }
        };

        // Act
        var result = await controller.Heartbeat(heartbeat);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<HeartbeatResponse>(okResult.Value);

        Assert.Single(response.PendingCommands);
        Assert.Equal("cmd-1", response.PendingCommands[0].CommandId);

        // Verify command was marked as sent
        await _db.Entry(pendingCommand).ReloadAsync();
        Assert.NotNull(pendingCommand.SentAt);
    }

    #endregion

    #region Command Result Tests

    [Fact]
    public async Task CommandResult_WithValidAuth_UpdatesCommand()
    {
        // Arrange
        var computer = new Computer
        {
            Hostname = "TEST-PC",
            AgentId = "agent-123",
            AgentAuthToken = "valid-token",
            IsAgentManaged = true
        };
        _db.Computers.Add(computer);
        await _db.SaveChangesAsync();

        var command = new AgentPendingCommand
        {
            ComputerId = computer.Id,
            CommandId = "cmd-1",
            CommandType = "PushConfig",
            CreatedAt = DateTime.UtcNow,
            SentAt = DateTime.UtcNow
        };
        _db.AgentPendingCommands.Add(command);
        await _db.SaveChangesAsync();

        var controller = CreateControllerWithHttpContext("valid-token", "agent-123");
        var agentResponse = new AgentResponse
        {
            CommandId = "cmd-1",
            Status = CommandResultStatus.Success,
            Message = "Config applied successfully"
        };

        // Act
        var result = await controller.CommandResult(agentResponse);

        // Assert
        Assert.IsType<OkResult>(result);

        // Verify command was updated
        await _db.Entry(command).ReloadAsync();
        Assert.NotNull(command.CompletedAt);
        Assert.Equal("Success", command.ResultStatus);
        Assert.Equal("Config applied successfully", command.ResultMessage);
    }

    [Fact]
    public async Task CommandResult_UpdatesDeploymentResult()
    {
        // Arrange
        var computer = new Computer
        {
            Hostname = "TEST-PC",
            AgentId = "agent-123",
            AgentAuthToken = "valid-token",
            IsAgentManaged = true
        };
        _db.Computers.Add(computer);

        var job = new DeploymentJob
        {
            Operation = "pushconfig",
            Status = "Running",
            StartedBy = "TestUser"
        };
        _db.DeploymentJobs.Add(job);
        await _db.SaveChangesAsync();

        var deploymentResult = new DeploymentResult
        {
            JobId = job.Id,
            ComputerId = computer.Id,
            Success = false,
            Message = "Pending"
        };
        _db.DeploymentResults.Add(deploymentResult);

        var command = new AgentPendingCommand
        {
            ComputerId = computer.Id,
            CommandId = "cmd-1",
            CommandType = "PushConfig",
            CreatedAt = DateTime.UtcNow,
            SentAt = DateTime.UtcNow,
            DeploymentJobId = job.Id
        };
        _db.AgentPendingCommands.Add(command);
        await _db.SaveChangesAsync();

        var controller = CreateControllerWithHttpContext("valid-token", "agent-123");
        var agentResponse = new AgentResponse
        {
            CommandId = "cmd-1",
            Status = CommandResultStatus.Success,
            Message = "Config applied successfully"
        };

        // Act
        var result = await controller.CommandResult(agentResponse);

        // Assert
        Assert.IsType<OkResult>(result);

        // Verify deployment result was updated
        await _db.Entry(deploymentResult).ReloadAsync();
        Assert.True(deploymentResult.Success);
        Assert.Equal("Config applied successfully", deploymentResult.Message);
        Assert.NotNull(deploymentResult.CompletedAt);
    }

    [Fact]
    public async Task CommandResult_WithInvalidAuth_ReturnsUnauthorized()
    {
        // Arrange
        var controller = CreateControllerWithHttpContext("wrong-token", "agent-123");
        var agentResponse = new AgentResponse
        {
            CommandId = "cmd-1",
            Status = CommandResultStatus.Success,
            Message = "Done"
        };

        // Act
        var result = await controller.CommandResult(agentResponse);

        // Assert
        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task CommandResult_ForUnknownCommand_ReturnsNotFound()
    {
        // Arrange
        var computer = new Computer
        {
            Hostname = "TEST-PC",
            AgentId = "agent-123",
            AgentAuthToken = "valid-token",
            IsAgentManaged = true
        };
        _db.Computers.Add(computer);
        await _db.SaveChangesAsync();

        var controller = CreateControllerWithHttpContext("valid-token", "agent-123");
        var agentResponse = new AgentResponse
        {
            CommandId = "unknown-cmd",
            Status = CommandResultStatus.Success,
            Message = "Done"
        };

        // Act
        var result = await controller.CommandResult(agentResponse);

        // Assert
        Assert.IsType<NotFoundResult>(result);
    }

    #endregion
}
