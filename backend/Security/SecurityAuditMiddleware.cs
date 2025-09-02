using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace MyPortfolioLandingPageBackend.Security;

/// <summary>
/// Comprehensive request/response logging for security auditing
/// </summary>
public class SecurityAuditMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SecurityAuditMiddleware> _logger;
    private readonly SecurityAuditOptions _options;

    public SecurityAuditMiddleware(
        RequestDelegate next,
        ILogger<SecurityAuditMiddleware> logger,
        SecurityAuditOptions options)
    {
        _next = next;
        _logger = logger;
        _options = options;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Generate unique request ID
        var requestId = Guid.NewGuid().ToString("N");
        context.Items["RequestId"] = requestId;
        context.Response.Headers.Append("X-Request-Id", requestId);

        var stopwatch = Stopwatch.StartNew();
        var auditLog = new SecurityAuditLog
        {
            RequestId = requestId,
            Timestamp = DateTime.UtcNow,
            Method = context.Request.Method,
            Path = context.Request.Path,
            QueryString = context.Request.QueryString.ToString(),
            ClientIp = GetClientIp(context),
            UserAgent = context.Request.Headers.UserAgent.ToString(),
            Authenticated = context.Items.ContainsKey("Authenticated")
        };

        // Log request headers if configured
        if (_options.LogRequestHeaders)
        {
            auditLog.RequestHeaders = GetSafeHeaders(context.Request.Headers);
        }

        // Capture request body if needed
        if (_options.LogRequestBody && IsTextContent(context.Request.ContentType))
        {
            context.Request.EnableBuffering();
            auditLog.RequestBody = await ReadRequestBody(context.Request);
        }

        try
        {
            await _next(context);
            
            auditLog.StatusCode = context.Response.StatusCode;
            auditLog.Success = context.Response.StatusCode < 400;
        }
        catch (Exception ex)
        {
            auditLog.Success = false;
            auditLog.Error = ex.Message;
            auditLog.StatusCode = 500;
            
            _logger.LogError(ex, "Unhandled exception for request {RequestId}", requestId);
            
            // Don't leak internal errors to client
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsync("An error occurred processing your request.");
        }
        finally
        {
            stopwatch.Stop();
            auditLog.DurationMs = stopwatch.ElapsedMilliseconds;
            
            // Log based on outcome
            if (auditLog.Success)
            {
                if (_options.LogSuccessfulRequests)
                {
                    _logger.LogInformation("Security Audit: {AuditLog}", 
                        JsonSerializer.Serialize(auditLog, _jsonOptions));
                }
            }
            else
            {
                _logger.LogWarning("Security Alert: Failed request {AuditLog}", 
                    JsonSerializer.Serialize(auditLog, _jsonOptions));
            }

            // Track suspicious patterns
            DetectSuspiciousActivity(context, auditLog);
        }
    }

    private void DetectSuspiciousActivity(HttpContext context, SecurityAuditLog auditLog)
    {
        var suspicious = new List<string>();

        // Check for SQL injection patterns
        var allParams = $"{auditLog.Path}{auditLog.QueryString}{auditLog.RequestBody}".ToLower();
        if (ContainsSqlInjectionPattern(allParams))
        {
            suspicious.Add("Possible SQL injection attempt");
        }

        // Check for path traversal
        if (allParams.Contains("../") || allParams.Contains("..\\"))
        {
            suspicious.Add("Possible path traversal attempt");
        }

        // Check for script injection
        if (ContainsScriptPattern(allParams))
        {
            suspicious.Add("Possible script injection attempt");
        }

        // Check for unusually long inputs
        if (auditLog.RequestBody?.Length > _options.MaxRequestBodySize)
        {
            suspicious.Add($"Unusually large request body: {auditLog.RequestBody.Length} bytes");
        }

        // Log suspicious activity
        if (suspicious.Any())
        {
            _logger.LogWarning("SECURITY ALERT - Suspicious activity detected: {Patterns} for request {RequestId} from {IP}",
                string.Join(", ", suspicious), auditLog.RequestId, auditLog.ClientIp);
        }
    }

    private bool ContainsSqlInjectionPattern(string input)
    {
        var patterns = new[] { "' or ", "1=1", "drop table", "union select", "exec(", "execute(", "script>" };
        return patterns.Any(pattern => input.Contains(pattern));
    }

    private bool ContainsScriptPattern(string input)
    {
        var patterns = new[] { "<script", "javascript:", "onerror=", "onclick=", "alert(" };
        return patterns.Any(pattern => input.Contains(pattern));
    }

    private Dictionary<string, string> GetSafeHeaders(IHeaderDictionary headers)
    {
        var safeHeaders = new Dictionary<string, string>();
        foreach (var header in headers)
        {
            // Don't log sensitive headers
            if (!_options.SensitiveHeaders.Contains(header.Key.ToLower()))
            {
                safeHeaders[header.Key] = header.Value.ToString();
            }
        }
        return safeHeaders;
    }

    private async Task<string> ReadRequestBody(HttpRequest request)
    {
        request.Body.Position = 0;
        using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        request.Body.Position = 0;
        
        // Truncate if too long
        if (body.Length > _options.MaxRequestBodySize)
        {
            body = body.Substring(0, _options.MaxRequestBodySize) + "...[truncated]";
        }
        
        return body;
    }

    private bool IsTextContent(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType)) return false;
        
        var textTypes = new[] { "text/", "application/json", "application/xml" };
        return textTypes.Any(type => contentType.Contains(type, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetClientIp(HttpContext context)
    {
        return context.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',').FirstOrDefault()?.Trim()
            ?? context.Request.Headers["X-Real-IP"].FirstOrDefault()
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";
    }

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}

public class SecurityAuditLog
{
    public string RequestId { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public string Method { get; set; } = "";
    public string Path { get; set; } = "";
    public string QueryString { get; set; } = "";
    public string ClientIp { get; set; } = "";
    public string UserAgent { get; set; } = "";
    public bool Authenticated { get; set; }
    public Dictionary<string, string>? RequestHeaders { get; set; }
    public string? RequestBody { get; set; }
    public int StatusCode { get; set; }
    public bool Success { get; set; }
    public long DurationMs { get; set; }
    public string? Error { get; set; }
}

public class SecurityAuditOptions
{
    public bool LogSuccessfulRequests { get; set; } = false;
    public bool LogRequestHeaders { get; set; } = true;
    public bool LogRequestBody { get; set; } = true;
    public int MaxRequestBodySize { get; set; } = 4096;
    public HashSet<string> SensitiveHeaders { get; set; } = new()
    {
        "authorization", "cookie", "x-api-key", "api-key", "password", "secret"
    };
}
