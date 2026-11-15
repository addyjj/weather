using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Serilog;
using System.Text.Json;
using Weather.Infrastructure.CurrentConditions;
using Weather.Infrastructure.WeatherHistory;
using Weather.Mcp.Tools;
using Weather.UseCases.CurrentConditions.GetCurrentConditions;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.Local.json"), optional: true);
if (builder.Environment.IsDevelopment()) builder.Configuration.AddUserSecrets<Program>(optional: true);
builder.Configuration
    .AddEnvironmentVariables()
    .AddCommandLine(args);
builder.Services.AddSerilog((services, logging) => logging
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"));
builder.WebHost.UseUrls(builder.Configuration["urls"] ?? "http://localhost:5000");
builder.Configuration["AllowedHosts"] ??= "localhost;127.0.0.1;[::1]";
builder.Services.AddSingleton(builder.Configuration.GetSection("Ambient").Get<AmbientWeatherOptions>() ?? new());
builder.Services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(20) });
builder.Services.AddTransient<ICurrentConditionsProvider, AmbientWeatherConditionsProvider>();
builder.Services.AddTransient<GetCurrentConditionsHandler>();
builder.Services.AddWeatherHistory(builder.Configuration.GetConnectionString("sql"));
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
    // Some clients probe these methods even when the server only offers tools.
    .WithListResourcesHandler((_, _) => ValueTask.FromResult(new ListResourcesResult()))
    .WithListResourceTemplatesHandler((_, _) => ValueTask.FromResult(new ListResourceTemplatesResult()))
    .WithRequestFilters(filters => filters.AddCallToolFilter(next => async (context, cancellationToken) =>
    {
        var logger = context.Services!.GetRequiredService<ILoggerFactory>().CreateLogger("Weather.Mcp.ToolCalls");
        var parameters = context.Params?.Arguments is { } arguments ? JsonSerializer.Serialize(arguments) : "{}";
        logger.LogInformation("MCP tool {ToolName} called with parameters {Arguments}", context.Params?.Name, parameters);
        return await next(context, cancellationToken);
    }))
    .WithTools<CurrentConditionsTools>()
    .WithTools<WeatherHistoryTools>();

await using var app = builder.Build();
app.UseSerilogRequestLogging();
app.MapMcp("/mcp");
var lifecycleLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Weather.Mcp.Lifecycle");
lifecycleLogger.LogInformation("Starting Weather MCP server in {Environment}", app.Environment.EnvironmentName);
app.Lifetime.ApplicationStarted.Register(() =>
{
    foreach (var address in app.Urls)
        lifecycleLogger.LogInformation("MCP ready at {Endpoint}", address.TrimEnd('/') + "/mcp");
});
app.Lifetime.ApplicationStopping.Register(() => lifecycleLogger.LogInformation("Weather MCP server stopping"));
app.Lifetime.ApplicationStopped.Register(() => lifecycleLogger.LogInformation("Weather MCP server stopped"));
await app.RunAsync();
