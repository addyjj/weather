using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using SocketIOClient;
using Weather.Infrastructure.CurrentConditions;
using Weather.UseCases.Devices;
using Weather.UseCases.Devices.StreamWeatherUpdates;

namespace Weather.Infrastructure.Devices;

public sealed class AmbientWeatherUpdateStream(AmbientWeatherOptions options) : IWeatherUpdateStream
{
    public async IAsyncEnumerable<DeviceDataResult> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey) || string.IsNullOrWhiteSpace(options.ApplicationKey))
            throw new DeviceQueryException("Configure Ambient:ApiKey and Ambient:ApplicationKey for live updates.");
        if (!Uri.TryCreate(options.SocketUrl, UriKind.Absolute, out var socketUri) || socketUri.Scheme is not ("http" or "https"))
            throw new DeviceQueryException("Configure a valid Ambient:SocketUrl.");

        var updates = Channel.CreateBounded<DeviceDataResult>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true
        });
        using var socket = new SocketIO(socketUri, new SocketIOOptions
        {
            Transport = SocketIOClient.Common.TransportProtocol.WebSocket,
            // The host owns retries so exhausted or closed streams cannot stall forever.
            Reconnection = false,
            Path = "/socket.io",
            Query = new NameValueCollection { ["api"] = "1", ["applicationKey"] = options.ApplicationKey }
        });
        socket.OnDisconnected += (_, _) => updates.Writer.TryComplete(new DeviceQueryException("Ambient live updates disconnected."));
        socket.OnError += (_, _) => updates.Writer.TryComplete(new DeviceQueryException("Ambient live updates unavailable."));
        socket.On("data", context =>
        {
            try
            {
                var reading = context.GetValue<AmbientDeviceQuery.AmbientReading>(0);
                if (reading is not null) updates.Writer.TryWrite(reading.ToResult());
            }
            catch (System.Text.Json.JsonException) { /* Ignore malformed upstream events. */ }
            return Task.CompletedTask;
        });

        try
        {
            await socket.ConnectAsync(cancellationToken);
            await socket.EmitAsync("subscribe", new object[] { new { apiKeys = new[] { options.ApiKey } } });
            await foreach (var reading in updates.Reader.ReadAllAsync(cancellationToken)) yield return reading;
        }
        finally
        {
            await socket.DisconnectAsync(CancellationToken.None);
        }
    }
}
