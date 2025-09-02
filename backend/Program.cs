using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

WebApplicationBuilder appBuilder = WebApplication.CreateBuilder(args);

// Configure Kestrel to only bind to localhost with HTTPS
// This ensures the API is never directly accessible from outside
// and must be accessed through a reverse proxy
appBuilder.WebHost.ConfigureKestrel((context, serverOptions) =>
{
    var config = context.Configuration;
    
    // Get ports from configuration, with secure defaults
    var httpsPort = config.GetValue<int>("Kestrel:Endpoints:Https:Port", 5000);
    
    // Force localhost-only binding - this is hardcoded for security
    // The API should NEVER bind to external interfaces
    // HTTPS only for end-to-end encryption
    serverOptions.ListenLocalhost(httpsPort, listenOptions =>
    {
        listenOptions.UseHttps();
    });
});

// Only add OpenAPI services in Development environment
if (appBuilder.Environment.IsDevelopment())
{
    appBuilder.Services.AddOpenApi();
}

WebApplication app = appBuilder.Build();

// Only map OpenAPI endpoints in Development environment
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Enable HTTPS redirection to ensure all traffic is encrypted
app.UseHttpsRedirection();

app.MapGet("/health", () =>
{
    return Results.Ok();
})
.WithName("GetHealth");

app.Run();
