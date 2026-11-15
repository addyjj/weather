using Microsoft.AspNetCore.SignalR;
using Weather.UseCases.Devices.StreamWeatherUpdates;
using Weather.WebApi.Hubs;

namespace Weather.WebApi.HostedServices;

public sealed class WeatherBroadcastService(
    StreamWeatherUpdatesHandler updates, IHubContext<WeatherHub> hub, ILogger<WeatherBroadcastService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await foreach (var reading in updates.HandleAsync(new StreamWeatherUpdatesQuery(), stoppingToken))
                    await hub.Clients.All.SendAsync("weatherData", reading, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                // Socket exception messages may contain authentication query strings.
                logger.LogWarning("Ambient live updates disconnected. Retrying in 10 seconds.");
            }
            try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
