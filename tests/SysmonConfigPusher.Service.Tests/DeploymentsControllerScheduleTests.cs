using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using SysmonConfigPusher.Core.Interfaces;
using SysmonConfigPusher.Core.Models;
using SysmonConfigPusher.Data;
using SysmonConfigPusher.Service.Controllers;

namespace SysmonConfigPusher.Service.Tests;

public class DeploymentsControllerScheduleTests : IDisposable
{
    private readonly SysmonDbContext _db;
    private readonly Mock<IDeploymentQueue> _mockQueue;
    private readonly Mock<IAuditService> _mockAuditService;
    private readonly Mock<ILogger<DeploymentsController>> _mockLogger;
    private readonly DeploymentsController _controller;

    public DeploymentsControllerScheduleTests()
    {
        var options = new DbContextOptionsBuilder<SysmonDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new SysmonDbContext(options);
        _mockQueue = new Mock<IDeploymentQueue>();
        _mockAuditService = new Mock<IAuditService>();
        _mockLogger = new Mock<ILogger<DeploymentsController>>();

        _controller = new DeploymentsController(
            _db,
            _mockQueue.Object,
            _mockAuditService.Object,
            _mockLogger.Object);

        // Set up a mock user
        var claims = new[] { new Claim(ClaimTypes.Name, "TestUser") };
        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    [Fact]
    public async Task GetScheduledDeployments_ReturnsEmptyList_WhenNoneExist()
    {
        // Act
        var result = await _controller.GetScheduledDeployments();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var deployments = Assert.IsAssignableFrom<IEnumerable<ScheduledDeploymentDto>>(okResult.Value);
        Assert.Empty(deployments);
    }

    [Fact]
    public async Task GetScheduledDeployments_ReturnsPendingDeployments()
    {
        // Arrange
        var computer = new Computer { Hostname = "TEST-PC" };
        _db.Computers.Add(computer);
        await _db.SaveChangesAsync();

        var scheduled = new ScheduledDeployment
        {
            Operation = "install",
            ScheduledAt = DateTime.UtcNow.AddHours(1),
            Status = "Pending",
            CreatedBy = "TestUser"
        };
        _db.ScheduledDeployments.Add(scheduled);
        await _db.SaveChangesAsync();

        scheduled.Computers.Add(new ScheduledDeploymentComputer
        {
            ScheduledDeploymentId = scheduled.Id,
            ComputerId = computer.Id
        });
        await _db.SaveChangesAsync();

        // Act
        var result = await _controller.GetScheduledDeployments();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var deployments = Assert.IsAssignableFrom<IEnumerable<ScheduledDeploymentDto>>(okResult.Value).ToList();
        Assert.Single(deployments);
        Assert.Equal("install", deployments[0].Operation);
        Assert.Equal("Pending", deployments[0].Status);
    }

    [Fact]
    public async Task GetScheduledDeployments_ExcludesCompletedDeployments()
    {
        // Arrange
        var scheduled = new ScheduledDeployment
        {
            Operation = "install",
            ScheduledAt = DateTime.UtcNow.AddHours(-1),
            Status = "Completed",
            CreatedBy = "TestUser"
        };
        _db.ScheduledDeployments.Add(scheduled);
        await _db.SaveChangesAsync();

        // Act
        var result = await _controller.GetScheduledDeployments();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var deployments = Assert.IsAssignableFrom<IEnumerable<ScheduledDeploymentDto>>(okResult.Value);
        Assert.Empty(deployments);
    }

    [Fact]
    public async Task CreateScheduledDeployment_ReturnsCreated_WithValidRequest()
    {
        // Arrange
        var computer = new Computer { Hostname = "SCHEDULE-TEST-PC" };
        _db.Computers.Add(computer);
        await _db.SaveChangesAsync();

        var request = new CreateScheduledDeploymentRequest(
            Operation: "update",
            ConfigId: null,
            ScheduledAt: DateTime.UtcNow.AddHours(2),
            ComputerIds: new[] { computer.Id }
        );

        // Act
        var result = await _controller.CreateScheduledDeployment(request);

        // Assert
        var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        var dto = Assert.IsType<ScheduledDeploymentDto>(createdResult.Value);
        Assert.Equal("update", dto.Operation);
        Assert.Equal("Pending", dto.Status);

        // Verify it was persisted
        var inDb = await _db.ScheduledDeployments.FirstOrDefaultAsync();
        Assert.NotNull(inDb);
        Assert.Equal("update", inDb.Operation);
    }

    [Fact]
    public async Task CreateScheduledDeployment_ReturnsBadRequest_WhenNoComputers()
    {
        // Arrange
        var request = new CreateScheduledDeploymentRequest(
            Operation: "install",
            ConfigId: null,
            ScheduledAt: DateTime.UtcNow.AddHours(1),
            ComputerIds: Array.Empty<int>()
        );

        // Act
        var result = await _controller.CreateScheduledDeployment(request);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task CreateScheduledDeployment_ReturnsBadRequest_WhenTimeInPast()
    {
        // Arrange
        var computer = new Computer { Hostname = "PAST-TEST-PC" };
        _db.Computers.Add(computer);
        await _db.SaveChangesAsync();

        var request = new CreateScheduledDeploymentRequest(
            Operation: "install",
            ConfigId: null,
            ScheduledAt: DateTime.UtcNow.AddHours(-1), // In the past
            ComputerIds: new[] { computer.Id }
        );

        // Act
        var result = await _controller.CreateScheduledDeployment(request);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task CreateScheduledDeployment_AuditsAction()
    {
        // Arrange
        var computer = new Computer { Hostname = "AUDIT-TEST-PC" };
        _db.Computers.Add(computer);
        await _db.SaveChangesAsync();

        var request = new CreateScheduledDeploymentRequest(
            Operation: "update",
            ConfigId: null,
            ScheduledAt: DateTime.UtcNow.AddHours(2),
            ComputerIds: new[] { computer.Id }
        );

        // Act
        await _controller.CreateScheduledDeployment(request);

        // Assert
        _mockAuditService.Verify(
            a => a.LogAsync(
                It.IsAny<string?>(),
                AuditAction.ScheduledDeploymentCreate,
                It.IsAny<object?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CancelScheduledDeployment_ReturnsNoContent_WhenPending()
    {
        // Arrange
        var scheduled = new ScheduledDeployment
        {
            Operation = "install",
            ScheduledAt = DateTime.UtcNow.AddHours(1),
            Status = "Pending",
            CreatedBy = "TestUser"
        };
        _db.ScheduledDeployments.Add(scheduled);
        await _db.SaveChangesAsync();

        // Act
        var result = await _controller.CancelScheduledDeployment(scheduled.Id);

        // Assert
        Assert.IsType<NoContentResult>(result);

        // Verify in database
        var updated = await _db.ScheduledDeployments.FindAsync(scheduled.Id);
        Assert.NotNull(updated);
        Assert.Equal("Cancelled", updated.Status);
    }

    [Fact]
    public async Task CancelScheduledDeployment_ReturnsNotFound_WhenDoesNotExist()
    {
        // Act
        var result = await _controller.CancelScheduledDeployment(99999);

        // Assert
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task CancelScheduledDeployment_ReturnsBadRequest_WhenNotPending()
    {
        // Arrange
        var scheduled = new ScheduledDeployment
        {
            Operation = "install",
            ScheduledAt = DateTime.UtcNow.AddHours(-1),
            Status = "Running", // Not pending
            CreatedBy = "TestUser"
        };
        _db.ScheduledDeployments.Add(scheduled);
        await _db.SaveChangesAsync();

        // Act
        var result = await _controller.CancelScheduledDeployment(scheduled.Id);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task CancelScheduledDeployment_AuditsAction()
    {
        // Arrange
        var scheduled = new ScheduledDeployment
        {
            Operation = "install",
            ScheduledAt = DateTime.UtcNow.AddHours(1),
            Status = "Pending",
            CreatedBy = "TestUser"
        };
        _db.ScheduledDeployments.Add(scheduled);
        await _db.SaveChangesAsync();

        // Act
        await _controller.CancelScheduledDeployment(scheduled.Id);

        // Assert
        _mockAuditService.Verify(
            a => a.LogAsync(
                It.IsAny<string?>(),
                AuditAction.ScheduledDeploymentCancel,
                It.IsAny<object?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
