using System.Net;
using Weather.Infrastructure.CurrentConditions;
using Weather.Infrastructure.WeatherHistory.Import;
using Xunit;

namespace Weather.Tests;

public sealed class AmbientWeatherImportSourceTests
{
    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("-1", true)]
    [InlineData("\"True\"", true)]
    [InlineData("\"false\"", false)]
    public async Task AcceptsAmbientBatteryRepresentations(string flag, bool expected)
    {
        using var transport = new StubHandler((_, _) => Response(HttpStatusCode.OK,
            $$"""[{"dateutc":1000,"battout":{{flag}},"batt_co2":{{flag}}}]"""));
        using var http = new HttpClient(transport);
        var reading = Assert.Single(await new AmbientWeatherImportSource(http, Options()).GetReadingsAsync("station", 1000, default));
        Assert.Equal(expected, reading.BattOut);
        Assert.Equal(expected, reading.BattCo2);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("[null]")]
    [InlineData("[{\"battout\":\"unknown\"}]")]
    [InlineData("[{\"batt_co2\":null}]")]
    [InlineData("[{\"battout\":{}}]")]
    public async Task RejectsMalformedPayloadsWithoutExposingUpstreamData(string json)
    {
        using var transport = new StubHandler((_, _) => Response(HttpStatusCode.OK, json));
        using var http = new HttpClient(transport);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new AmbientWeatherImportSource(http, Options()).GetReadingsAsync("station", 1000, default));
        Assert.Equal("Ambient Weather returned invalid import data.", error.Message);
    }

    [Theory]
    [InlineData("http", "Ambient Weather returned HTTP 429.")]
    [InlineData("network", "Could not connect to Ambient Weather.")]
    [InlineData("timeout", "Ambient Weather import request timed out.")]
    public async Task SanitizesUpstreamFailures(string failure, string expected)
    {
        using var transport = new StubHandler((_, _) => failure switch
        {
            "network" => throw new HttpRequestException("secret-api secret-app"),
            "timeout" => throw new OperationCanceledException("secret-api secret-app"),
            _ => Response(HttpStatusCode.TooManyRequests, "secret-api secret-app")
        });
        using var http = new HttpClient(transport);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new AmbientWeatherImportSource(http, Options()).GetReadingsAsync("station", 1000, default));
        Assert.Equal(expected, error.Message);
        Assert.DoesNotContain("secret", error.ToString());
    }

    [Fact]
    public async Task PreservesCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        using var transport = new StubHandler((_, token) =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Response(HttpStatusCode.OK, "[]");
        });
        using var http = new HttpClient(transport);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new AmbientWeatherImportSource(http, Options()).GetReadingsAsync("station", 1000, cancellation.Token));
    }

    [Theory]
    [InlineData("", "app", "https://example.test/")]
    [InlineData("api", " ", "https://example.test/")]
    [InlineData("api", "app", "invalid")]
    [InlineData("api", "app", "file:///tmp/")]
    public async Task RejectsInvalidConfigurationBeforeSending(string apiKey, string appKey, string url)
    {
        using var transport = new StubHandler((_, _) => throw new Xunit.Sdk.XunitException("Unexpected HTTP request."));
        using var http = new HttpClient(transport);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AmbientWeatherImportSource(http,
            new AmbientWeatherOptions { ApiKey = apiKey, ApplicationKey = appKey, ApiUrl = url })
            .GetReadingsAsync("station", 1000, default));
    }

    private static AmbientWeatherOptions Options() => new() { ApiKey = "secret-api", ApplicationKey = "secret-app" };
    private static HttpResponseMessage Response(HttpStatusCode status, string json) => new(status) { Content = new StringContent(json) };
    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(send(request, cancellationToken));
    }
}
