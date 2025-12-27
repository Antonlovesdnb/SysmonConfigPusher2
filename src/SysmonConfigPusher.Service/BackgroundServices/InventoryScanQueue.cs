using System.Threading.Channels;
using SysmonConfigPusher.Core.Interfaces;

namespace SysmonConfigPusher.Service.BackgroundServices;

public class InventoryScanQueue : IInventoryScanQueue
{
    private readonly Channel<InventoryScanRequest> _queue;

    public InventoryScanQueue()
    {
        _queue = Channel.CreateUnbounded<InventoryScanRequest>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    }

    public void Enqueue(IEnumerable<int> computerIds)
    {
        _queue.Writer.TryWrite(new InventoryScanRequest(false, computerIds.ToList()));
    }

    public void EnqueueAll()
    {
        _queue.Writer.TryWrite(new InventoryScanRequest(true, null));
    }

    public async Task<InventoryScanRequest?> DequeueAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _queue.Reader.ReadAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }
}
