using Microsoft.EntityFrameworkCore;
using SysmonConfigPusher.Core.Interfaces;
using SysmonConfigPusher.Data;

namespace SysmonConfigPusher.Service.BackgroundServices;

public class InventoryScanWorker : BackgroundService
{
    private readonly IInventoryScanQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InventoryScanWorker> _logger;

    // WMI connections are expensive - keep parallelism reasonable
    private const int ScanParallelism = 5;

    public InventoryScanWorker(
        IInventoryScanQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<InventoryScanWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Inventory scan worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            var request = await _queue.DequeueAsync(stoppingToken);
            if (request == null) continue;

            try
            {
                await ProcessScanRequestAsync(request, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing inventory scan request");
            }
        }

        _logger.LogInformation("Inventory scan worker stopped");
    }

    private async Task ProcessScanRequestAsync(InventoryScanRequest request, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SysmonDbContext>();
        var remoteExec = scope.ServiceProvider.GetRequiredService<IRemoteExecutionService>();

        // Get computers to scan
        IQueryable<Core.Models.Computer> query = db.Computers;
        if (!request.ScanAll && request.ComputerIds != null)
        {
            query = query.Where(c => request.ComputerIds.Contains(c.Id));
        }

        var computers = await query.ToListAsync(cancellationToken);

        _logger.LogInformation("Starting inventory scan for {Count} computers", computers.Count);

        var scanned = 0;
        var succeeded = 0;

        await Parallel.ForEachAsync(
            computers,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = ScanParallelism,
                CancellationToken = cancellationToken
            },
            async (computer, ct) =>
            {
                try
                {
                    var sysmonPath = await remoteExec.GetSysmonPathAsync(computer.Hostname, ct);
                    string? sysmonVersion = null;

                    if (!string.IsNullOrEmpty(sysmonPath))
                    {
                        sysmonVersion = await remoteExec.GetSysmonVersionAsync(computer.Hostname, ct);
                    }
                    else
                    {
                        // Sysmon not installed - clear the config hash and tag
                        computer.ConfigHash = null;
                        computer.ConfigTag = null;
                    }

                    computer.SysmonPath = sysmonPath;
                    computer.SysmonVersion = sysmonVersion;
                    // Note: ConfigHash and ConfigTag are set during deployment, not during scan
                    computer.LastInventoryScan = DateTime.UtcNow;

                    Interlocked.Increment(ref succeeded);
                    _logger.LogDebug("Scanned {Hostname}: Path={Path}, Version={Version}",
                        computer.Hostname, sysmonPath ?? "(not installed)", sysmonVersion ?? "N/A");
                }
                catch (Exception ex)
                {
                    computer.LastInventoryScan = DateTime.UtcNow;
                    _logger.LogWarning(ex, "Failed to scan {Hostname}", computer.Hostname);
                }

                Interlocked.Increment(ref scanned);
            });

        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Inventory scan completed: {Succeeded}/{Scanned} succeeded", succeeded, scanned);
    }
}
