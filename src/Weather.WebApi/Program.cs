using Weather.Infrastructure.CurrentConditions;
using Weather.Infrastructure.Devices;
using Weather.UseCases.Devices;
using Weather.UseCases.Devices.GetDevices;
using Weather.UseCases.Devices.GetDeviceData;
using Weather.UseCases.Devices.StreamWeatherUpdates;
using Weather.WebApi.HostedServices;
using Weather.WebApi.Hubs;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration
    .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.Local.json"), optional: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args);
builder.WebHost.UseUrls(builder.Configuration["urls"] ?? "http://localhost:5001");
builder.Configuration["AllowedHosts"] ??= "localhost;127.0.0.1;[::1]";
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddTransient<IWeatherUpdateStream, AmbientWeatherUpdateStream>();
builder.Services.AddTransient<StreamWeatherUpdatesHandler>();
builder.Services.AddHostedService<WeatherBroadcastService>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton(builder.Configuration.GetSection("Ambient").Get<AmbientWeatherOptions>() ?? new());
builder.Services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(20) });
builder.Services.AddTransient<IDeviceQuery, AmbientDeviceQuery>();
builder.Services.AddTransient<GetDevicesHandler>();
builder.Services.AddTransient<GetDeviceDataHandler>();

await using var app = builder.Build();
app.Use(async (context, next) =>
{
    try { await next(context); }
    catch (DeviceQueryException exception)
    {
        await Results.Problem(statusCode: StatusCodes.Status502BadGateway,
            title: "Weather service unavailable", detail: exception.Message).ExecuteAsync(context);
    }
});
app.MapControllers();
app.MapHub<WeatherHub>("/weatherHub");
app.Lifetime.ApplicationStarted.Register(() =>
{
    foreach (var address in app.Urls)
        app.Logger.LogInformation("REST API ready at {Endpoint}", address);
});
await app.RunAsync();
