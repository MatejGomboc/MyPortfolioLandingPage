using System.Collections.Concurrent;

namespace MyPortfolioLandingPageBackend.Security;

/// <summary>
/// Custom in-memory rate limiter to prevent abuse
/// </summary>
public class RateLimiterMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimiterMiddleware> _logger;
    private readonly RateLimiterOptions _options;
    private readonly ConcurrentDictionary<string, ClientRequestInfo> _clients = new();
    private readonly Timer _cleanupTimer;

    public RateLimiterMiddleware(
        RequestDelegate next,
        ILogger<RateLimiterMiddleware> logger,
        RateLimiterOptions options)
    {
        _next = next;
        _logger = logger;
        _options = options;
        
        // Cleanup old entries every minute to prevent memory leaks
        _cleanupTimer = new Timer(CleanupOldEntries, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var clientId = GetClientIdentifier(context);
        var now = DateTime.UtcNow;

        // Get or create client info
        var clientInfo = _clients.AddOrUpdate(clientId,
            key => new ClientRequestInfo { FirstRequestTime = now, RequestCount = 1, LastRequestTime = now },
            (key, info) =>
            {
                // Reset if outside the time window
                if (now - info.FirstRequestTime > _options.TimeWindow)
                {
                    info.FirstRequestTime = now;
                    info.RequestCount = 1;
                }
                else
                {
                    info.RequestCount++;
                }
                info.LastRequestTime = now;
                return info;
            });

        // Check if rate limit exceeded
        if (clientInfo.RequestCount > _options.MaxRequests)
        {
            _logger.LogWarning("Rate limit exceeded for client {ClientId} - {RequestCount} requests in window", 
                clientId, clientInfo.RequestCount);
            
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.Append("X-RateLimit-Limit", _options.MaxRequests.ToString());
            context.Response.Headers.Append("X-RateLimit-Remaining", "0");
            context.Response.Headers.Append("X-RateLimit-Reset", 
                (clientInfo.FirstRequestTime + _options.TimeWindow).ToUnixTimeSeconds().ToString());
            
            await context.Response.WriteAsync("Rate limit exceeded. Please try again later.");
            return;
        }

        // Add rate limit headers
        context.Response.Headers.Append("X-RateLimit-Limit", _options.MaxRequests.ToString());
        context.Response.Headers.Append("X-RateLimit-Remaining", 
            (_options.MaxRequests - clientInfo.RequestCount).ToString());

        await _next(context);
    }

    private string GetClientIdentifier(HttpContext context)
    {
        // Use API key if present, otherwise use IP address
        if (context.Request.Headers.TryGetValue("X-API-Key", out var apiKey))
        {
            return $"key:{apiKey}";
        }

        // Get real IP considering proxy headers
        var ip = context.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',').FirstOrDefault()?.Trim()
            ?? context.Request.Headers["X-Real-IP"].FirstOrDefault()
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";

        return $"ip:{ip}";
    }

    private void CleanupOldEntries(object? state)
    {
        var cutoff = DateTime.UtcNow - _options.TimeWindow - TimeSpan.FromMinutes(5);
        var keysToRemove = _clients
            .Where(kvp => kvp.Value.LastRequestTime < cutoff)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in keysToRemove)
        {
            _clients.TryRemove(key, out _);
        }

        if (keysToRemove.Any())
        {
            _logger.LogDebug("Cleaned up {Count} old rate limiter entries", keysToRemove.Count);
        }
    }

    private class ClientRequestInfo
    {
        public DateTime FirstRequestTime { get; set; }
        public DateTime LastRequestTime { get; set; }
        public int RequestCount { get; set; }
    }
}

public class RateLimiterOptions
{
    public int MaxRequests { get; set; } = 100;
    public TimeSpan TimeWindow { get; set; } = TimeSpan.FromMinutes(1);
}

public static class DateTimeExtensions
{
    public static long ToUnixTimeSeconds(this DateTime dateTime)
    {
        return ((DateTimeOffset)dateTime).ToUnixTimeSeconds();
    }
}
