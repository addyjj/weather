namespace Weather.UseCases.Devices.StreamWeatherUpdates;

public sealed class StreamWeatherUpdatesHandler(IWeatherUpdateStream stream)
{
    public IAsyncEnumerable<DeviceDataResult> HandleAsync(
        StreamWeatherUpdatesQuery query, CancellationToken cancellationToken = default) => stream.WatchAsync(cancellationToken);
}
