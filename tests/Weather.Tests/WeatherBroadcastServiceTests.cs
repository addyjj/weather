using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Weather.UseCases.Devices;
using Weather.UseCases.Devices.StreamWeatherUpdates;
using Weather.WebApi.HostedServices;
using Weather.WebApi.Hubs;
using Xunit;

namespace Weather.Tests;

public sealed class WeatherBroadcastServiceTests
{
    [Fact]
    public async Task BroadcastsReadingsInOrderThroughTheApplicationStreamAndStopsOnCancellation()
    {
        var first = new DeviceDataResult { TempOut = 68 };
        var second = new DeviceDataResult { TempOut = 72 };
        var stream = new TestStream(first, second);
        var client = new RecordingClient();
        using var service = new WeatherBroadcastService(new StreamWeatherUpdatesHandler(stream),
            new TestHubContext(client), NullLogger<WeatherBroadcastService>.Instance);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await service.StartAsync(timeout.Token);
        await client.Delivered.Task.WaitAsync(timeout.Token);
        await service.StopAsync(timeout.Token);

        Assert.Equal(new[] { first, second }, client.Readings);
        Assert.True(stream.CancellationObserved);
        Assert.True(service.ExecuteTask!.IsCompletedSuccessfully);
    }

    private sealed class TestStream(params DeviceDataResult[] readings) : IWeatherUpdateStream
    {
        public bool CancellationObserved { get; private set; }
        public async IAsyncEnumerable<DeviceDataResult> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            try
            {
                foreach (var reading in readings) yield return reading;
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            finally { CancellationObserved = cancellationToken.IsCancellationRequested; }
        }
    }

    private sealed class RecordingClient : IClientProxy
    {
        public List<DeviceDataResult> Readings { get; } = [];
        public TaskCompletionSource Delivered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            Assert.Equal("weatherData", method);
            Readings.Add(Assert.IsType<DeviceDataResult>(Assert.Single(args)));
            if (Readings.Count == 2) Delivered.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class TestHubContext(IClientProxy client) : IHubContext<WeatherHub>
    {
        public IHubClients Clients { get; } = new TestClients(client);
        public IGroupManager Groups => throw new NotSupportedException();
    }

    private sealed class TestClients(IClientProxy client) : IHubClients
    {
        public IClientProxy All => client;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Client(string connectionId) => throw new NotSupportedException();
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
        public IClientProxy Group(string groupName) => throw new NotSupportedException();
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
        public IClientProxy User(string userId) => throw new NotSupportedException();
        public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
    }
}
