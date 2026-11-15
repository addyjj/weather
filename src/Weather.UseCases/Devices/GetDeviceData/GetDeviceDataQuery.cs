namespace Weather.UseCases.Devices.GetDeviceData;

public sealed record GetDeviceDataQuery(string MacAddress, DateTime? EndDate = null, int Limit = 10);
