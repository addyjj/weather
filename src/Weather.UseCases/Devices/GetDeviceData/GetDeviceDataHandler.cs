namespace Weather.UseCases.Devices.GetDeviceData;

public sealed class GetDeviceDataHandler(IDeviceQuery devices)
{
    public Task<IReadOnlyList<DeviceDataResult>> HandleAsync(
        GetDeviceDataQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query.MacAddress);
        if (query.Limit is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(query.Limit), "Limit must be between 1 and 100.");

        return devices.GetDeviceDataAsync(query.MacAddress, query.EndDate, query.Limit, cancellationToken);
    }
}
