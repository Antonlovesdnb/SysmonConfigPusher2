using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.EntityFrameworkCore;
using SysmonConfigPusher.Core.Interfaces;
using SysmonConfigPusher.Data;
using SysmonConfigPusher.Infrastructure.ActiveDirectory;
using SysmonConfigPusher.Infrastructure.Wmi;
using SysmonConfigPusher.Infrastructure.Smb;
using SysmonConfigPusher.Service.BackgroundServices;

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

// Deployment queue and worker
builder.Services.AddSingleton<IDeploymentQueue, DeploymentQueue>();
builder.Services.AddHostedService<DeploymentWorker>();

// Inventory scan queue and worker
builder.Services.AddSingleton<IInventoryScanQueue, InventoryScanQueue>();
builder.Services.AddHostedService<InventoryScanWorker>();

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
    // Development mode: use a simple pass-through auth
    builder.Services.AddAuthentication("DevAuth")
        .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, DevAuthHandler>("DevAuth", null);
    builder.Services.AddAuthorization(options =>
    {
        options.DefaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
            .RequireAssertion(_ => true)
            .Build();
    });
}
else
{
    builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme)
        .AddNegotiate();
    builder.Services.AddAuthorization();
}

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

// Development authentication handler that allows all requests
public class DevAuthHandler : Microsoft.AspNetCore.Authentication.AuthenticationHandler<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions>
{
    public DevAuthHandler(
        Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<Microsoft.AspNetCore.Authentication.AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "DevUser") };
        var identity = new System.Security.Claims.ClaimsIdentity(claims, Scheme.Name);
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);
        var ticket = new Microsoft.AspNetCore.Authentication.AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(Microsoft.AspNetCore.Authentication.AuthenticateResult.Success(ticket));
    }
}
