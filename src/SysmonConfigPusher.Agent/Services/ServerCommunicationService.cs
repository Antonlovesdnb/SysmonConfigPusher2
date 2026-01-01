using System.Net.Http.Json;
using System.Text.Json;
using SysmonConfigPusher.Shared;

namespace SysmonConfigPusher.Agent.Services;

/// <summary>
/// Handles communication with the central server
/// </summary>
public class ServerCommunicationService
{
    private readonly HttpClient _httpClient;
    private readonly ConfigurationService _configService;
    private readonly ILogger<ServerCommunicationService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public ServerCommunicationService(
        HttpClient httpClient,
        ConfigurationService configService,
        ILogger<ServerCommunicationService> logger)
    {
        _httpClient = httpClient;
        _configService = configService;
        _logger = logger;
    }

    /// <summary>
    /// Register with the server
    /// </summary>
    public async Task<AgentRegistrationResponse?> RegisterAsync(CancellationToken cancellationToken)
    {
        var config = _configService.Config;

        var request = new AgentRegistrationRequest
        {
            AgentId = config.AgentId,
            Hostname = Environment.MachineName,
            AgentVersion = typeof(ServerCommunicationService).Assembly.GetName().Version?.ToString() ?? "1.0.0",
            OperatingSystem = Environment.OSVersion.ToString(),
            Is64Bit = Environment.Is64BitOperatingSystem,
            RegistrationToken = config.RegistrationToken,
            Tags = config.Tags
        };

        try
        {
            var url = GetUrl(AgentConstants.Endpoints.Register);
            SetHeaders();

            var response = await _httpClient.PostAsJsonAsync(url, request, JsonOptions, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Registration failed: {Status} - {Error}",
                    response.StatusCode, error);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<AgentRegistrationResponse>(
                JsonOptions, cancellationToken);

            if (result?.Accepted == true && !string.IsNullOrEmpty(result.AuthToken))
            {
                _configService.UpdateAuthToken(result.AuthToken);
                _logger.LogInformation("Successfully registered with server");

                if (result.PollIntervalSeconds > 0)
                {
                    _configService.UpdatePollInterval(result.PollIntervalSeconds);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register with server");
            return null;
        }
    }

    /// <summary>
    /// Send heartbeat and receive pending commands
    /// </summary>
    public async Task<HeartbeatResponse?> SendHeartbeatAsync(
        AgentStatusPayload status,
        CancellationToken cancellationToken)
    {
        var heartbeat = new AgentHeartbeat
        {
            AgentId = _configService.Config.AgentId,
            Status = status
        };

        try
        {
            var url = GetUrl(AgentConstants.Endpoints.Heartbeat);
            SetHeaders();

            var response = await _httpClient.PostAsJsonAsync(url, heartbeat, JsonOptions, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    _logger.LogWarning("Heartbeat unauthorized - need to re-register");
                    return new HeartbeatResponse { Registered = false };
                }

                _logger.LogWarning("Heartbeat failed: {Status}", response.StatusCode);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<HeartbeatResponse>(
                JsonOptions, cancellationToken);

            if (result?.NewPollIntervalSeconds.HasValue == true)
            {
                _configService.UpdatePollInterval(result.NewPollIntervalSeconds.Value);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send heartbeat");
            return null;
        }
    }

    /// <summary>
    /// Send command result back to server
    /// </summary>
    public async Task<bool> SendCommandResultAsync(
        AgentResponse commandResult,
        CancellationToken cancellationToken)
    {
        try
        {
            var url = GetUrl(AgentConstants.Endpoints.CommandResult);
            SetHeaders();

            var response = await _httpClient.PostAsJsonAsync(url, commandResult, JsonOptions, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to send command result: {Status}", response.StatusCode);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send command result");
            return false;
        }
    }

    private string GetUrl(string endpoint)
    {
        var baseUrl = _configService.Config.ServerUrl.TrimEnd('/');
        return $"{baseUrl}{endpoint}";
    }

    private void SetHeaders()
    {
        var config = _configService.Config;

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add(AgentConstants.Headers.AgentId, config.AgentId);
        _httpClient.DefaultRequestHeaders.Add(AgentConstants.Headers.AgentVersion,
            typeof(ServerCommunicationService).Assembly.GetName().Version?.ToString() ?? "1.0.0");

        if (!string.IsNullOrEmpty(config.AuthToken))
        {
            _httpClient.DefaultRequestHeaders.Add(AgentConstants.Headers.AuthToken, config.AuthToken);
        }
    }
}
