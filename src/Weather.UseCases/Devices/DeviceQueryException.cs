namespace Weather.UseCases.Devices;

public sealed class DeviceQueryException(string message) : Exception(message);
