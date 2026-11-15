using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Weather.Infrastructure.CurrentConditions;
using Weather.UseCases.WeatherHistory.ImportWeatherHistory;

namespace Weather.Infrastructure.WeatherHistory.Import;

public sealed class AmbientWeatherImportSource(HttpClient client, AmbientWeatherOptions options) : IWeatherImportSource
{
    public async Task<IReadOnlyList<WeatherImportReading>> GetReadingsAsync(string macAddress, long endDateUtc, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey) || string.IsNullOrWhiteSpace(options.ApplicationKey))
            throw new InvalidOperationException("Configure Ambient:ApiKey and Ambient:ApplicationKey.");
        if (!Uri.TryCreate(options.ApiUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri) || baseUri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("Configure a valid Ambient:ApiUrl.");
        var requestPath = $"devices/{Uri.EscapeDataString(macAddress)}?endDate={endDateUtc.ToString(CultureInfo.InvariantCulture)}" +
            $"&apiKey={Uri.EscapeDataString(options.ApiKey)}&applicationKey={Uri.EscapeDataString(options.ApplicationKey)}";
        try
        {
            using var response = await client.GetAsync(new Uri(baseUri, requestPath), cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Ambient Weather returned HTTP {(int)response.StatusCode}.");
            var readings = await response.Content.ReadFromJsonAsync<AmbientImportReading[]>(cancellationToken)
                ?? throw new JsonException();
            if (readings.Any(x => x is null)) throw new JsonException();
            return readings.Select(reading => new WeatherImportReading
            {
                DateUtc = reading.DateUtc,
                TempIn = reading.TempIn,
                HumidityIn = reading.HumidityIn,
                BaromRel = reading.BaromRel,
                BaromAbs = reading.BaromAbs,
                TempOut = reading.TempOut,
                BattOut = reading.BattOut,
                HumidityOut = reading.HumidityOut,
                WindDir = reading.WindDir,
                WindSpeed = reading.WindSpeed,
                WindGust = reading.WindGust,
                MaxDailyGust = reading.MaxDailyGust,
                HourlyRainRate = reading.HourlyRainRate,
                EventRain = reading.EventRain,
                DailyRain = reading.DailyRain,
                WeeklyRain = reading.WeeklyRain,
                MonthlyRain = reading.MonthlyRain,
                TotalRain = reading.TotalRain,
                SolarRadiation = reading.SolarRadiation,
                Uv = reading.Uv,
                BattCo2 = reading.BattCo2,
                FeelsLike = reading.FeelsLike,
                DewPoint = reading.DewPoint,
                FeelsLikeIn = reading.FeelsLikeIn,
                DewPointIn = reading.DewPointIn,
                Loc = reading.Loc,
                Date = reading.Date,
            }).ToArray();
        }
        catch (HttpRequestException) { throw new InvalidOperationException("Could not connect to Ambient Weather."); }
        catch (JsonException) { throw new InvalidOperationException("Ambient Weather returned invalid import data."); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new InvalidOperationException("Ambient Weather import request timed out."); }
    }

    private sealed record AmbientImportReading
    {
        [JsonPropertyName("dateutc")] public long DateUtc { get; init; }
        [JsonPropertyName("tempinf")] public double TempIn { get; init; }
        [JsonPropertyName("humidityin")] public int HumidityIn { get; init; }
        [JsonPropertyName("baromrelin")] public double BaromRel { get; init; }
        [JsonPropertyName("baromabsin")] public double BaromAbs { get; init; }
        [JsonPropertyName("tempf")] public double TempOut { get; init; }
        [JsonPropertyName("battout")] [JsonConverter(typeof(AmbientBatteryConverter))] public bool BattOut { get; init; }
        [JsonPropertyName("humidity")] public int HumidityOut { get; init; }
        [JsonPropertyName("winddir")] public int WindDir { get; init; }
        [JsonPropertyName("windspeedmph")] public double WindSpeed { get; init; }
        [JsonPropertyName("windgustmph")] public double WindGust { get; init; }
        [JsonPropertyName("maxdailygust")] public double MaxDailyGust { get; init; }
        [JsonPropertyName("hourlyrainin")] public double HourlyRainRate { get; init; }
        [JsonPropertyName("eventrainin")] public double EventRain { get; init; }
        [JsonPropertyName("dailyrainin")] public double DailyRain { get; init; }
        [JsonPropertyName("weeklyrainin")] public double WeeklyRain { get; init; }
        [JsonPropertyName("monthlyrainin")] public double MonthlyRain { get; init; }
        [JsonPropertyName("totalrainin")] public double TotalRain { get; init; }
        [JsonPropertyName("solarradiation")] public double SolarRadiation { get; init; }
        [JsonPropertyName("uv")] public int Uv { get; init; }
        [JsonPropertyName("batt_co2")] [JsonConverter(typeof(AmbientBatteryConverter))] public bool BattCo2 { get; init; }
        [JsonPropertyName("feelsLike")] public double FeelsLike { get; init; }
        [JsonPropertyName("dewPoint")] public double DewPoint { get; init; }
        [JsonPropertyName("feelsLikein")] public double FeelsLikeIn { get; init; }
        [JsonPropertyName("dewPointin")] public double DewPointIn { get; init; }
        [JsonPropertyName("loc")] public string Loc { get; init; } = "";
        [JsonPropertyName("date")] public DateTime Date { get; init; }
    }
}
