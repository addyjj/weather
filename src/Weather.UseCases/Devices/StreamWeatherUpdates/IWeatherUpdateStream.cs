namespace Weather.UseCases.Devices.StreamWeatherUpdates;

public interface IWeatherUpdateStream
{
    IAsyncEnumerable<DeviceDataResult> WatchAsync(CancellationToken cancellationToken);
}
