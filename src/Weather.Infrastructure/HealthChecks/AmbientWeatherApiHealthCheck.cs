using Microsoft.Extensions.Diagnostics.HealthChecks;
using Weather.Infrastructure.CurrentConditions;
using Weather.Infrastructure.Devices;

namespace Weather.Infrastructure.HealthChecks;

public sealed class AmbientWeatherApiHealthCheck(AmbientDeviceQuery devices, AmbientWeatherOptions options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(options.DeviceMacAddress))
            return HealthCheckResult.Unhealthy("Configure Ambient:DeviceMacAddress.");
        try
        {
            // A fresh request must not be masked by the five-minute device cache.
            await devices.ProbeAsync(options.DeviceMacAddress, cancellationToken);
            return HealthCheckResult.Healthy("Ambient Weather API is healthy.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy("Ambient Weather API connection failed.");
        }
    }
}
