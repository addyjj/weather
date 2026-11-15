namespace Weather.UseCases.WeatherHistory.QueryWeatherHistory;

public sealed class QueryWeatherHistoryHandler(IWeatherHistoryQuery history)
{
    public Task<WeatherHistoryResult> HandleAsync(QueryWeatherHistoryQuery query, CancellationToken cancellationToken = default) =>
        history.ExecuteAsync(query, cancellationToken);
}
