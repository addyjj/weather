using Weather.Infrastructure.CurrentConditions;
using Weather.Infrastructure.Devices;
using Weather.UseCases.Devices;
using Weather.UseCases.Devices.GetDevices;
using Weather.UseCases.Devices.GetDeviceData;
using Weather.UseCases.Devices.StreamWeatherUpdates;
using Weather.WebApi.HostedServices;
using Weather.WebApi.Hubs;
using Weather.Infrastructure.HealthChecks;
using Weather.WebApi.HealthChecks;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.Local.json"), optional: true);
if (builder.Environment.IsDevelopment()) builder.Configuration.AddUserSecrets<Program>(optional: true);
builder.Configuration
    .AddEnvironmentVariables()
    .AddCommandLine(args);
builder.WebHost.UseUrls(builder.Configuration["urls"] ?? "http://localhost:5001");
// Keep API host filtering separate from settings inherited by KuduAgent.
builder.Configuration["AllowedHosts"] = builder.Configuration["WeatherHttp:AllowedHosts"]
    ?? builder.Configuration["AllowedHosts"] ?? "localhost;127.0.0.1;[::1]";
builder.Services.AddControllers();
builder.Services.AddSignalR();
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()?
    .Where(origin => !string.IsNullOrWhiteSpace(origin)).Select(origin => origin.Trim()).ToArray() ?? [];
builder.Services.AddCors(options => options.AddPolicy("WeatherUi", policy =>
    policy.WithOrigins(allowedOrigins).AllowAnyMethod().AllowAnyHeader()));
builder.Services.AddHttpsRedirection(options => options.HttpsPort = builder.Configuration.GetValue<int?>("Https:Port"));
builder.Services.AddTransient<IWeatherUpdateStream, AmbientWeatherUpdateStream>();
builder.Services.AddTransient<StreamWeatherUpdatesHandler>();
builder.Services.AddHostedService<WeatherBroadcastService>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton(builder.Configuration.GetSection("Ambient").Get<AmbientWeatherOptions>() ?? new());
builder.Services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(20) });
builder.Services.AddTransient<AmbientDeviceQuery>();
builder.Services.AddTransient<IDeviceQuery>(services => services.GetRequiredService<AmbientDeviceQuery>());
builder.Services.AddTransient<GetDevicesHandler>();
builder.Services.AddTransient<GetDeviceDataHandler>();
builder.Services.AddHealthChecks()
    .AddCheck("database", new DatabaseHealthCheck(builder.Configuration.GetConnectionString("sql")),
        tags: ["detail"], timeout: TimeSpan.FromSeconds(10))
    .AddCheck<AmbientWeatherApiHealthCheck>("ambient_weather_api", tags: ["detail"], timeout: TimeSpan.FromSeconds(10));

await using var app = builder.Build();
if (builder.Configuration.GetValue("Https:RedirectEnabled", true)) app.UseHttpsRedirection();
app.UseRouting();
if (allowedOrigins.Length > 0) app.UseCors("WeatherUi");
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
app.MapWeatherHealthChecks();
app.Lifetime.ApplicationStarted.Register(() =>
{
    foreach (var address in app.Urls)
        app.Logger.LogInformation("REST API ready at {Endpoint}", address);
});
await app.RunAsync();
