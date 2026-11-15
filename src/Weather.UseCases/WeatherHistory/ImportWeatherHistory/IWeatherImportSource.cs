namespace Weather.UseCases.WeatherHistory.ImportWeatherHistory;

public interface IWeatherImportSource
{
    Task<IReadOnlyList<WeatherImportReading>> GetReadingsAsync(string macAddress, long endDateUtc, CancellationToken cancellationToken);
}
