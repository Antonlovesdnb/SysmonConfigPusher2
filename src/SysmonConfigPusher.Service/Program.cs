using System.Reflection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using SysmonConfigPusher.Core.Configuration;
using SysmonConfigPusher.Core.Interfaces;
using SysmonConfigPusher.Data;
using SysmonConfigPusher.Infrastructure.ActiveDirectory;
using SysmonConfigPusher.Infrastructure.Wmi;
using SysmonConfigPusher.Infrastructure.Smb;
using SysmonConfigPusher.Infrastructure.EventLog;
using SysmonConfigPusher.Infrastructure.NoiseAnalysis;
using SysmonConfigPusher.Infrastructure.BinaryCache;
using SysmonConfigPusher.Service.Authorization;
using SysmonConfigPusher.Service.BackgroundServices;
using SysmonConfigPusher.Service.Services;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

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

// Register infrastructure services
builder.Services.AddScoped<IActiveDirectoryService, ActiveDirectoryService>();
builder.Services.AddScoped<IRemoteExecutionService, WmiRemoteExecutionService>();
builder.Services.AddScoped<IFileTransferService, SmbFileTransferService>();
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
var dataPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "SysmonConfigPusher");
Directory.CreateDirectory(dataPath);
var dbPath = Path.Combine(dataPath, "sysmon.db");

builder.Services.AddDbContext<SysmonDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

// Windows Authentication (can be disabled for local development testing)
var disableAuth = builder.Configuration.GetValue<bool>("DisableAuth");
if (disableAuth)
{
    // Development mode: use a simple pass-through auth with all roles
    builder.Services.AddAuthentication("DevAuth")
        .AddScheme<AuthenticationSchemeOptions, DevAuthHandler>("DevAuth", null);
}
else
{
    // Production mode: Windows Integrated Authentication
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
if (app.Environment.IsDevelopment())
{
    app.UseCors();
}

// Swagger UI (available in all environments for ops visibility)
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "SysmonConfigPusher API v1");
    options.RoutePrefix = "swagger";
});

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
public partial class Program { }
