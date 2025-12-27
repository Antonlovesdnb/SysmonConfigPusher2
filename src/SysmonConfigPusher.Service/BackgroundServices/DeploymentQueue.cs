using System.Threading.Channels;
using SysmonConfigPusher.Core.Interfaces;

namespace SysmonConfigPusher.Service.BackgroundServices;

public class DeploymentQueue : IDeploymentQueue
{
    private readonly Channel<int> _queue;

    public DeploymentQueue()
    {
        _queue = Channel.CreateUnbounded<int>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });
    }

    public void Enqueue(int jobId)
    {
        _queue.Writer.TryWrite(jobId);
    }

    public async Task<int?> DequeueAsync(CancellationToken cancellationToken)
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
