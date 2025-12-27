using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace SysmonConfigPusher.Service.Hubs;

[Authorize]
public class DeploymentHub : Hub<IDeploymentHubClient>
{
    public async Task JoinDeploymentGroup(int jobId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"deployment-{jobId}");
    }

    public async Task LeaveDeploymentGroup(int jobId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"deployment-{jobId}");
    }
}

public interface IDeploymentHubClient
{
    Task DeploymentProgress(DeploymentProgressMessage message);
    Task DeploymentCompleted(int jobId, bool success, string? message);
}

public record DeploymentProgressMessage(
    int JobId,
    int ComputerId,
    string Hostname,
    string Status,
    bool Success,
    string? Message,
    int Completed,
    int Total
);
