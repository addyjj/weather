using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Weather.Infrastructure.CurrentConditions;
using Weather.Infrastructure.WeatherHistory;
using Weather.Infrastructure.WeatherHistory.Import;
using Weather.UseCases.WeatherHistory.ImportWeatherHistory;

if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine("Triggered Azure WebJob: import Ambient Weather history into dbo.DeviceData.");
    Console.WriteLine("dotnet run --project src/Weather.Import -- [--macAddress AA:BB:CC:DD:EE:FF]");
    Console.WriteLine("Requires ConnectionStrings:sql and Ambient:ApiKey/ApplicationKey/DeviceMacAddress (or --macAddress).");
    return 0;
}

var builder = Host.CreateApplicationBuilder(args);
// Emit only sanitized job progress, including on SQL/upstream failures.
builder.Logging.ClearProviders();
builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.Local.json"), optional: true);
if (builder.Environment.IsDevelopment()) builder.Configuration.AddUserSecrets<Program>(optional: true);
builder.Configuration.AddEnvironmentVariables().AddCommandLine(args);
var ambient = builder.Configuration.GetSection("Ambient").Get<AmbientWeatherOptions>() ?? new();
var macAddress = builder.Configuration["macAddress"] ?? ambient.DeviceMacAddress;
var connectionString = builder.Configuration.GetConnectionString("sql");
if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(macAddress) ||
    string.IsNullOrWhiteSpace(ambient.ApiKey) || string.IsNullOrWhiteSpace(ambient.ApplicationKey))
{
    Console.Error.WriteLine("Configure ConnectionStrings:sql and Ambient:ApiKey/ApplicationKey/DeviceMacAddress (or --macAddress).");
    return 1;
}
builder.Services.AddSingleton(ambient);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(30) });
builder.Services.AddDbContextFactory<WeatherHistoryContext>(options => options.UseSqlServer(connectionString, sql => sql.CommandTimeout(30)));
builder.Services.AddTransient<IWeatherImportSource, AmbientWeatherImportSource>();
builder.Services.AddTransient<IWeatherImportStore, SqlWeatherImportStore>();
builder.Services.AddTransient<ImportWeatherHistoryHandler>();
using var host = builder.Build();
var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);
var shutdownFile = Environment.GetEnvironmentVariable("WEBJOBS_SHUTDOWN_FILE");
// Kudu signals graceful shutdown by creating this file. ConsoleLifetime also
// handles SIGTERM/Ctrl+C when the job or App Service is stopped.
await using var shutdownMonitor = new Timer(_ =>
{
    if (!string.IsNullOrWhiteSpace(shutdownFile) && File.Exists(shutdownFile))
        cancellation.Cancel();
}, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
try
{
    await host.StartAsync(cancellation.Token);
    if (!string.IsNullOrWhiteSpace(shutdownFile) && File.Exists(shutdownFile))
        cancellation.Cancel();
    cancellation.Token.ThrowIfCancellationRequested();
    Console.WriteLine("Importing Ambient Weather history...");
    // Keep Kudu informed while a large backfill is waiting between API pages.
    await using var progress = new Timer(_ => Console.WriteLine("Import still running..."),
        null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    var result = await host.Services.GetRequiredService<ImportWeatherHistoryHandler>()
        .HandleAsync(new ImportWeatherHistoryCommand(macAddress), cancellation.Token);
    Console.WriteLine($"Import complete: {result.ReadingsAdded} readings added from {result.PagesRead} pages.");
    return 0;
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    Console.Error.WriteLine("Import canceled. No partial import was committed.");
    return 130;
}
catch (Exception)
{
    // Connection and upstream exception messages can contain credentials.
    Console.Error.WriteLine("Import failed. Check SQL access, Ambient configuration, and upstream availability. No partial import was committed.");
    return 1;
}
finally { await host.StopAsync(CancellationToken.None); }
