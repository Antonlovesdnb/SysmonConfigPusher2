using System.Reflection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Serilog;
using SysmonConfigPusher.Core.Configuration;
using SysmonConfigPusher.Core.Interfaces;
using SysmonConfigPusher.Data;
using SysmonConfigPusher.Infrastructure.ActiveDirectory;
using SysmonConfigPusher.Infrastructure.Wmi;
using SysmonConfigPusher.Infrastructure.Smb;
using SysmonConfigPusher.Infrastructure.EventLog;
using SysmonConfigPusher.Infrastructure.NoiseAnalysis;
using SysmonConfigPusher.Infrastructure.BinaryCache;
using SysmonConfigPusher.Service;
using SysmonConfigPusher.Service.Authentication;
using ApiKeyConfig = SysmonConfigPusher.Service.Authentication.ApiKeyConfig;
using SysmonConfigPusher.Service.Authorization;
using SysmonConfigPusher.Service.BackgroundServices;
using SysmonConfigPusher.Service.Middleware;
using SysmonConfigPusher.Service.Services;
using System.Text.Json;

// Ensure self-signed certificate exists for HTTPS (Windows only)
if (OperatingSystem.IsWindows())
{
    CertificateHelper.EnsureCertificateExists();
}

// Configure Serilog - read log directory from configuration
var tempConfig = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
    .Build();

var logDirectory = tempConfig["SysmonConfigPusher:LogDirectory"];
if (string.IsNullOrEmpty(logDirectory))
{
    if (OperatingSystem.IsWindows())
    {
        logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SysmonConfigPusher", "logs");
    }
    else
    {
        // On Linux/Docker, use /data/logs (matches Docker volume mount)
        logDirectory = "/data/logs";
    }
}
Directory.CreateDirectory(logDirectory);
var logPath = Path.Combine(logDirectory, "sysmonpusher-.log");

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", Serilog.Events.LogEventLevel.Information)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(logPath,
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        fileSizeLimitBytes: 50 * 1024 * 1024, // 50MB per file
        rollOnFileSizeLimit: true,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("Starting SysmonConfigPusher service");

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

// Map friendly Docker environment variables to configuration
// This allows users to use simple env vars like API_KEY_ADMIN instead of
// Authentication__ApiKeys__0__Key
ConfigureFromFriendlyEnvVars(builder.Configuration);

// Configure as Windows Service
builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "SysmonConfigPusher";
});

// Add services
builder.Services.AddControllers();
builder.Services.AddSignalR();

// Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "SysmonConfigPusher API",
        Version = "v1",
        Description = "API for managing Sysmon configurations across Windows endpoints",
        Contact = new OpenApiContact
        {
            Name = "SysmonConfigPusher",
            Url = new Uri("https://github.com/Antonlovesdnb/SysmonConfigPusher2")
        }
    });

    // Include XML comments for better documentation
    var xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFilename);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }
});

// Determine server mode (Full = all features, AgentOnly = no WMI/SMB/AD)
var serverMode = builder.Configuration["ServerMode"] ?? "Full";
var isAgentOnlyMode = string.Equals(serverMode, "AgentOnly", StringComparison.OrdinalIgnoreCase) ||
                      !OperatingSystem.IsWindows();

if (isAgentOnlyMode)
{
    Log.Information("Running in AgentOnly mode - WMI/SMB/AD features disabled");
    // AgentOnly mode: use null implementations
    builder.Services.AddScoped<IActiveDirectoryService, NullActiveDirectoryService>();
    builder.Services.AddScoped<IRemoteExecutionService, AgentOnlyRemoteExecutionService>();
    builder.Services.AddScoped<IFileTransferService, AgentOnlyFileTransferService>();
}
else
{
    Log.Information("Running in Full mode - all features enabled");
    // Full mode: use Windows implementations
    builder.Services.AddScoped<IActiveDirectoryService, ActiveDirectoryService>();
    builder.Services.AddScoped<IRemoteExecutionService, WmiRemoteExecutionService>();
    builder.Services.AddScoped<IFileTransferService, SmbFileTransferService>();
}

// These services work in both modes (EventLog requires WMI but degrades gracefully)
builder.Services.AddScoped<IEventLogService, WmiEventLogService>();
builder.Services.AddScoped<INoiseAnalysisService, NoiseAnalysisService>();
builder.Services.AddScoped<IConfigValidationService, SysmonConfigPusher.Infrastructure.ConfigValidation.ConfigValidationService>();

