using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using SysmonConfigPusher.Core.Interfaces;
using SysmonConfigPusher.Core.Models;
using SysmonConfigPusher.Data;
using SysmonConfigPusher.Service.BackgroundServices;
using SysmonConfigPusher.Service.Services;

namespace SysmonConfigPusher.Service.Tests;

public class ScheduledDeploymentWorkerTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly Mock<IDeploymentQueue> _mockDeploymentQueue;
    private readonly Mock<ILogger<ScheduledDeploymentWorker>> _mockLogger;
    private readonly string _databaseName;

    public ScheduledDeploymentWorkerTests()
    {
        _databaseName = Guid.NewGuid().ToString();
        _mockDeploymentQueue = new Mock<IDeploymentQueue>();
        _mockLogger = new Mock<ILogger<ScheduledDeploymentWorker>>();

        var services = new ServiceCollection();
        services.AddDbContext<SysmonDbContext>(options =>
            options.UseInMemoryDatabase(_databaseName));
        services.AddScoped<IAuditService, MockAuditService>();

        _serviceProvider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SysmonDbContext>();
        db.Database.EnsureDeleted();
        _serviceProvider.Dispose();
    }

    private SysmonDbContext GetDbContext()
    {
        var scope = _serviceProvider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<SysmonDbContext>();
    }

    [Fact]
    public async Task ProcessDueDeployments_ProcessesDueDeployment()
    {
        // Arrange
        using (var db = GetDbContext())
        {
            var computer = new Computer { Hostname = "TEST-PC" };
            db.Computers.Add(computer);
            await db.SaveChangesAsync();

            var scheduledDeployment = new ScheduledDeployment
            {
                Operation = "test",
                ScheduledAt = DateTime.UtcNow.AddMinutes(-5), // Due 5 minutes ago
                Status = "Pending",
                CreatedBy = "TestUser",
                Computers = new List<ScheduledDeploymentComputer>
                {
                    new() { ComputerId = computer.Id }
                }
            };
            db.ScheduledDeployments.Add(scheduledDeployment);
            await db.SaveChangesAsync();
        }

        var worker = new ScheduledDeploymentWorker(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            _mockDeploymentQueue.Object,
            _mockLogger.Object);

        // Act
        await worker.TestProcessDueDeployments(CancellationToken.None);

        // Assert
        using (var db = GetDbContext())
        {
            var updatedDeployment = await db.ScheduledDeployments.FirstAsync();
            Assert.Equal("Running", updatedDeployment.Status);
            Assert.NotNull(updatedDeployment.DeploymentJobId);
        }

        // Verify deployment was enqueued
        _mockDeploymentQueue.Verify(q => q.Enqueue(It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public async Task ProcessDueDeployments_IgnoresFutureDeployments()
    {
        // Arrange
        using (var db = GetDbContext())
        {
            var computer = new Computer { Hostname = "TEST-PC" };
            db.Computers.Add(computer);
            await db.SaveChangesAsync();

            var scheduledDeployment = new ScheduledDeployment
            {
                Operation = "test",
                ScheduledAt = DateTime.UtcNow.AddHours(1), // 1 hour in the future
                Status = "Pending",
                CreatedBy = "TestUser",
                Computers = new List<ScheduledDeploymentComputer>
                {
                    new() { ComputerId = computer.Id }
                }
            };
            db.ScheduledDeployments.Add(scheduledDeployment);
            await db.SaveChangesAsync();
        }

        var worker = new ScheduledDeploymentWorker(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            _mockDeploymentQueue.Object,
            _mockLogger.Object);

        // Act
        await worker.TestProcessDueDeployments(CancellationToken.None);

        // Assert
        using (var db = GetDbContext())
        {
            var unchangedDeployment = await db.ScheduledDeployments.FirstAsync();
            Assert.Equal("Pending", unchangedDeployment.Status); // Still pending
            Assert.Null(unchangedDeployment.DeploymentJobId);
        }

        // Verify nothing was enqueued
        _mockDeploymentQueue.Verify(q => q.Enqueue(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ProcessDueDeployments_IgnoresAlreadyProcessedDeployments()
    {
        // Arrange
        using (var db = GetDbContext())
        {
            var computer = new Computer { Hostname = "TEST-PC" };
            db.Computers.Add(computer);

            var job = new DeploymentJob
            {
                Operation = "test",
                Status = "Running",
                StartedBy = "TestUser"
            };
            db.DeploymentJobs.Add(job);
            await db.SaveChangesAsync();

            var scheduledDeployment = new ScheduledDeployment
            {
                Operation = "test",
                ScheduledAt = DateTime.UtcNow.AddMinutes(-5),
                Status = "Running", // Already running
                CreatedBy = "TestUser",
                DeploymentJobId = job.Id,
                Computers = new List<ScheduledDeploymentComputer>
                {
                    new() { ComputerId = computer.Id }
                }
            };
            db.ScheduledDeployments.Add(scheduledDeployment);
            await db.SaveChangesAsync();
        }

        var worker = new ScheduledDeploymentWorker(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            _mockDeploymentQueue.Object,
            _mockLogger.Object);

        // Act
        await worker.TestProcessDueDeployments(CancellationToken.None);

        // Assert - should not be processed again
        _mockDeploymentQueue.Verify(q => q.Enqueue(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ProcessDueDeployments_ProcessesMultipleDueDeployments()
    {
        // Arrange
        using (var db = GetDbContext())
        {
            var computer1 = new Computer { Hostname = "TEST-PC1" };
            var computer2 = new Computer { Hostname = "TEST-PC2" };
            db.Computers.AddRange(computer1, computer2);
            await db.SaveChangesAsync();

            var deployment1 = new ScheduledDeployment
            {
                Operation = "test",
                ScheduledAt = DateTime.UtcNow.AddMinutes(-10),
                Status = "Pending",
                CreatedBy = "TestUser",
                Computers = new List<ScheduledDeploymentComputer>
                {
                    new() { ComputerId = computer1.Id }
                }
            };
            var deployment2 = new ScheduledDeployment
            {
                Operation = "update",
                ScheduledAt = DateTime.UtcNow.AddMinutes(-5),
                Status = "Pending",
                CreatedBy = "TestUser",
                Computers = new List<ScheduledDeploymentComputer>
                {
                    new() { ComputerId = computer2.Id }
                }
            };
            db.ScheduledDeployments.AddRange(deployment1, deployment2);
            await db.SaveChangesAsync();
        }

        var worker = new ScheduledDeploymentWorker(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            _mockDeploymentQueue.Object,
            _mockLogger.Object);

        // Act
        await worker.TestProcessDueDeployments(CancellationToken.None);

        // Assert - both should be processed
        _mockDeploymentQueue.Verify(q => q.Enqueue(It.IsAny<int>()), Times.Exactly(2));

        using (var db = GetDbContext())
        {
            var processedDeployments = await db.ScheduledDeployments.ToListAsync();
            Assert.All(processedDeployments, d => Assert.Equal("Running", d.Status));
        }
    }
}

/// <summary>
/// Mock audit service for tests
/// </summary>
public class MockAuditService : IAuditService
{
    public Task LogAsync(string? user, string action, string? details = null, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }

    public Task LogAsync(string? user, AuditAction action, object? details = null, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
/// Extension to expose protected method for testing
/// </summary>
public static class ScheduledDeploymentWorkerTestExtensions
{
    public static Task TestProcessDueDeployments(this ScheduledDeploymentWorker worker, CancellationToken ct)
    {
        // Use reflection to call the protected method
        var method = typeof(ScheduledDeploymentWorker)
            .GetMethod("ProcessDueDeploymentsAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (Task)method!.Invoke(worker, new object[] { ct })!;
    }
}
