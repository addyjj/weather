using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Weather.UseCases.CurrentConditions.GetCurrentConditions;

namespace Weather.Infrastructure.CurrentConditions;

public sealed class AmbientWeatherConditionsProvider(HttpClient httpClient, AmbientWeatherOptions options)
    : ICurrentConditionsProvider
{
    public async Task<CurrentConditionsResult> GetCurrentConditionsAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey) ||
            string.IsNullOrWhiteSpace(options.ApplicationKey) ||
            string.IsNullOrWhiteSpace(options.DeviceMacAddress))
            throw new InvalidOperationException("Configure Ambient:ApiKey, Ambient:ApplicationKey, and Ambient:DeviceMacAddress.");

        var baseUri = new Uri(options.ApiUrl.TrimEnd('/') + "/");
        var path = $"devices/{Uri.EscapeDataString(options.DeviceMacAddress)}?limit=1" +
            $"&apiKey={Uri.EscapeDataString(options.ApiKey)}" +
            $"&applicationKey={Uri.EscapeDataString(options.ApplicationKey)}";
        try
        {
            using var response = await httpClient.GetAsync(new Uri(baseUri, path), cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Ambient Weather returned HTTP {(int)response.StatusCode}. Check credentials, station configuration, or API rate limits.");

            var readings = await response.Content.ReadFromJsonAsync<Reading[]>(cancellationToken);
            if (readings is not { Length: > 0 } || readings[0]?.TemperatureFahrenheit is not { } fahrenheit ||
                !double.IsFinite(fahrenheit))
                throw new InvalidOperationException("Ambient Weather returned no outdoor temperature for the configured station.");

            if (readings[0].DateUtc is < -62135596800000L or > 253402300799999L)
                throw new InvalidOperationException("Ambient Weather returned an invalid observation timestamp.");

            return new CurrentConditionsResult
            {
                TemperatureFahrenheit = fahrenheit,
                ObservedAtUtc = readings[0].DateUtc is { } timestamp ? DateTimeOffset.FromUnixTimeMilliseconds(timestamp) : null,
                FeelsLikeFahrenheit = readings[0].FeelsLikeFahrenheit,
                DewPointFahrenheit = readings[0].DewPointFahrenheit,
                HumidityPercent = readings[0].HumidityPercent,
                WindDirectionDegrees = readings[0].WindDirectionDegrees,
                WindSpeedMph = readings[0].WindSpeedMph,
                WindGustMph = readings[0].WindGustMph,
                MaxDailyGustMph = readings[0].MaxDailyGustMph,
                RelativePressureInHg = readings[0].RelativePressureInHg,
                AbsolutePressureInHg = readings[0].AbsolutePressureInHg,
                HourlyRainRateInches = readings[0].HourlyRainRateInches,
                DailyRainInches = readings[0].DailyRainInches,
                SolarRadiationWattsPerSquareMeter = readings[0].SolarRadiationWattsPerSquareMeter,
                UvIndex = readings[0].UvIndex,
            };
        }
        catch (HttpRequestException)
        {
            // Request URLs contain credentials; do not expose the original exception.
            throw new InvalidOperationException("Could not connect to Ambient Weather.");
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("Ambient Weather returned an invalid weather response.");
        }
    }

    private sealed record Reading
    {
        [JsonPropertyName("dateutc")] public long? DateUtc { get; init; }
        [JsonPropertyName("tempf")] public double? TemperatureFahrenheit { get; init; }
        [JsonPropertyName("feelsLike")] public double? FeelsLikeFahrenheit { get; init; }
        [JsonPropertyName("dewPoint")] public double? DewPointFahrenheit { get; init; }
        [JsonPropertyName("humidity")] public double? HumidityPercent { get; init; }
        [JsonPropertyName("winddir")] public double? WindDirectionDegrees { get; init; }
        [JsonPropertyName("windspeedmph")] public double? WindSpeedMph { get; init; }
        [JsonPropertyName("windgustmph")] public double? WindGustMph { get; init; }
        [JsonPropertyName("maxdailygust")] public double? MaxDailyGustMph { get; init; }
        [JsonPropertyName("baromrelin")] public double? RelativePressureInHg { get; init; }
        [JsonPropertyName("baromabsin")] public double? AbsolutePressureInHg { get; init; }
        [JsonPropertyName("hourlyrainin")] public double? HourlyRainRateInches { get; init; }
        [JsonPropertyName("dailyrainin")] public double? DailyRainInches { get; init; }
        [JsonPropertyName("solarradiation")] public double? SolarRadiationWattsPerSquareMeter { get; init; }
        [JsonPropertyName("uv")] public double? UvIndex { get; init; }
    }
}
