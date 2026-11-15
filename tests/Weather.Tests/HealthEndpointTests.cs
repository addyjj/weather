using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Xunit;

namespace Weather.Tests;

public sealed class HealthEndpointTests(HistoryDatabase database) : IClassFixture<HistoryDatabase>
{
    [Fact]
    public async Task ReportsRealDependencyHealthAndDoesNotCacheAmbientFailures()
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
            for (var i = 0; i < 2; i++)
            {
                var context = await ambient.GetContextAsync().WaitAsync(timeout.Token);
                try
                {
                    Interlocked.Increment(ref requests);
                    Assert.Equal("/devices/test-station", context.Request.Url!.AbsolutePath);
                    Assert.Equal("1", context.Request.QueryString["limit"]);
                    Assert.Equal("secret-api", context.Request.QueryString["apiKey"]);
                    Assert.Equal("secret-app", context.Request.QueryString["applicationKey"]);
                    context.Response.ContentType = "application/json";
                    context.Response.StatusCode = i == 0 ? 200 : 429;
                    var bytes = i == 0 ? "[{\"tempf\":68}]"u8.ToArray() : "secret upstream body"u8.ToArray();
                    await context.Response.OutputStream.WriteAsync(bytes, timeout.Token);
                }
                finally { context.Response.Close(); }
            }
        });

        await using var server = await HttpMcpServer.StartRestAsync(new Dictionary<string, string?>
        {
            ["ConnectionStrings__sql"] = database.ConnectionString,
            ["Ambient__ApiUrl"] = ambientUrl,
            ["Ambient__SocketUrl"] = "http://127.0.0.1:1",
            ["Ambient__ApiKey"] = "secret-api",
            ["Ambient__ApplicationKey"] = "secret-app",
            ["Ambient__DeviceMacAddress"] = "test-station"
        }, timeout.Token);
        using var http = new HttpClient { BaseAddress = server.Endpoint };
        Assert.Equal("Healthy", await http.GetStringAsync("/health", timeout.Token));
        Assert.Equal(0, Volatile.Read(ref requests));

        using var healthyResponse = await http.GetAsync("/health/details", timeout.Token);
        Assert.Equal(HttpStatusCode.OK, healthyResponse.StatusCode);
        Assert.Equal("application/json", healthyResponse.Content.Headers.ContentType?.MediaType);
        Assert.True(healthyResponse.Headers.CacheControl?.NoStore);
        using var healthy = JsonDocument.Parse(await healthyResponse.Content.ReadAsStringAsync(timeout.Token));
        Assert.Equal("Healthy", healthy.RootElement.GetProperty("status").GetString());
        var checks = healthy.RootElement.GetProperty("checks").EnumerateArray().ToArray();
        Assert.Equal(2, checks.Length);
        Assert.Equal(new[] { "ambient_weather_api", "database" }, checks.Select(x => x.GetProperty("name").GetString()).Order());
        foreach (var check in checks)
        {
            Assert.Equal(new[] { "name", "status", "description", "exception" }, check.EnumerateObject().Select(x => x.Name));
            Assert.Equal("Healthy", check.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, check.GetProperty("exception").ValueKind);
        }

        using var failedResponse = await http.GetAsync("/health/details", timeout.Token);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failedResponse.StatusCode);
        var body = await failedResponse.Content.ReadAsStringAsync(timeout.Token);
        Assert.DoesNotContain("secret", body);
        using var failed = JsonDocument.Parse(body);
        Assert.Equal("Unhealthy", failed.RootElement.GetProperty("status").GetString());
        var entries = failed.RootElement.GetProperty("checks").EnumerateArray().ToDictionary(x => x.GetProperty("name").GetString()!);
        Assert.Equal("Healthy", entries["database"].GetProperty("status").GetString());
        Assert.Equal("Unhealthy", entries["ambient_weather_api"].GetProperty("status").GetString());
        Assert.Equal("Healthy", await http.GetStringAsync("/health", timeout.Token));
        Assert.Equal(2, Volatile.Read(ref requests));
        await serve;
    }

    [Theory]
    [InlineData("")]
    [InlineData("unsupported-secret=secret-db")]
    [InlineData("Server=127.0.0.1,1;Database=secret-db;User ID=secret-user;Password=secret-password;Encrypt=false;Connect Timeout=1;ConnectRetryCount=0")]
    public async Task MissingOrInvalidConfigurationIsUnhealthyWithoutBreakingLiveness(string connectionString)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var server = await HttpMcpServer.StartRestAsync(new Dictionary<string, string?>
        {
            ["ConnectionStrings__sql"] = connectionString,
            ["Ambient__ApiKey"] = "",
            ["Ambient__ApplicationKey"] = "",
            ["Ambient__DeviceMacAddress"] = "",
            ["Ambient__SocketUrl"] = "http://127.0.0.1:1"
        }, timeout.Token);
        using var http = new HttpClient { BaseAddress = server.Endpoint };
        Assert.Equal("Healthy", await http.GetStringAsync("/health", timeout.Token));
        using var response = await http.GetAsync("/health/details", timeout.Token);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(timeout.Token);
        Assert.DoesNotContain("secret", body);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("Unhealthy", json.RootElement.GetProperty("status").GetString());
        Assert.All(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
        {
            Assert.Equal("Unhealthy", check.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, check.GetProperty("exception").ValueKind);
        });
    }
}
