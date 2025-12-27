namespace SysmonConfigPusher.Core.Interfaces;

public interface IInventoryScanQueue
{
    /// <summary>
    /// Enqueue specific computer IDs for scanning.
    /// </summary>
    void Enqueue(IEnumerable<int> computerIds);

    /// <summary>
    /// Enqueue all computers for scanning.
    /// </summary>
    void EnqueueAll();

    /// <summary>
    /// Dequeue the next batch of computer IDs to scan.
    /// Returns null if the queue is empty.
    /// </summary>
    Task<InventoryScanRequest?> DequeueAsync(CancellationToken cancellationToken);
}

public record InventoryScanRequest(bool ScanAll, IReadOnlyList<int>? ComputerIds);
