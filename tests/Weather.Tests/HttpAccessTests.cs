using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Configuration.UserSecrets;
using Weather.WebApi.Hubs;
using Xunit;

namespace Weather.Tests;

public sealed class HttpAccessTests
{
    [Theory]
    [InlineData("https://weather-ui.example")]
    [InlineData("http://localhost:5173")]
    public async Task AllowedOriginsCanReadRestAndNegotiateSignalR(string origin)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var environment = BaseEnvironment();
        environment["Cors__AllowedOrigins__0"] = "https://weather-ui.example";
        environment["Cors__AllowedOrigins__1"] = "http://localhost:5173";
        await using var server = await HttpMcpServer.StartRestAsync(environment, timeout.Token);
        using var http = new HttpClient { BaseAddress = server.Endpoint };
        http.DefaultRequestHeaders.Add("Origin", origin);

        using var response = await http.GetAsync("/health", timeout.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(origin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/weatherHub/negotiate?negotiateVersion=1");
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        preflight.Headers.Add("Access-Control-Request-Headers", "content-type,x-signalr-user-agent");
        using var preflightResponse = await http.SendAsync(preflight, timeout.Token);
        Assert.Equal(HttpStatusCode.NoContent, preflightResponse.StatusCode);
        Assert.Equal(origin, Assert.Single(preflightResponse.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Contains("POST", string.Join(",", preflightResponse.Headers.GetValues("Access-Control-Allow-Methods")));
        Assert.Contains("x-signalr-user-agent", string.Join(",", preflightResponse.Headers.GetValues("Access-Control-Allow-Headers")));

        using var negotiated = await http.PostAsync("/weatherHub/negotiate?negotiateVersion=1", null, timeout.Token);
        Assert.Equal(HttpStatusCode.OK, negotiated.StatusCode);
        Assert.Equal(origin, Assert.Single(negotiated.Headers.GetValues("Access-Control-Allow-Origin")));
        using var json = JsonDocument.Parse(await negotiated.Content.ReadAsStringAsync(timeout.Token));
        Assert.Contains(json.RootElement.GetProperty("availableTransports").EnumerateArray(),
            transport => transport.GetProperty("transport").GetString() == "WebSockets");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnlistedOriginsDoNotReceiveCorsPermission(bool configured)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var environment = BaseEnvironment();
        environment["Cors__AllowedOrigins__0"] = configured ? "https://trusted.example" : "";
        await using var server = await HttpMcpServer.StartRestAsync(environment, timeout.Token);
        using var http = new HttpClient { BaseAddress = server.Endpoint };
        http.DefaultRequestHeaders.Add("Origin", "https://unlisted.example");
        using var response = await http.GetAsync("/health", timeout.Token);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        using var request = new HttpRequestMessage(HttpMethod.Options, "/weatherHub/negotiate");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        using var preflight = await http.SendAsync(request, timeout.Token);
        Assert.False(preflight.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HttpsRedirectionCanBeEnabledOrDisabled(bool enabled)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var environment = BaseEnvironment();
        environment["Https__RedirectEnabled"] = enabled.ToString();
        environment["Https__Port"] = "5443";
        await using var server = await HttpMcpServer.StartRestAsync(environment, timeout.Token);
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = server.Endpoint };
        using var response = await http.GetAsync("/health?check=1", timeout.Token);
        Assert.Equal(enabled ? HttpStatusCode.TemporaryRedirect : HttpStatusCode.OK, response.StatusCode);
        if (enabled) Assert.Equal(new Uri("https://127.0.0.1:5443/health?check=1"), response.Headers.Location);
        else Assert.Null(response.Headers.Location);
    }

    [Theory]
    [InlineData("Development", null, null, "https://secret-ui.example", true)]
    [InlineData("Development", "https://env-ui.example", null, "https://env-ui.example", true)]
    [InlineData("Development", "https://env-ui.example", "https://cli-ui.example", "https://cli-ui.example", true)]
    [InlineData("Production", null, null, "https://secret-ui.example", false)]
    public async Task LoadsDevelopmentUserSecretsWithEnvironmentAndCommandLinePrecedence(
        string hostingEnvironment, string? environmentOrigin, string? commandLineOrigin, string requestOrigin, bool allowed)
    {
        var secretsId = typeof(WeatherHub).Assembly.GetCustomAttribute<UserSecretsIdAttribute>()!.UserSecretsId;
        Assert.Equal(secretsId, typeof(Weather.Mcp.Tools.CurrentConditionsTools).Assembly.GetCustomAttribute<UserSecretsIdAttribute>()!.UserSecretsId);
        Assert.Equal(secretsId, Assembly.Load("Weather.Import").GetCustomAttribute<UserSecretsIdAttribute>()!.UserSecretsId);
        // The suite uses Windows LocalDB. Isolate APPDATA for the child process only;
        // never read or change the developer's real User Secrets store.
        var profile = Path.Combine(Path.GetTempPath(), "WeatherSecretTests_" + Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(profile, "Microsoft", "UserSecrets", secretsId);
        Directory.CreateDirectory(directory);
        var secretsFile = Path.Combine(directory, "secrets.json");
        await File.WriteAllTextAsync(secretsFile, """{"Cors:AllowedOrigins:0":"https://secret-ui.example"}""");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var environment = BaseEnvironment();
            environment["APPDATA"] = profile;
            environment["ASPNETCORE_ENVIRONMENT"] = hostingEnvironment;
            environment["DOTNET_ENVIRONMENT"] = hostingEnvironment;
            if (environmentOrigin is not null) environment["Cors__AllowedOrigins__0"] = environmentOrigin;
            string[]? args = commandLineOrigin is null ? null : ["--Cors:AllowedOrigins:0", commandLineOrigin];
            await using var server = await HttpMcpServer.StartRestAsync(environment, timeout.Token, args);
            using var http = new HttpClient { BaseAddress = server.Endpoint };
            http.DefaultRequestHeaders.Add("Origin", requestOrigin);
            using var response = await http.GetAsync("/health", timeout.Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(allowed, response.Headers.Contains("Access-Control-Allow-Origin"));
            if (allowed) Assert.Equal(requestOrigin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        }
        finally
        {
            File.Delete(secretsFile);
            Directory.Delete(directory);
            Directory.Delete(Path.GetDirectoryName(directory)!);
            Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(directory)!)!);
            Directory.Delete(profile);
        }
    }

    [Fact]
    public async Task ApiSpecificHostFilterOverridesSharedRuntimeConfiguration()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var environment = BaseEnvironment();
        environment["AllowedHosts"] = "*";
        environment["WeatherHttp__AllowedHosts"] = "127.0.0.1";
        await using var server = await HttpMcpServer.StartRestAsync(environment, timeout.Token);
        using var http = new HttpClient { BaseAddress = server.Endpoint };
        using var allowed = await http.GetAsync("/health", timeout.Token);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Host = "untrusted.example";
        using var rejected = await http.SendAsync(request, timeout.Token);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
    }

    private static Dictionary<string, string?> BaseEnvironment() => new()
    {
        ["Ambient__ApiKey"] = "", ["Ambient__ApplicationKey"] = "",
        ["Ambient__SocketUrl"] = "http://127.0.0.1:1", ["Https__RedirectEnabled"] = "false"
    };
}
