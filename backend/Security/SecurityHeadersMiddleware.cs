namespace MyPortfolioLandingPageBackend.Security;

/// <summary>
/// Middleware to add comprehensive security headers to all responses
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly SecurityHeadersOptions _options;

    public SecurityHeadersMiddleware(RequestDelegate next, SecurityHeadersOptions options)
    {
        _next = next;
        _options = options;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Add security headers before processing the request
        AddSecurityHeaders(context);

        await _next(context);
    }

    private void AddSecurityHeaders(HttpContext context)
    {
        var headers = context.Response.Headers;

        // Prevent clickjacking attacks
        headers.Append("X-Frame-Options", _options.XFrameOptions);
        
        // Prevent MIME type sniffing
        headers.Append("X-Content-Type-Options", "nosniff");
        
        // Enable XSS protection in older browsers
        headers.Append("X-XSS-Protection", "1; mode=block");
        
        // Control referrer information
        headers.Append("Referrer-Policy", _options.ReferrerPolicy);
        
        // Strict Transport Security (HSTS) - Forces HTTPS
        if (_options.EnableHSTS)
        {
            headers.Append("Strict-Transport-Security", 
                $"max-age={_options.HSTSMaxAge}; includeSubDomains; preload");
        }
        
        // Content Security Policy - Prevents XSS, injection attacks
        if (!string.IsNullOrEmpty(_options.ContentSecurityPolicy))
        {
            headers.Append("Content-Security-Policy", _options.ContentSecurityPolicy);
        }
        
        // Permissions Policy (formerly Feature Policy)
        headers.Append("Permissions-Policy", _options.PermissionsPolicy);
        
        // Prevent browser from caching sensitive data
        if (_options.PreventCaching)
        {
            headers.Append("Cache-Control", "no-store, no-cache, must-revalidate, private");
            headers.Append("Pragma", "no-cache");
            headers.Append("Expires", "0");
        }
        
        // Remove server header to hide technology stack
        headers.Remove("Server");
        headers.Remove("X-Powered-By");
        headers.Remove("X-AspNet-Version");
        headers.Remove("X-AspNetCore-Version");
        
        // Add custom security header to indicate hardened API
        headers.Append("X-Security-Level", "Ultra");
        
        // Add request ID for tracing (if not already present)
        if (!headers.ContainsKey("X-Request-Id"))
        {
            headers.Append("X-Request-Id", context.TraceIdentifier);
        }
    }
}

public class SecurityHeadersOptions
{
    public string XFrameOptions { get; set; } = "DENY";
    public string ReferrerPolicy { get; set; } = "strict-origin-when-cross-origin";
    public bool EnableHSTS { get; set; } = true;
    public int HSTSMaxAge { get; set; } = 31536000; // 1 year in seconds
    public string ContentSecurityPolicy { get; set; } = "default-src 'none'; frame-ancestors 'none';";
    public string PermissionsPolicy { get; set; } = 
        "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()";
    public bool PreventCaching { get; set; } = false; // Set to true for sensitive endpoints
}
