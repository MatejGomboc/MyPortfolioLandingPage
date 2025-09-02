using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
