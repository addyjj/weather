using System.Net;
using Weather.Infrastructure.CurrentConditions;
using Xunit;

namespace Weather.Tests;

public sealed class AmbientWeatherConditionsProviderTests
{
    [Fact]
    public async Task MapsLatestOutdoorConditionsWithTheirUnits()
    {
        const string json = """
            [{"dateutc":1794690000000,"tempf":68,"feelsLike":67.5,"dewPoint":51,
              "humidity":55,"winddir":270,"windspeedmph":3.5,"windgustmph":8,
              "maxdailygust":12,"baromrelin":30.1,"baromabsin":29.5,
              "hourlyrainin":0.02,"dailyrainin":0.15,"solarradiation":125,"uv":1.5},
             {"tempf":60}]
            """;
        using var client = new HttpClient(new ResponseHandler(HttpStatusCode.OK, json));
        var provider = new AmbientWeatherConditionsProvider(client, ValidOptions());

        var conditions = await provider.GetCurrentConditionsAsync();

        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1794690000000), conditions.ObservedAtUtc);
        Assert.Equal(68, conditions.TemperatureFahrenheit);
        Assert.Equal(67.5, conditions.FeelsLikeFahrenheit);
        Assert.Equal(51, conditions.DewPointFahrenheit);
        Assert.Equal(55, conditions.HumidityPercent);
        Assert.Equal(270, conditions.WindDirectionDegrees);
        Assert.Equal(3.5, conditions.WindSpeedMph);
        Assert.Equal(8, conditions.WindGustMph);
        Assert.Equal(12, conditions.MaxDailyGustMph);
        Assert.Equal(30.1, conditions.RelativePressureInHg);
        Assert.Equal(29.5, conditions.AbsolutePressureInHg);
        Assert.Equal(0.02, conditions.HourlyRainRateInches);
        Assert.Equal(0.15, conditions.DailyRainInches);
        Assert.Equal(125, conditions.SolarRadiationWattsPerSquareMeter);
        Assert.Equal(1.5, conditions.UvIndex);
    }

    [Theory]
    [InlineData("[{\"tempf\":32}]", 32)]
    [InlineData("[{\"tempf\":-4}]", -4)]
    [InlineData("[{\"tempf\":72.5}]", 72.5)]
    public async Task ReturnsOutdoorFahrenheitReading(string json, double expected)
    {
        using var client = new HttpClient(new ResponseHandler(HttpStatusCode.OK, json));
        var provider = new AmbientWeatherConditionsProvider(client, ValidOptions());
        var conditions = await provider.GetCurrentConditionsAsync();
        Assert.Equal(expected, conditions.TemperatureFahrenheit);
        Assert.Null(conditions.HumidityPercent);
        Assert.Null(conditions.ObservedAtUtc);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[{\"tempinf\":70}]")]
    [InlineData("null")]
    [InlineData("[null]")]
    [InlineData("[{\"tempf\":68,\"dateutc\":9223372036854775807}]")]
    [InlineData("not json")]
    public async Task RejectsMissingOrMalformedOutdoorReadings(string json)
    {
        using var client = new HttpClient(new ResponseHandler(HttpStatusCode.OK, json));
        var provider = new AmbientWeatherConditionsProvider(client, ValidOptions());
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetCurrentConditionsAsync());
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task ReportsApiErrorsWithoutCredentials(HttpStatusCode status)
    {
        using var client = new HttpClient(new ResponseHandler(status, "secret response"));
        var provider = new AmbientWeatherConditionsProvider(client, ValidOptions());
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetCurrentConditionsAsync());
        Assert.Contains(((int)status).ToString(), error.Message);
        Assert.DoesNotContain("secret", error.ToString());
    }

    [Fact]
    public async Task RequiresConfiguration()
    {
        using var client = new HttpClient(new ResponseHandler(HttpStatusCode.OK, "[]"));
        var provider = new AmbientWeatherConditionsProvider(client, new AmbientWeatherOptions());
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetCurrentConditionsAsync());
    }

    [Fact]
    public async Task PropagatesCancellation()
    {
        using var client = new HttpClient(new ResponseHandler(HttpStatusCode.OK, "[]"));
        var provider = new AmbientWeatherConditionsProvider(client, ValidOptions());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.GetCurrentConditionsAsync(cancellation.Token));
    }

    private static AmbientWeatherOptions ValidOptions() => new()
    {
        ApiKey = "secret-api", ApplicationKey = "secret-app", DeviceMacAddress = "station"
    };

    private sealed class ResponseHandler(HttpStatusCode status, string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json) });
    }
}
