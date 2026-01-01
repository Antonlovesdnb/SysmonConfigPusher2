using System.Text.Json;
using SysmonConfigPusher.Agent.Configuration;
using SysmonConfigPusher.Shared;

namespace SysmonConfigPusher.Agent.Services;

/// <summary>
/// Manages agent configuration persistence
/// </summary>
public class ConfigurationService
{
    private readonly string _configPath;
    private readonly ILogger<ConfigurationService> _logger;
    private readonly object _lock = new();
    private AgentConfig _config = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ConfigurationService(ILogger<ConfigurationService> logger)
    {
        _logger = logger;
        var basePath = Path.GetDirectoryName(Environment.ProcessPath)
            ?? AgentConstants.DefaultInstallPath;
        _configPath = Path.Combine(basePath, AgentConstants.ConfigFileName);
    }

    public AgentConfig Config => _config;

    public void Load()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    var json = File.ReadAllText(_configPath);
                    _config = JsonSerializer.Deserialize<AgentConfig>(json, JsonOptions) ?? new();
                    _logger.LogInformation("Loaded configuration from {Path}", _configPath);
                }
                else
                {
                    _logger.LogWarning("Configuration file not found at {Path}, using defaults", _configPath);
                    _config = new AgentConfig();
                }

                // Generate agent ID if not set
                if (string.IsNullOrEmpty(_config.AgentId))
                {
                    _config.AgentId = Guid.NewGuid().ToString("N");
                    Save();
                    _logger.LogInformation("Generated new agent ID: {AgentId}", _config.AgentId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load configuration from {Path}", _configPath);
                _config = new AgentConfig();
            }
        }
    }

    public void Save()
    {
        lock (_lock)
        {
            try
            {
                var directory = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(_config, JsonOptions);
                File.WriteAllText(_configPath, json);
                _logger.LogDebug("Saved configuration to {Path}", _configPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save configuration to {Path}", _configPath);
            }
        }
    }

    public void UpdateAuthToken(string token)
    {
        lock (_lock)
        {
            _config.AuthToken = token;
            _config.IsRegistered = true;
            Save();
        }
    }

    public void UpdatePollInterval(int seconds)
    {
        lock (_lock)
        {
            _config.PollIntervalSeconds = Math.Clamp(
                seconds,
                AgentConstants.MinPollIntervalSeconds,
                AgentConstants.MaxPollIntervalSeconds);
            Save();
        }
    }
}
