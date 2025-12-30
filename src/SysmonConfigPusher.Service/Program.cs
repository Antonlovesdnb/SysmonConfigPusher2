using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.EntityFrameworkCore;
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

var builder = WebApplication.CreateBuilder(args);

// Configure as Windows Service
builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "SysmonConfigPusher";
});

// Add services
builder.Services.AddControllers();
builder.Services.AddSignalR();

// Register infrastructure services
builder.Services.AddScoped<IActiveDirectoryService, ActiveDirectoryService>();
builder.Services.AddScoped<IRemoteExecutionService, WmiRemoteExecutionService>();
builder.Services.AddScoped<IFileTransferService, SmbFileTransferService>();
builder.Services.AddScoped<IEventLogService, WmiEventLogService>();
builder.Services.AddScoped<INoiseAnalysisService, NoiseAnalysisService>();

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

// Auto-migrate database
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SysmonDbContext>();
    db.Database.Migrate();
}

// Configure pipeline
if (app.Environment.IsDevelopment())
{
    app.UseCors();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

// Serve React SPA from wwwroot
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();
app.MapHealthChecks("/health");
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
