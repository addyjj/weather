using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace Weather.WebApi.HealthChecks;

public static class HealthCheckEndpoints
{
    public static void MapWeatherHealthChecks(this WebApplication app)
    {
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            Predicate = check => !check.Tags.Contains("detail")
        });
        app.MapHealthChecks("/health/details", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("detail"),
            ResponseWriter = (context, report) => context.Response.WriteAsJsonAsync(new
            {
                status = report.Status.ToString(),
                checks = report.Entries.Select(entry => new
                {
                    name = entry.Key,
                    status = entry.Value.Status.ToString(),
                    description = entry.Value.Description,
                    // Keep the old response shape without exposing provider exceptions.
                    exception = (string?)null
                })
            }, context.RequestAborted)
        });
    }
}
