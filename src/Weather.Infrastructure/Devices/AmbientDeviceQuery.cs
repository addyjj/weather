using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Weather.Infrastructure.CurrentConditions;
using Weather.UseCases.Devices;

namespace Weather.Infrastructure.Devices;

public sealed class AmbientDeviceQuery(HttpClient client, AmbientWeatherOptions options, IMemoryCache cache)
    : IDeviceQuery
{
    public Task<IReadOnlyList<DeviceResult>> GetDevicesAsync(CancellationToken cancellationToken) =>
        GetCachedAsync<IReadOnlyList<DeviceResult>>((typeof(AmbientDeviceQuery), "devices"), async () =>
        {
            var devices = await GetAsync<AmbientDevice[]>("devices", cancellationToken);
            return devices.Select(d => new DeviceResult
            {
                Name = d.Info?.Name ?? "",
                Location = d.Info?.Coords?.Location ?? "",
                LatestData = d.LastData?.ToResult() ?? new()
            }).ToArray();
        }, cancellationToken);

    public Task<IReadOnlyList<DeviceDataResult>> GetDeviceDataAsync(
        string macAddress, DateTime? endDate, int limit, CancellationToken cancellationToken)
    {
        var endDateUnix = endDate.HasValue ? new DateTimeOffset(endDate.Value).ToUnixTimeMilliseconds() : (long?)null;
        return GetCachedAsync<IReadOnlyList<DeviceDataResult>>((typeof(AmbientDeviceQuery), macAddress, endDateUnix, limit), async () =>
        {
            var path = $"devices/{Uri.EscapeDataString(macAddress)}?limit={limit.ToString(CultureInfo.InvariantCulture)}";
            if (endDateUnix.HasValue) path += $"&endDate={endDateUnix.Value.ToString(CultureInfo.InvariantCulture)}";
            var data = await GetAsync<AmbientReading[]>(path, cancellationToken);
            return data.Select(d => d.ToResult()).ToArray();
        }, cancellationToken);
    }

    private async Task<T> GetCachedAsync<T>(object key, Func<Task<T>> factory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (cache.TryGetValue<T>(key, out var value)) return value!;
        value = await factory();
        cache.Set(key, value, TimeSpan.FromMinutes(5));
        return value;
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey) || string.IsNullOrWhiteSpace(options.ApplicationKey))
            throw new DeviceQueryException("Configure Ambient:ApiKey and Ambient:ApplicationKey.");
        if (!Uri.TryCreate(options.ApiUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme is not ("http" or "https"))
            throw new DeviceQueryException("Configure a valid Ambient:ApiUrl.");
        path += (path.Contains('?') ? "&" : "?") + $"apiKey={Uri.EscapeDataString(options.ApiKey)}" +
            $"&applicationKey={Uri.EscapeDataString(options.ApplicationKey)}";
        try
        {
            using var response = await client.GetAsync(new Uri(baseUri, path), cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new DeviceQueryException($"Ambient Weather returned HTTP {(int)response.StatusCode}.");
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
                ?? throw new DeviceQueryException("Ambient Weather returned an invalid device response.");
        }
        catch (HttpRequestException)
        {
            throw new DeviceQueryException("Could not connect to Ambient Weather.");
        }
        catch (JsonException)
        {
            throw new DeviceQueryException("Ambient Weather returned an invalid device response.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DeviceQueryException("Ambient Weather request timed out.");
        }
    }

    private sealed record AmbientDevice(AmbientInfo? Info, AmbientReading? LastData);
    private sealed record AmbientInfo(string? Name, AmbientCoordinates? Coords);
    private sealed record AmbientCoordinates(string? Location);

    internal sealed record AmbientReading
    {
        [JsonPropertyName("date")] public DateTime Date { get; init; }
        [JsonPropertyName("baromrelin")] public double BaromRel { get; init; }
        [JsonPropertyName("tempf")] public double TempOut { get; init; }
        [JsonPropertyName("humidity")] public int HumidityOut { get; init; }
        [JsonPropertyName("winddir")] public int WindDir { get; init; }
        [JsonPropertyName("windspeedmph")] public double WindSpeed { get; init; }
        [JsonPropertyName("windgustmph")] public double WindGust { get; init; }
        [JsonPropertyName("eventrainin")] public double EventRain { get; init; }
        [JsonPropertyName("dailyrainin")] public double DailyRain { get; init; }
        [JsonPropertyName("uv")] public int UvIndex { get; init; }
        [JsonPropertyName("feelsLike")] public double FeelsLike { get; init; }
        [JsonPropertyName("dewPoint")] public double DewPoint { get; init; }
        [JsonPropertyName("solarradiation")] public double SolarRadiation { get; init; }

        public DeviceDataResult ToResult() => new()
        {
            Date = Date, BaromRel = BaromRel, TempOut = TempOut, HumidityOut = HumidityOut,
            WindDir = WindDir, WindSpeed = WindSpeed, WindGust = WindGust,
            EventRain = EventRain, DailyRain = DailyRain, UvIndex = UvIndex,
            FeelsLike = FeelsLike, DewPoint = DewPoint, SolarRadiation = SolarRadiation
        };
    }
}
