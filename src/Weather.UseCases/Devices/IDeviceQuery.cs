namespace Weather.UseCases.Devices;

public interface IDeviceQuery
{
    Task<IReadOnlyList<DeviceResult>> GetDevicesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<DeviceDataResult>> GetDeviceDataAsync(
        string macAddress, DateTime? endDate, int limit, CancellationToken cancellationToken);
}
