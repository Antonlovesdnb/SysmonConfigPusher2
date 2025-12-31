using System.Net;
using System.Text.Json;

namespace SysmonConfigPusher.Service.Middleware;

/// <summary>
/// Global exception handling middleware that catches unhandled exceptions
/// and returns consistent error responses.
/// </summary>
public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;
    private readonly IWebHostEnvironment _environment;

    public GlobalExceptionMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionMiddleware> logger,
        IWebHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        _logger.LogError(exception, "Unhandled exception occurred while processing request {Method} {Path}",
            context.Request.Method, context.Request.Path);

        var (statusCode, errorType) = exception switch
        {
            UnauthorizedAccessException => (HttpStatusCode.Forbidden, "AccessDenied"),
            ArgumentException => (HttpStatusCode.BadRequest, "InvalidArgument"),
            KeyNotFoundException => (HttpStatusCode.NotFound, "NotFound"),
            InvalidOperationException => (HttpStatusCode.BadRequest, "InvalidOperation"),
            TimeoutException => (HttpStatusCode.GatewayTimeout, "Timeout"),
            OperationCanceledException => (HttpStatusCode.BadRequest, "OperationCancelled"),
            _ => (HttpStatusCode.InternalServerError, "InternalError")
        };

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        var response = new ErrorResponse
        {
            Type = errorType,
            Message = GetUserFriendlyMessage(exception, statusCode),
            TraceId = context.TraceIdentifier
        };

        // Only include details in development
        if (_environment.IsDevelopment())
        {
            response.Details = exception.ToString();
        }

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(response, options));
    }

    private static string GetUserFriendlyMessage(Exception exception, HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            HttpStatusCode.Forbidden => "Access denied. You do not have permission to perform this action.",
            HttpStatusCode.NotFound => "The requested resource was not found.",
            HttpStatusCode.BadRequest => exception.Message,
            HttpStatusCode.GatewayTimeout => "The operation timed out. Please try again.",
            HttpStatusCode.InternalServerError => "An unexpected error occurred. Please try again or contact support.",
            _ => "An error occurred while processing your request."
        };
    }

    private class ErrorResponse
    {
        public string Type { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string TraceId { get; set; } = string.Empty;
        public string? Details { get; set; }
    }
}

/// <summary>
/// Extension methods for registering the global exception middleware.
/// </summary>
public static class GlobalExceptionMiddlewareExtensions
{
    public static IApplicationBuilder UseGlobalExceptionHandler(this IApplicationBuilder app)
    {
        return app.UseMiddleware<GlobalExceptionMiddleware>();
    }
}
