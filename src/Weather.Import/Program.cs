using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Weather.Infrastructure.CurrentConditions;
using Weather.Infrastructure.WeatherHistory;
using Weather.Infrastructure.WeatherHistory.Import;
using Weather.UseCases.WeatherHistory.ImportWeatherHistory;

if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine("Import Ambient Weather history into the existing dbo.DeviceData table.");
    Console.WriteLine("dotnet run --project src/Weather.Import -- [--macAddress AA:BB:CC:DD:EE:FF]");
    Console.WriteLine("Requires ConnectionStrings:sql and Ambient:ApiKey/ApplicationKey/DeviceMacAddress (or --macAddress).");
    return 0;
}

var builder = Host.CreateApplicationBuilder(args);
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
using var cancellation = new CancellationTokenSource();
ConsoleCancelEventHandler onCancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
Console.CancelKeyPress += onCancel;
try
{
    Console.WriteLine("Importing Ambient Weather history...");
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
finally { Console.CancelKeyPress -= onCancel; }
