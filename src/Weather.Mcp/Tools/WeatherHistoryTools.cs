using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Weather.UseCases.WeatherHistory.QueryWeatherHistory;

namespace Weather.Mcp.Tools;

[McpServerToolType]
public sealed class WeatherHistoryTools(QueryWeatherHistoryHandler handler)
{
    [McpServerTool(Name = "query_weather_history", ReadOnly = true, Idempotent = false)]
    [Description("Query historical weather readings stored in SQL Server. Supports filtering, field selection, computed fields, grouping, aggregates, sorting, and pagination. " +
        "Returns rows, hasMore, nextSkip, and an optional totalCount. Set options.timeZone to the user's timezone for local timestamps and calendar groups; defaults to UTC. " +
        "Fields: DateUtc (epoch milliseconds, key), Date (UTC), LocalDate (timeZone with UTC offset), Year/Month/Day/Hour (timeZone), TempIn/TempOut/FeelsLike/DewPoint/FeelsLikeIn/DewPointIn (Fahrenheit), " +
        "HumidityIn/HumidityOut (%), WindDir (degrees), WindSpeed/WindGust/MaxDailyGust (mph), BaromRel/BaromAbs (inHg), " +
        "HourlyRainRate (in/hr), EventRain/DailyRain/WeeklyRain/MonthlyRain/TotalRain (cumulative inches), SolarRadiation (W/m²), Uv, BattOut, BattCo2, Loc.")]
    public async Task<WeatherHistoryResult> QueryWeatherHistory(
        [Description("Optional query settings. Supply an empty object for the first 100 readings. Each property describes its OData syntax and defaults.")]
        WeatherHistoryOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            return await handler.HandleAsync(options.ToQuery(), cancellationToken);
        }
        catch (WeatherHistoryQueryException ex)
        {
            throw new McpException(ex.Message);
        }
    }
}
