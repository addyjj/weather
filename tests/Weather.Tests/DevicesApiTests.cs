using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Weather.Infrastructure.CurrentConditions;
using Weather.Infrastructure.Devices;
using Microsoft.Extensions.Caching.Memory;
using Weather.UseCases.Devices;
using Weather.UseCases.Devices.GetDeviceData;
using Xunit;

namespace Weather.Tests;

public sealed class DevicesApiTests
{
    private const string Reading = """
        {"date":"2026-11-14T12:00:00Z","baromrelin":30.1,"tempf":68,"humidity":55,
         "winddir":270,"windspeedmph":3.5,"windgustmph":8,"eventrainin":0.1,"dailyrainin":0.2,
         "uv":2,"feelsLike":67.5,"dewPoint":51,"solarradiation":125}
        """;

    [Fact]
    public async Task RestHostMatchesDeviceRoutesResponsesParametersAndCache()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        using var ambient = new HttpListener();
        var ambientUrl = $"http://127.0.0.1:{port}/";
        ambient.Prefixes.Add(ambientUrl);
        ambient.Start();
        var requests = 0;
        var serve = Task.Run(async () =>
        {
            while (!timeout.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await ambient.GetContextAsync().WaitAsync(timeout.Token); }
                catch (Exception) when (timeout.IsCancellationRequested || !ambient.IsListening) { break; }
                try
                {
                    Interlocked.Increment(ref requests);
                    Assert.Equal("test-api", context.Request.QueryString["apiKey"]);
                    Assert.Equal("test-app", context.Request.QueryString["applicationKey"]);
                    var path = context.Request.Url!.AbsolutePath;
                    if (path == "/devices/fail")
                    {
                        context.Response.StatusCode = 429;
                        await context.Response.OutputStream.WriteAsync("secret upstream body"u8.ToArray(), timeout.Token);
                        continue;
                    }
                    string json;
                    if (path == "/devices")
                        json = "[{\"info\":{\"name\":\"Backyard\",\"coords\":{\"location\":\"Home\"}},\"lastData\":" + Reading + "},{}]";
                    else
                    {
                        Assert.Equal("/devices/AA:BB:CC:DD:EE:FF", Uri.UnescapeDataString(path));
                        if (context.Request.QueryString["endDate"] is { } endDate)
                        {
                            Assert.Equal("1794657600000", endDate);
                            Assert.Equal("2", context.Request.QueryString["limit"]);
                        }
                        else Assert.Equal("10", context.Request.QueryString["limit"]);
                        json = "[" + Reading + "]";
                    }
                    context.Response.ContentType = "application/json";
                    await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(json), timeout.Token);
                }
                finally { context.Response.Close(); }
            }
        });
        try
        {
            await using var server = await HttpMcpServer.StartRestAsync(new Dictionary<string, string?>
            {
                ["Ambient__ApiUrl"] = ambientUrl, ["Ambient__SocketUrl"] = "http://127.0.0.1:1",
                ["Ambient__ApiKey"] = "test-api",
                ["Ambient__ApplicationKey"] = "test-app"
            }, timeout.Token);
            using var http = new HttpClient { BaseAddress = server.Endpoint };
            using var devices = JsonDocument.Parse(await http.GetStringAsync("/Devices", timeout.Token));
            var device = devices.RootElement[0];
            Assert.Equal(new[] { "name", "location", "latestData" }, device.EnumerateObject().Select(p => p.Name));
            Assert.Equal("Backyard", device.GetProperty("name").GetString());
            Assert.Equal("Home", device.GetProperty("location").GetString());
            AssertReading(device.GetProperty("latestData"));
            var empty = devices.RootElement[1];
            Assert.Equal("", empty.GetProperty("name").GetString());
            Assert.Equal("", empty.GetProperty("location").GetString());
            Assert.Equal(0, empty.GetProperty("latestData").GetProperty("tempOut").GetDouble());
            await http.GetStringAsync("/Devices", timeout.Token);

            const string dataPath = "/Devices/AA:BB:CC:DD:EE:FF/data";
            using var data = JsonDocument.Parse(await http.GetStringAsync(dataPath, timeout.Token));
            AssertReading(Assert.Single(data.RootElement.EnumerateArray()));
            await http.GetStringAsync(dataPath, timeout.Token);
            var custom = dataPath + "?endDate=2026-11-14T12:00:00Z&limit=2";
            await http.GetStringAsync(custom, timeout.Token);
            await http.GetStringAsync(custom, timeout.Token);
            Assert.Equal(3, Volatile.Read(ref requests));
            using var unavailable = await http.GetAsync("/Devices/fail/data", timeout.Token);
            Assert.Equal(HttpStatusCode.BadGateway, unavailable.StatusCode);
            var problem = await unavailable.Content.ReadAsStringAsync(timeout.Token);
            Assert.Contains("429", problem);
            Assert.DoesNotContain("secret", problem);

            foreach (var query in new[] { "?limit=0", "?limit=101", "?limit=abc", "?endDate=invalid" })
            {
                using var invalid = await http.GetAsync(dataPath + query, timeout.Token);
                Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            }
            Assert.Equal(4, Volatile.Read(ref requests));
        }
        finally
        {
            ambient.Stop();
            await serve;
        }
    }

    private static void AssertReading(JsonElement actual)
    {
        using var expected = JsonDocument.Parse("""
            {"date":"2026-11-14T12:00:00Z","baromRel":30.1,"tempOut":68,"humidityOut":55,
             "windDir":270,"windSpeed":3.5,"windGust":8,"eventRain":0.1,"dailyRain":0.2,
             "uvIndex":2,"feelsLike":67.5,"dewPoint":51,"solarRadiation":125}
            """);
        Assert.True(JsonElement.DeepEquals(expected.RootElement, actual), actual.GetRawText());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task HandlerRejectsLimitsBeforeCallingAdapter(int limit)
    {
        var handler = new GetDeviceDataHandler(new UnusedDeviceQuery());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => handler.HandleAsync(new("station", Limit: limit)));
    }

    [Theory]
    [InlineData(200, "not json")]
    [InlineData(200, "null")]
    [InlineData(429, "secret body")]
    public async Task AdapterSanitizesErrorsAndDoesNotCacheFailures(int status, string json)
    {
        using var handler = new StubHandler(status, json);
        using var http = new HttpClient(handler);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var adapter = new AmbientDeviceQuery(http, new AmbientWeatherOptions
        {
            ApiKey = "secret-api", ApplicationKey = "secret-app"
        }, cache);
        for (var i = 0; i < 2; i++)
        {
            var error = await Assert.ThrowsAsync<DeviceQueryException>(() => adapter.GetDevicesAsync(default));
            Assert.DoesNotContain("secret", error.ToString());
        }
        Assert.Equal(2, handler.Calls);
    }

    private sealed class StubHandler(int status, string json) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(json) });
        }
    }

    private sealed class UnusedDeviceQuery : IDeviceQuery
    {
        public Task<IReadOnlyList<DeviceResult>> GetDevicesAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<DeviceDataResult>> GetDeviceDataAsync(string macAddress, DateTime? endDate, int limit,
            CancellationToken cancellationToken) => throw new NotImplementedException();
    }
}
