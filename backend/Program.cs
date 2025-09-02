using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

WebApplicationBuilder appBuilder = WebApplication.CreateBuilder(args);

// Configure Kestrel to only bind to localhost
// This ensures the API is never directly accessible from outside
// and must be accessed through a reverse proxy
appBuilder.WebHost.ConfigureKestrel((context, serverOptions) =>
{
    // Get port from configuration, default to 5001
    var config = context.Configuration;
    var port = config.GetValue<int>("Kestrel:Endpoints:Http:Port", 5001);
    
    // Force localhost-only binding - this is hardcoded for security
    // The API should NEVER bind to external interfaces
    serverOptions.ListenLocalhost(port);
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

// Note: UseHttpsRedirection removed - HTTPS is handled by reverse proxy
// app.UseHttpsRedirection();

app.MapGet("/health", () =>
{
    return Results.Ok();
})
.WithName("GetHealth");

app.Run();
