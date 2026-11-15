namespace Weather.UseCases.Devices;

public sealed record DeviceResult
{
    public string Name { get; init; } = "";
    public string Location { get; init; } = "";
    public DeviceDataResult LatestData { get; init; } = new();
}
