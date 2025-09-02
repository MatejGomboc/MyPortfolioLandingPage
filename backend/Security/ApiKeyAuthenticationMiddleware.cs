using System.Security.Cryptography;
using System.Text;

namespace MyPortfolioLandingPageBackend.Security;

/// <summary>
/// Simple but secure API key authentication middleware
/// </summary>
public class ApiKeyAuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyAuthenticationMiddleware> _logger;
    private readonly ApiKeyOptions _options;
    private readonly HashSet<string> _hashedApiKeys;

    public ApiKeyAuthenticationMiddleware(
        RequestDelegate next,
        ILogger<ApiKeyAuthenticationMiddleware> logger,
        ApiKeyOptions options)
    {
        _next = next;
        _logger = logger;
        _options = options;
        
        // Pre-hash API keys for constant-time comparison
        _hashedApiKeys = new HashSet<string>(
            options.ApiKeys.Select(key => HashApiKey(key))
        );
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip authentication for excluded paths
        if (_options.ExcludedPaths.Any(path => 
            context.Request.Path.StartsWithSegments(path, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        // Check for API key in headers
        if (!context.Request.Headers.TryGetValue(_options.HeaderName, out var providedApiKey))
        {
            _logger.LogWarning("Request rejected - No API key provided from {IP}", 
                GetClientIp(context));
            
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.Append("WWW-Authenticate", "ApiKey");
            await context.Response.WriteAsync("API key is required");
            return;
        }

        // Validate API key using constant-time comparison
        var hashedProvidedKey = HashApiKey(providedApiKey.ToString());
        
        if (!IsValidApiKey(hashedProvidedKey))
        {
            _logger.LogWarning("Request rejected - Invalid API key from {IP}", 
                GetClientIp(context));
            
            // Add delay to prevent brute force attacks
            await Task.Delay(_options.FailureDelay);
            
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Invalid API key");
            return;
        }

        // Add authenticated marker to context
        context.Items["Authenticated"] = true;
        context.Items["AuthMethod"] = "ApiKey";
        
        await _next(context);
    }

    private bool IsValidApiKey(string hashedKey)
    {
        // Use constant-time comparison to prevent timing attacks
        return _hashedApiKeys.Any(validKey => 
            CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(validKey),
                Encoding.UTF8.GetBytes(hashedKey)
            ));
    }

    private static string HashApiKey(string apiKey)
    {
        // Use SHA256 to hash API keys for storage and comparison
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(apiKey));
        return Convert.ToBase64String(hashBytes);
    }

    private static string GetClientIp(HttpContext context)
    {
        return context.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',').FirstOrDefault()?.Trim()
            ?? context.Request.Headers["X-Real-IP"].FirstOrDefault()
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";
    }
}

public class ApiKeyOptions
{
    public string HeaderName { get; set; } = "X-API-Key";
    public List<string> ApiKeys { get; set; } = new();
    public List<string> ExcludedPaths { get; set; } = new() { "/health" };
    public TimeSpan FailureDelay { get; set; } = TimeSpan.FromSeconds(2);
}
