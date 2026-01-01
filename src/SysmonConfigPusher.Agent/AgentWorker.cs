using SysmonConfigPusher.Agent.Services;

namespace SysmonConfigPusher.Agent;

/// <summary>
/// Main background worker that handles agent lifecycle
/// </summary>
public class AgentWorker : BackgroundService
{
    private readonly ConfigurationService _configService;
    private readonly ServerCommunicationService _serverService;
    private readonly SysmonService _sysmonService;
    private readonly CommandExecutor _commandExecutor;
    private readonly ILogger<AgentWorker> _logger;

    public AgentWorker(
        ConfigurationService configService,
        ServerCommunicationService serverService,
        SysmonService sysmonService,
        CommandExecutor commandExecutor,
        ILogger<AgentWorker> logger)
    {
        _configService = configService;
        _serverService = serverService;
        _sysmonService = sysmonService;
        _commandExecutor = commandExecutor;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SysmonConfigPusher Agent starting...");

        // Load configuration
        _configService.Load();

        // Validate configuration
        if (string.IsNullOrEmpty(_configService.Config.ServerUrl))
        {
            _logger.LogError("ServerUrl not configured in agent.json - agent cannot start");
            return;
        }

        // Registration loop
        while (!_configService.Config.IsRegistered && !stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Attempting to register with server...");

            var response = await _serverService.RegisterAsync(stoppingToken);

            if (response?.Accepted == true)
            {
                _logger.LogInformation("Registration successful");
                break;
            }

            _logger.LogWarning("Registration failed: {Message}. Retrying in 30 seconds...",
                response?.Message ?? "No response");

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }

        if (stoppingToken.IsCancellationRequested)
            return;

        _logger.LogInformation("Agent registered. Starting main loop with {Interval}s poll interval",
            _configService.Config.PollIntervalSeconds);

        // Main heartbeat loop
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessHeartbeatCycle(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in heartbeat cycle");
            }

            await Task.Delay(
                TimeSpan.FromSeconds(_configService.Config.PollIntervalSeconds),
                stoppingToken);
        }

        _logger.LogInformation("Agent stopping...");
    }

    private async Task ProcessHeartbeatCycle(CancellationToken stoppingToken)
    {
        // Get current status
        var status = _sysmonService.GetStatus();

        // Send heartbeat
        var response = await _serverService.SendHeartbeatAsync(status, stoppingToken);

        if (response == null)
        {
            _logger.LogWarning("No response from server");
            return;
        }

        if (!response.Registered)
        {
            _logger.LogWarning("Agent no longer registered - attempting re-registration");
            _configService.Config.IsRegistered = false;

            var regResponse = await _serverService.RegisterAsync(stoppingToken);
            if (regResponse?.Accepted != true)
            {
                _logger.LogError("Re-registration failed");
                return;
            }
        }

        // Process any pending commands
        if (response.PendingCommands.Count > 0)
        {
            _logger.LogInformation("Received {Count} pending commands", response.PendingCommands.Count);

            foreach (var command in response.PendingCommands)
            {
                var result = await _commandExecutor.ExecuteAsync(command);

                // Send result back to server
                await _serverService.SendCommandResultAsync(result, stoppingToken);
            }
        }
    }
}
