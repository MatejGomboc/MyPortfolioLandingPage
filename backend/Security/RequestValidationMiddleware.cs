using System.Text;
using System.Text.RegularExpressions;

namespace MyPortfolioLandingPageBackend.Security;

/// <summary>
/// Input validation and sanitization to prevent injection attacks
/// </summary>
public class RequestValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestValidationMiddleware> _logger;
    private readonly RequestValidationOptions _options;

    // Precompiled regex patterns for performance
    private static readonly Regex SqlInjectionRegex = new(
        @"(\b(SELECT|INSERT|UPDATE|DELETE|DROP|CREATE|ALTER|EXEC|EXECUTE|UNION|FROM|WHERE)\b)|(--)|(;)|(\*)|(')",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex XssRegex = new(
        @"<[^>]*(script|iframe|object|embed|form|input|button|textarea|svg|on\w+\s*=)[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PathTraversalRegex = new(
        @"(\.\.[\\/])|(%2e%2e[\\/])|(\.\./)|(%252e%252e)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CommandInjectionRegex = new(
        @"[;&|`$]|\b(cmd|powershell|bash|sh|nc|netcat)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public RequestValidationMiddleware(
        RequestDelegate next,
        ILogger<RequestValidationMiddleware> logger,
        RequestValidationOptions options)
    {
        _next = next;
        _logger = logger;
        _options = options;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Validate request size
        if (context.Request.ContentLength > _options.MaxRequestSize)
        {
            _logger.LogWarning("Request rejected - Too large: {Size} bytes from {IP}", 
                context.Request.ContentLength, GetClientIp(context));
            
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            await context.Response.WriteAsync("Request body too large");
            return;
        }

        // Validate URL and query string
        var urlValidation = ValidateUrl(context.Request.Path + context.Request.QueryString);
        if (!urlValidation.IsValid)
        {
            _logger.LogWarning("Request rejected - Invalid URL: {Reason} from {IP}", 
                urlValidation.Reason, GetClientIp(context));
            
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync($"Invalid request: {urlValidation.Reason}");
            return;
        }

        // Validate headers
        var headerValidation = ValidateHeaders(context.Request.Headers);
        if (!headerValidation.IsValid)
        {
            _logger.LogWarning("Request rejected - Invalid headers: {Reason} from {IP}", 
                headerValidation.Reason, GetClientIp(context));
            
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync($"Invalid headers: {headerValidation.Reason}");
            return;
        }

        // Validate request body for POST/PUT/PATCH
        if (IsBodyExpected(context.Request.Method) && context.Request.ContentLength > 0)
        {
            context.Request.EnableBuffering();
            var bodyValidation = await ValidateRequestBody(context.Request);
            
            if (!bodyValidation.IsValid)
            {
                _logger.LogWarning("Request rejected - Invalid body: {Reason} from {IP}", 
                    bodyValidation.Reason, GetClientIp(context));
                
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync($"Invalid request body: {bodyValidation.Reason}");
                return;
            }
        }

        await _next(context);
    }

    private ValidationResult ValidateUrl(string url)
    {
        // Check URL length
        if (url.Length > _options.MaxUrlLength)
        {
            return ValidationResult.Invalid("URL too long");
        }

        // Check for null bytes
        if (url.Contains('\0'))
        {
            return ValidationResult.Invalid("Null bytes not allowed");
        }

        // Check for path traversal
        if (PathTraversalRegex.IsMatch(url))
        {
            return ValidationResult.Invalid("Path traversal detected");
        }

        // Check for encoded attacks
        var decoded = Uri.UnescapeDataString(url);
        if (decoded != url && (SqlInjectionRegex.IsMatch(decoded) || XssRegex.IsMatch(decoded)))
        {
            return ValidationResult.Invalid("Encoded attack pattern detected");
        }

        return ValidationResult.Valid();
    }

    private ValidationResult ValidateHeaders(IHeaderDictionary headers)
    {
        foreach (var header in headers)
        {
            // Check header size
            if (header.Value.ToString().Length > _options.MaxHeaderValueLength)
            {
                return ValidationResult.Invalid($"Header '{header.Key}' value too long");
            }

            // Check for injection in header values
            var headerValue = header.Value.ToString();
            
            if (SqlInjectionRegex.IsMatch(headerValue))
            {
                return ValidationResult.Invalid($"SQL injection pattern in header '{header.Key}'");
            }

            if (CommandInjectionRegex.IsMatch(headerValue))
            {
                return ValidationResult.Invalid($"Command injection pattern in header '{header.Key}'");
            }

            // Check for CRLF injection
            if (headerValue.Contains('\r') || headerValue.Contains('\n'))
            {
                return ValidationResult.Invalid($"CRLF injection in header '{header.Key}'");
            }
        }

        return ValidationResult.Valid();
    }

    private async Task<ValidationResult> ValidateRequestBody(HttpRequest request)
    {
        request.Body.Position = 0;
        using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        request.Body.Position = 0;

        // Check for various injection patterns
        if (SqlInjectionRegex.IsMatch(body))
        {
            return ValidationResult.Invalid("SQL injection pattern detected");
        }

        if (XssRegex.IsMatch(body))
        {
            return ValidationResult.Invalid("XSS pattern detected");
        }

        if (CommandInjectionRegex.IsMatch(body))
        {
            return ValidationResult.Invalid("Command injection pattern detected");
        }

        // Check for oversized JSON depth (JSON bomb protection)
        if (IsJson(request.ContentType))
        {
            var depthCheck = CheckJsonDepth(body);
            if (!depthCheck.IsValid)
            {
                return depthCheck;
            }
        }

        return ValidationResult.Valid();
    }

    private ValidationResult CheckJsonDepth(string json)
    {
        var depth = 0;
        var maxDepth = 0;
        var inString = false;
        var escape = false;

        foreach (var c in json)
        {
            if (!inString)
            {
                if (c == '{' || c == '[')
                {
                    depth++;
                    maxDepth = Math.Max(maxDepth, depth);
                    
                    if (maxDepth > _options.MaxJsonDepth)
                    {
                        return ValidationResult.Invalid($"JSON depth exceeds maximum of {_options.MaxJsonDepth}");
                    }
                }
                else if (c == '}' || c == ']')
                {
                    depth--;
                }
                else if (c == '"' && !escape)
                {
                    inString = true;
                }
            }
            else
            {
                if (c == '"' && !escape)
                {
                    inString = false;
                }
            }

            escape = !escape && c == '\\';
        }

        return ValidationResult.Valid();
    }

    private bool IsBodyExpected(string method)
    {
        return method == "POST" || method == "PUT" || method == "PATCH";
    }

    private bool IsJson(string? contentType)
    {
        return contentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) ?? false;
    }

    private static string GetClientIp(HttpContext context)
    {
        return context.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',').FirstOrDefault()?.Trim()
            ?? context.Request.Headers["X-Real-IP"].FirstOrDefault()
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";
    }

    private class ValidationResult
    {
        public bool IsValid { get; private set; }
        public string? Reason { get; private set; }

        public static ValidationResult Valid() => new() { IsValid = true };
        public static ValidationResult Invalid(string reason) => new() { IsValid = false, Reason = reason };
    }
}

public class RequestValidationOptions
{
    public long MaxRequestSize { get; set; } = 1048576; // 1MB
    public int MaxUrlLength { get; set; } = 2048;
    public int MaxHeaderValueLength { get; set; } = 4096;
    public int MaxJsonDepth { get; set; } = 32;
}
