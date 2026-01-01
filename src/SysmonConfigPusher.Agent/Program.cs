using SysmonConfigPusher.Agent;
using SysmonConfigPusher.Agent.Services;

var builder = Host.CreateApplicationBuilder(args);

// Configure as Windows Service
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "SysmonConfigPusherAgent";
});

// Register services
builder.Services.AddSingleton<ConfigurationService>();
builder.Services.AddSingleton<SysmonService>();
builder.Services.AddSingleton<EventLogService>();
builder.Services.AddSingleton<CommandExecutor>();

// Configure HTTP client for server communication
builder.Services.AddHttpClient<ServerCommunicationService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
})
.ConfigurePrimaryHttpMessageHandler(sp =>
{
    var configService = sp.GetRequiredService<ConfigurationService>();
    configService.Load();

    var handler = new HttpClientHandler();

    // Configure certificate validation
    if (!configService.Config.ValidateServerCertificate)
    {
        handler.ServerCertificateCustomValidationCallback =
            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
    }
    else if (!string.IsNullOrEmpty(configService.Config.CertificateThumbprint))
    {
        handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
        {
            if (cert == null) return false;
            return cert.GetCertHashString().Equals(
                configService.Config.CertificateThumbprint,
                StringComparison.OrdinalIgnoreCase);
        };
    }

    return handler;
});

// Add the main worker
builder.Services.AddHostedService<AgentWorker>();

// Configure logging
builder.Logging.AddEventLog(settings =>
{
    settings.SourceName = "SysmonConfigPusherAgent";
    settings.LogName = "Application";
});

var host = builder.Build();
host.Run();
