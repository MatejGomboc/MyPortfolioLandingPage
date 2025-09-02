using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MyPortfolioLandingPageBackend.Security;

WebApplicationBuilder appBuilder = WebApplication.CreateBuilder(args);

// Configure Kestrel: localhost-only HTTPS binding for security
// API is never directly accessible from outside - must use reverse proxy
appBuilder.WebHost.ConfigureKestrel((context, serverOptions) =>
{
    // Force localhost-only binding with HTTPS (port from config, default 5000)
    serverOptions.ListenLocalhost(
        context.Configuration.GetValue<int>("Kestrel:Endpoints:Https:Port", 5000),
        listenOptions => listenOptions.UseHttps()
    );
    
    // Set server limits for security
    serverOptions.Limits.MaxRequestBodySize = 1048576; // 1MB
    serverOptions.Limits.MaxRequestHeaderCount = 100;
    serverOptions.Limits.MaxRequestLineSize = 8192;
    serverOptions.Limits.MaxRequestHeadersTotalSize = 32768;
    
    // Set timeouts to prevent slowloris attacks
    serverOptions.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(120);
    serverOptions.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
});

// Configure services
appBuilder.Services.AddSingleton<RateLimiterOptions>(provider =>
{
    var config = provider.GetRequiredService<IConfiguration>();
    return new RateLimiterOptions
    {
        MaxRequests = config.GetValue<int>("Security:RateLimit:MaxRequests", 100),
        TimeWindow = TimeSpan.FromMinutes(config.GetValue<int>("Security:RateLimit:WindowMinutes", 1))
    };
});

appBuilder.Services.AddSingleton<SecurityHeadersOptions>(provider =>
{
    return new SecurityHeadersOptions
    {
        EnableHSTS = !provider.GetRequiredService<IWebHostEnvironment>().IsDevelopment(),
        PreventCaching = false
    };
});

appBuilder.Services.AddSingleton<ApiKeyOptions>(provider =>
{
    var config = provider.GetRequiredService<IConfiguration>();
    var apiKeys = config.GetSection("Security:ApiKeys").Get<List<string>>() ?? new List<string>();
    
    // In production, API keys should come from secure configuration
    if (!provider.GetRequiredService<IWebHostEnvironment>().IsDevelopment() && !apiKeys.Any())
    {
        throw new InvalidOperationException("API keys must be configured in production");
    }
    
    return new ApiKeyOptions
    {
        ApiKeys = apiKeys,
        ExcludedPaths = new List<string> { "/health" }
    };
});

appBuilder.Services.AddSingleton<SecurityAuditOptions>(provider =>
{
    var isDevelopment = provider.GetRequiredService<IWebHostEnvironment>().IsDevelopment();
    return new SecurityAuditOptions
    {
        LogSuccessfulRequests = isDevelopment,
        LogRequestHeaders = true,
        LogRequestBody = true
    };
});

appBuilder.Services.AddSingleton<RequestValidationOptions>();

// Add health checks
appBuilder.Services.AddHealthChecks();

// Configure CORS (restrictive by default)
appBuilder.Services.AddCors(options =>
{
    options.AddPolicy("RestrictiveCors", policy =>
    {
        // In production, replace with specific origins
        policy.WithOrigins("https://localhost:3000")
              .AllowCredentials()
              .WithMethods("GET", "POST")
              .WithHeaders("Content-Type", "X-API-Key", "X-Request-Id")
              .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
    });
});

// Only add OpenAPI services in Development environment
if (appBuilder.Environment.IsDevelopment())
{
    appBuilder.Services.AddOpenApi();
}

// Add response compression (but be careful with BREACH attacks)
appBuilder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = false; // Disabled for HTTPS to prevent BREACH attacks
});

WebApplication app = appBuilder.Build();

// Order matters! Security middleware should run first
// 1. Security Audit - Log everything for forensics
app.UseMiddleware<SecurityAuditMiddleware>();

// 2. Security Headers - Add protective headers
app.UseMiddleware<SecurityHeadersMiddleware>();

// 3. Request Validation - Block malicious inputs
app.UseMiddleware<RequestValidationMiddleware>();

// 4. Rate Limiting - Prevent abuse
app.UseMiddleware<RateLimiterMiddleware>();

// 5. API Key Authentication - Require authentication
if (!app.Environment.IsDevelopment())
{
    app.UseMiddleware<ApiKeyAuthenticationMiddleware>();
}

// 6. CORS
app.UseCors("RestrictiveCors");

// 7. HTTPS Redirection
app.UseHttpsRedirection();

// 8. Response Compression (after security checks)
app.UseResponseCompression();

// Only map OpenAPI endpoints in Development environment
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Health check endpoint (unauthenticated for monitoring)
app.MapHealthChecks("/health");

// Example secure API endpoint
app.MapGet("/api/secure", (HttpContext context) =>
{
    // Check if authenticated
    if (!context.Items.ContainsKey("Authenticated"))
    {
        return Results.Unauthorized();
    }
    
    return Results.Ok(new 
    { 
        message = "Secure endpoint accessed successfully",
        requestId = context.Items["RequestId"],
        timestamp = DateTime.UtcNow
    });
})
.WithName("SecureEndpoint")
.RequireAuthorization(); // This would work with proper auth setup

// Catch-all for undefined routes (security through obscurity)
app.Map("{*path}", (HttpContext context) =>
{
    context.Response.StatusCode = StatusCodes.Status404NotFound;
    return Task.CompletedTask;
});

// Graceful shutdown configuration
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(() =>
{
    app.Logger.LogInformation("Application is shutting down gracefully...");
    // Add cleanup code here if needed
});

app.Run();
