using System.Text.Json;
using System.Net;
using System.Net.Sockets;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Weather.UseCases.CurrentConditions.GetCurrentConditions;
using Xunit;

namespace Weather.Tests;

public sealed class CurrentConditionsTests
{
    [Fact]
    public async Task HandlerReturnsTheProvidersReading()
    {
        var handler = new GetCurrentConditionsHandler(new FixedConditionsProvider());

        Assert.Equal(FixedConditionsProvider.Conditions, await handler.HandleAsync(new GetCurrentConditionsQuery()));
    }

    [Fact]
    public async Task HttpServerExposesBothToolsAndReturnsCurrentConditions()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var portReservation = new TcpListener(IPAddress.Loopback, 0);
        portReservation.Start();
        var port = ((IPEndPoint)portReservation.LocalEndpoint).Port;
        portReservation.Stop();
        using var ambient = new HttpListener();
        var ambientUrl = $"http://127.0.0.1:{port}/";
        ambient.Prefixes.Add(ambientUrl);
        ambient.Start();
        var serve = Task.Run(async () =>
        {
            var request = await ambient.GetContextAsync().WaitAsync(timeout.Token);
            try
            {
                Assert.Equal("/devices/test-station", request.Request.Url!.AbsolutePath);
                Assert.Equal("test-api-key", request.Request.QueryString["apiKey"]);
                Assert.Equal("test-app-key", request.Request.QueryString["applicationKey"]);
                Assert.Equal("1", request.Request.QueryString["limit"]);
                request.Response.ContentType = "application/json";
                await request.Response.OutputStream.WriteAsync("[{\"dateutc\":1794690000000,\"tempf\":68,\"humidity\":55,\"windspeedmph\":3.5,\"dailyrainin\":0}]"u8.ToArray(), timeout.Token);
            }
            finally
            {
                request.Response.Close();
            }
        }, timeout.Token);
        await using var server = await HttpMcpServer.StartAsync(new Dictionary<string, string?>
            {
                ["Ambient__ApiUrl"] = ambientUrl,
                ["Ambient__ApiKey"] = "test-api-key",
                ["Ambient__ApplicationKey"] = "test-app-key",
                ["Ambient__DeviceMacAddress"] = "test-station"
            }, timeout.Token);
        await using var client = await server.ConnectAsync(timeout.Token);
        Assert.Contains(server.LogLines, line => line.Contains("Weather.Mcp.Lifecycle") && line.Contains("Starting Weather MCP server in Production"));
        Assert.Contains(server.LogLines, line => line.Contains("Weather.Mcp.Lifecycle") && line.Contains("MCP ready at " + server.Endpoint));
        Assert.DoesNotContain(server.LogLines, line => line.Contains("Microsoft.Hosting.Lifetime") && line.Contains("INF"));
        using var http = new HttpClient();
        using var invalidHostRequest = new HttpRequestMessage(HttpMethod.Get, server.Endpoint);
        invalidHostRequest.Headers.Host = "untrusted.example";
        using var rejectedHost = await http.SendAsync(invalidHostRequest, timeout.Token);
        Assert.Equal(HttpStatusCode.BadRequest, rejectedHost.StatusCode);

        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        Assert.Equal(2, tools.Count);
        var tool = Assert.Single(tools, x => x.Name == "get_current_conditions");
        Assert.Contains(tools, x => x.Name == "query_weather_history");
        Assert.Equal("get_current_conditions", tool.Name);
        Assert.Contains("Fahrenheit", tool.Description);
        Assert.Empty(await client.ListResourcesAsync(cancellationToken: timeout.Token));
        Assert.Empty(await client.ListResourceTemplatesAsync(cancellationToken: timeout.Token));

        var result = await client.CallToolAsync("get_current_conditions",
            cancellationToken: timeout.Token);

        Assert.False(result.IsError is true);
        var text = Assert.Single(result.Content.OfType<TextContentBlock>()).Text;
        using var json = JsonDocument.Parse(text);
        var conditions = json.RootElement;
        Assert.Equal(68, conditions.GetProperty("temperatureFahrenheit").GetDouble());
        Assert.Equal(55, conditions.GetProperty("humidityPercent").GetDouble());
        Assert.Equal(3.5, conditions.GetProperty("windSpeedMph").GetDouble());
        Assert.Equal(0, conditions.GetProperty("dailyRainInches").GetDouble());
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1794690000000), conditions.GetProperty("observedAtUtc").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, conditions.GetProperty("uvIndex").ValueKind);
        await serve;
        while (!server.LogLines.Any(line => line.Contains("Weather.Mcp.ToolCalls") && line.Contains("get_current_conditions")))
            await Task.Delay(20, timeout.Token);
        Assert.Contains(server.LogLines, line => line.Contains("get_current_conditions") && line.Contains("parameters {}"));
        Assert.DoesNotContain(server.LogLines, line => line.Contains("no handler is available") || line.Contains("request handler failed"));
    }

    private sealed class FixedConditionsProvider : ICurrentConditionsProvider
    {
        public static readonly CurrentConditionsResult Conditions = new()
        {
            TemperatureFahrenheit = 21.5, HumidityPercent = 80, WindSpeedMph = 12
        };

        public Task<CurrentConditionsResult> GetCurrentConditionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Conditions);
    }
}
