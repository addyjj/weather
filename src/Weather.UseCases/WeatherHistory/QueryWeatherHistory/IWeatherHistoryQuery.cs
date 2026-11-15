namespace Weather.UseCases.WeatherHistory.QueryWeatherHistory;

public interface IWeatherHistoryQuery
{
    Task<WeatherHistoryResult> ExecuteAsync(QueryWeatherHistoryQuery query, CancellationToken cancellationToken = default);
}
