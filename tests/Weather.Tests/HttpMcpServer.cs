using System.Diagnostics;
using System.Collections.Concurrent;
using ModelContextProtocol.Client;

namespace Weather.Tests;

// Launch the production HTTP host on an OS-assigned loopback port.
internal sealed class HttpMcpServer : IAsyncDisposable
{
    private readonly Process process;
    private readonly Task output;
    private readonly Task errors;
    public Uri Endpoint { get; private set; } = null!;
    public ConcurrentQueue<string> LogLines { get; }

    private HttpMcpServer(Process process, Task output, Task errors, ConcurrentQueue<string> logLines)
    {
        this.process = process;
        this.output = output;
        this.errors = errors;
        LogLines = logLines;
    }

    public static Task<HttpMcpServer> StartAsync(IReadOnlyDictionary<string, string?> environment, CancellationToken ct) =>
        StartHostAsync(environment, "Weather.Mcp", "MCP ready at ", ct);

    public static Task<HttpMcpServer> StartRestAsync(IReadOnlyDictionary<string, string?> environment, CancellationToken ct,
        IReadOnlyList<string>? args = null) =>
        StartHostAsync(environment, "Weather.WebApi", "REST API ready at ", ct, args);

    private static async Task<HttpMcpServer> StartHostAsync(
        IReadOnlyDictionary<string, string?> environment, string host, string marker, CancellationToken ct,
        IReadOnlyList<string>? args = null)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var serverPath = Path.Combine(root, "src", host, "bin", configuration, "net10.0", host + ".dll");
        var info = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(serverPath)!
        };
        info.ArgumentList.Add(serverPath);
        foreach (var arg in args ?? []) info.ArgumentList.Add(arg);
        foreach (var entry in environment) info.Environment[entry.Key] = entry.Value;
        info.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:0";
        info.Environment["ASPNETCORE_ENVIRONMENT"] = environment.GetValueOrDefault("ASPNETCORE_ENVIRONMENT") ?? "Production";
        info.Environment["DOTNET_ENVIRONMENT"] = environment.GetValueOrDefault("DOTNET_ENVIRONMENT") ?? "Production";
        info.Environment["Logging__Console__FormatterOptions__ColorBehavior"] = "Disabled";
        var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start HTTP MCP server.");
        var ready = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        var logLines = new ConcurrentQueue<string>();
        var output = Task.Run(async () =>
        {
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                logLines.Enqueue(line);
                var index = line.IndexOf(marker, StringComparison.Ordinal);
                if (index >= 0 && Uri.TryCreate(line[(index + marker.Length)..].Trim(), UriKind.Absolute, out var address))
                    ready.TrySetResult(address);
            }
            ready.TrySetException(new InvalidOperationException("HTTP MCP server exited before listening."));
        });
        var errors = process.StandardError.ReadToEndAsync();
        var server = new HttpMcpServer(process, output, errors, logLines);
        try
        {
            server.Endpoint = await ready.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
            return server;
        }
        catch
        {
            await server.DisposeAsync();
            throw;
        }
    }

    public Task<McpClient> ConnectAsync(CancellationToken ct) => McpClient.CreateAsync(
        new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = Endpoint, TransportMode = HttpTransportMode.StreamableHttp
        }), cancellationToken: ct);

    public async ValueTask DisposeAsync()
    {
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();
        await Task.WhenAll(output, errors);
        process.Dispose();
    }
}