// Deployment queue and worker
builder.Services.AddSingleton<IDeploymentQueue, DeploymentQueue>();
builder.Services.AddHostedService<DeploymentWorker>();

// Inventory scan queue and worker
builder.Services.AddSingleton<IInventoryScanQueue, InventoryScanQueue>();
builder.Services.AddHostedService<InventoryScanWorker>();

// Scheduled deployment worker
builder.Services.AddHostedService<ScheduledDeploymentWorker>();

// Audit service
builder.Services.AddScoped<IAuditService, AuditService>();

// Authorization settings
builder.Services.Configure<AuthorizationSettings>(
    builder.Configuration.GetSection(AuthorizationSettings.SectionName));

// SysmonConfigPusher settings
builder.Services.Configure<SysmonConfigPusherSettings>(
    builder.Configuration.GetSection(SysmonConfigPusherSettings.SectionName));

// Sysmon binary cache service
builder.Services.AddHttpClient<ISysmonBinaryCacheService, SysmonBinaryCacheService>();

// HttpClient for config import from URL
builder.Services.AddHttpClient("ConfigImport", client =>
{
    client.DefaultRequestHeaders.Add("User-Agent", "SysmonConfigPusher/2.0");
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Configure SQLite
string dataPath;
if (OperatingSystem.IsWindows())
{
    dataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "SysmonConfigPusher");
}
else
{
    // On Linux/Docker, use /data (matches Docker volume mount)
    dataPath = "/data";
}
Directory.CreateDirectory(dataPath);
var dbPath = Path.Combine(dataPath, "sysmon.db");

builder.Services.AddDbContext<SysmonDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

// Authentication configuration
// Modes: Windows (default on Windows), ApiKey (for Docker/non-domain), DevAuth (development only)
var authMode = builder.Configuration["Authentication:Mode"] ??
               (OperatingSystem.IsWindows() ? "Windows" : "ApiKey");
var disableAuth = builder.Configuration.GetValue<bool>("DisableAuth");

if (disableAuth && builder.Environment.IsDevelopment())
{
    // Development mode ONLY: use a simple pass-through auth with all roles
    // This is intentionally restricted to Development environment for security
    Log.Information("Using DevAuth (development mode - all requests get Admin access)");
    builder.Services.AddAuthentication("DevAuth")
        .AddScheme<AuthenticationSchemeOptions, DevAuthHandler>("DevAuth", null);
}
else if (string.Equals(authMode, "ApiKey", StringComparison.OrdinalIgnoreCase))
{
    // API Key authentication for Docker/non-domain environments
    Log.Information("Using API Key authentication");

    // Load API keys from configuration
    var apiKeySection = builder.Configuration.GetSection("Authentication:ApiKeys");
    var keys = apiKeySection.Get<List<ApiKeyConfig>>() ?? new List<ApiKeyConfig>();
    var headerName = builder.Configuration["Authentication:ApiKeyHeader"] ?? "X-Api-Key";

    Log.Information("Loaded {Count} API keys for authentication", keys.Count);

    builder.Services.AddAuthentication(ApiKeyAuthOptions.DefaultScheme)
        .AddScheme<ApiKeyAuthOptions, ApiKeyAuthHandler>(ApiKeyAuthOptions.DefaultScheme, options =>
        {
            options.HeaderName = headerName;
            options.Keys = keys;
        });
}
else
{
    // Windows Integrated Authentication (default for domain-joined Windows servers)
    Log.Information("Using Windows Integrated Authentication");
    builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme)
        .AddNegotiate();

    // Register claims transformation for AD group to role mapping
    builder.Services.AddScoped<IClaimsTransformation, AdGroupClaimsTransformation>();
}

// Authorization policies
builder.Services.AddAuthorization(options =>
{
    // Viewer: read-only access (any authenticated user with a role)
    options.AddPolicy("RequireViewer", policy =>
        policy.RequireRole("Admin", "Operator", "Viewer"));

    // Operator: can deploy, manage configs, run analysis
    options.AddPolicy("RequireOperator", policy =>
        policy.RequireRole("Admin", "Operator"));

    // Admin: full access
    options.AddPolicy("RequireAdmin", policy =>
        policy.RequireRole("Admin"));
});

// Health checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<SysmonDbContext>("database");

