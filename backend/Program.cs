using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

WebApplicationBuilder appBuilder = WebApplication.CreateBuilder(args);

// Configure Kestrel to only bind to localhost
// This ensures the API is never directly accessible from outside
// and must be accessed through a reverse proxy
appBuilder.WebHost.ConfigureKestrel(serverOptions =>
{
    // Force localhost-only binding
    serverOptions.ListenLocalhost(5001);
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
