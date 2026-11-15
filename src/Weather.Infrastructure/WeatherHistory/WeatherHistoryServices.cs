using Microsoft.AspNetCore.OData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Weather.UseCases.WeatherHistory.QueryWeatherHistory;

namespace Weather.Infrastructure.WeatherHistory;

public static class WeatherHistoryServices
{
    public static IServiceCollection AddWeatherHistory(this IServiceCollection services, string? connectionString)
    {
        services.AddSingleton<WeatherHistoryOData>();
        services.AddControllers().AddOData(options => options.EnableQueryFeatures(1000));
        services.AddDbContextFactory<WeatherHistoryContext>(options => options.UseSqlServer(
            string.IsNullOrWhiteSpace(connectionString) ? null : connectionString, sql => sql.CommandTimeout(30)));
        services.AddTransient<IWeatherHistoryQuery, SqlWeatherHistoryQuery>();
        services.AddTransient<QueryWeatherHistoryHandler>();
        return services;
    }
}
