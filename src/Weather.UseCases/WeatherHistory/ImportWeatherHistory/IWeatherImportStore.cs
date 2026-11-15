namespace Weather.UseCases.WeatherHistory.ImportWeatherHistory;

public interface IWeatherImportStore
{
    Task<long?> GetLatestTimestampAsync(CancellationToken cancellationToken);
    Task<int> AddMissingReadingsAsync(IReadOnlyList<WeatherImportReading> readings, CancellationToken cancellationToken);
}
