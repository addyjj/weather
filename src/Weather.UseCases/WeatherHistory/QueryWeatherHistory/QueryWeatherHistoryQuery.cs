namespace Weather.UseCases.WeatherHistory.QueryWeatherHistory;

public sealed record QueryWeatherHistoryQuery(IReadOnlyDictionary<string, string> Options, string TimeZone = "UTC");