// CORS for development
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://localhost:5174", "http://localhost:5175") // Vite dev server
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

// Auto-migrate database (skip for in-memory databases used in testing)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SysmonDbContext>();
    if (db.Database.IsRelational())
    {
        db.Database.Migrate();
    }
    else
    {
        db.Database.EnsureCreated();
    }
}

// Configure pipeline
app.UseGlobalExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseCors();
}

// Swagger UI (development only for security)
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "SysmonConfigPusher API v1");
        options.RoutePrefix = "swagger";
    });
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

// Serve React SPA from wwwroot
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();

// Health check endpoint with detailed JSON response
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var response = new
        {
            status = report.Status.ToString(),
            totalDuration = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                duration = e.Value.Duration.TotalMilliseconds,
                description = e.Value.Description,
                exception = e.Value.Exception?.Message
            })
        };
        await context.Response.WriteAsync(JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        }));
    }
});

// Simple liveness probe (no dependency checks)
app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }));

app.MapHub<SysmonConfigPusher.Service.Hubs.DeploymentHub>("/hubs/deployment");

// Fallback to index.html for SPA routing
app.MapFallbackToFile("index.html");

app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// Development authentication handler that allows all requests with full Admin access
public class DevAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public DevAuthHandler(
        Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Dev user gets all roles for full access during development
        var claims = new[]
        {
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "DevUser"),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "Admin"),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "Operator"),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "Viewer")
        };
        var identity = new System.Security.Claims.ClaimsIdentity(claims, Scheme.Name);
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

// Partial class to make Program visible for integration tests
public partial class Program
{
    /// <summary>
    /// Maps friendly Docker environment variables to ASP.NET Core configuration.
    /// This allows users to use simple env vars instead of the verbose nested syntax.
    /// </summary>
    /// <remarks>
    /// Supported environment variables:
    /// - API_KEY_ADMIN: API key for Admin role
    /// - API_KEY_OPERATOR: API key for Operator role
    /// - API_KEY_VIEWER: API key for Viewer role
    /// - AGENT_TOKEN: Registration token for agents
    /// </remarks>
    private static void ConfigureFromFriendlyEnvVars(IConfigurationManager configuration)
    {
        var apiKeys = new List<Dictionary<string, string>>();
        var keyIndex = 0;

        // Check for friendly API key environment variables
        var adminKey = Environment.GetEnvironmentVariable("API_KEY_ADMIN");
        if (!string.IsNullOrEmpty(adminKey))
        {
            configuration[$"Authentication:ApiKeys:{keyIndex}:Key"] = adminKey;
            configuration[$"Authentication:ApiKeys:{keyIndex}:Name"] = "Admin";
            configuration[$"Authentication:ApiKeys:{keyIndex}:Role"] = "Admin";
            keyIndex++;
            Log.Information("Configured Admin API key from API_KEY_ADMIN environment variable");
        }

        var operatorKey = Environment.GetEnvironmentVariable("API_KEY_OPERATOR");
        if (!string.IsNullOrEmpty(operatorKey))
        {
            configuration[$"Authentication:ApiKeys:{keyIndex}:Key"] = operatorKey;
            configuration[$"Authentication:ApiKeys:{keyIndex}:Name"] = "Operator";
            configuration[$"Authentication:ApiKeys:{keyIndex}:Role"] = "Operator";
            keyIndex++;
            Log.Information("Configured Operator API key from API_KEY_OPERATOR environment variable");
        }

        var viewerKey = Environment.GetEnvironmentVariable("API_KEY_VIEWER");
        if (!string.IsNullOrEmpty(viewerKey))
        {
            configuration[$"Authentication:ApiKeys:{keyIndex}:Key"] = viewerKey;
            configuration[$"Authentication:ApiKeys:{keyIndex}:Name"] = "Viewer";
            configuration[$"Authentication:ApiKeys:{keyIndex}:Role"] = "Viewer";
            keyIndex++;
            Log.Information("Configured Viewer API key from API_KEY_VIEWER environment variable");
        }

        // Check for friendly agent token environment variable
        var agentToken = Environment.GetEnvironmentVariable("AGENT_TOKEN");
        if (!string.IsNullOrEmpty(agentToken))
        {
            configuration["Agent:RegistrationToken"] = agentToken;
            Log.Information("Configured agent registration token from AGENT_TOKEN environment variable");
        }
    }
}
