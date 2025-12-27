namespace SysmonConfigPusher.Core.Interfaces;

public interface IDeploymentQueue
{
    void Enqueue(int jobId);
    Task<int?> DequeueAsync(CancellationToken cancellationToken);
}
