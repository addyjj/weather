namespace Weather.UseCases.Devices.GetDevices;

public sealed class GetDevicesHandler(IDeviceQuery devices)
{
    public Task<IReadOnlyList<DeviceResult>> HandleAsync(
        GetDevicesQuery query, CancellationToken cancellationToken = default) =>
        devices.GetDevicesAsync(cancellationToken);
}
