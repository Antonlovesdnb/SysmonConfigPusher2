using System.Text.Json;
using SysmonConfigPusher.Shared;

namespace SysmonConfigPusher.Agent.Services;

/// <summary>
/// Executes commands received from the server
/// </summary>
public class CommandExecutor
{
    private readonly SysmonService _sysmonService;
    private readonly EventLogService _eventLogService;
    private readonly ILogger<CommandExecutor> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public CommandExecutor(
        SysmonService sysmonService,
        EventLogService eventLogService,
        ILogger<CommandExecutor> logger)
    {
        _sysmonService = sysmonService;
        _eventLogService = eventLogService;
        _logger = logger;
    }

    /// <summary>
    /// Execute a command and return the response
    /// </summary>
    public async Task<AgentResponse> ExecuteAsync(AgentCommand command)
    {
        _logger.LogInformation("Executing command {CommandId} of type {Type}",
            command.CommandId, command.Type);

        var response = new AgentResponse
        {
            CommandId = command.CommandId
        };

        try
        {
            switch (command.Type)
            {
                case AgentCommandType.GetStatus:
                    response = await ExecuteGetStatus(command);
                    break;

                case AgentCommandType.InstallSysmon:
                    response = await ExecuteInstallSysmon(command);
                    break;

                case AgentCommandType.UpdateConfig:
                    response = await ExecuteUpdateConfig(command);
                    break;

                case AgentCommandType.UninstallSysmon:
                    response = await ExecuteUninstallSysmon(command);
                    break;

                case AgentCommandType.QueryEvents:
                    response = await ExecuteQueryEvents(command);
                    break;

                case AgentCommandType.RestartSysmon:
                    response = await ExecuteRestartSysmon(command);
                    break;

                default:
                    response.Status = CommandResultStatus.Failed;
                    response.Message = $"Unknown command type: {command.Type}";
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing command {CommandId}", command.CommandId);
            response.Status = CommandResultStatus.Failed;
            response.Message = $"Execution error: {ex.Message}";
        }

        return response;
    }

    private Task<AgentResponse> ExecuteGetStatus(AgentCommand command)
    {
        var status = _sysmonService.GetStatus();

        return Task.FromResult(new AgentResponse
        {
            CommandId = command.CommandId,
            Status = CommandResultStatus.Success,
            Message = "Status retrieved",
            Payload = JsonSerializer.Serialize(status, JsonOptions)
        });
    }

    private async Task<AgentResponse> ExecuteInstallSysmon(AgentCommand command)
    {
        if (string.IsNullOrEmpty(command.Payload))
        {
            return new AgentResponse
            {
                CommandId = command.CommandId,
                Status = CommandResultStatus.Failed,
                Message = "Missing payload for InstallSysmon command"
            };
        }

        var payload = JsonSerializer.Deserialize<InstallSysmonPayload>(command.Payload, JsonOptions);
        if (payload == null)
        {
            return new AgentResponse
            {
                CommandId = command.CommandId,
                Status = CommandResultStatus.Failed,
                Message = "Invalid payload for InstallSysmon command"
            };
        }

        var (success, message) = await _sysmonService.InstallAsync(payload);

        return new AgentResponse
        {
            CommandId = command.CommandId,
            Status = success ? CommandResultStatus.Success : CommandResultStatus.Failed,
            Message = message
        };
    }

    private async Task<AgentResponse> ExecuteUpdateConfig(AgentCommand command)
    {
        if (string.IsNullOrEmpty(command.Payload))
        {
            return new AgentResponse
            {
                CommandId = command.CommandId,
                Status = CommandResultStatus.Failed,
                Message = "Missing payload for UpdateConfig command"
            };
        }

        var payload = JsonSerializer.Deserialize<UpdateConfigPayload>(command.Payload, JsonOptions);
        if (payload == null)
        {
            return new AgentResponse
            {
                CommandId = command.CommandId,
                Status = CommandResultStatus.Failed,
                Message = "Invalid payload for UpdateConfig command"
            };
        }

        var (success, message) = await _sysmonService.UpdateConfigAsync(payload);

        return new AgentResponse
        {
            CommandId = command.CommandId,
            Status = success ? CommandResultStatus.Success : CommandResultStatus.Failed,
            Message = message
        };
    }

    private async Task<AgentResponse> ExecuteUninstallSysmon(AgentCommand command)
    {
        var (success, message) = await _sysmonService.UninstallAsync();

        return new AgentResponse
        {
            CommandId = command.CommandId,
            Status = success ? CommandResultStatus.Success : CommandResultStatus.Failed,
            Message = message
        };
    }

    private Task<AgentResponse> ExecuteQueryEvents(AgentCommand command)
    {
        var query = new QueryEventsPayload();

        if (!string.IsNullOrEmpty(command.Payload))
        {
            query = JsonSerializer.Deserialize<QueryEventsPayload>(command.Payload, JsonOptions)
                ?? new QueryEventsPayload();
        }

        var result = _eventLogService.QueryEvents(query);

        return Task.FromResult(new AgentResponse
        {
            CommandId = command.CommandId,
            Status = CommandResultStatus.Success,
            Message = $"Retrieved {result.ReturnedCount} events",
            Payload = JsonSerializer.Serialize(result, JsonOptions)
        });
    }

    private async Task<AgentResponse> ExecuteRestartSysmon(AgentCommand command)
    {
        var (success, message) = await _sysmonService.RestartAsync();

        return new AgentResponse
        {
            CommandId = command.CommandId,
            Status = success ? CommandResultStatus.Success : CommandResultStatus.Failed,
            Message = message
        };
    }
}
