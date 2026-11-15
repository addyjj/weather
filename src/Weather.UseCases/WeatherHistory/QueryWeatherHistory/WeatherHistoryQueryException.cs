namespace Weather.UseCases.WeatherHistory.QueryWeatherHistory;

public sealed class WeatherHistoryQueryException(string message) : Exception(message);
