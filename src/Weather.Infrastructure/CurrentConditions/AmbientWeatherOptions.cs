namespace Weather.Infrastructure.CurrentConditions;

public sealed class AmbientWeatherOptions
{
    public string ApiUrl { get; set; } = "https://api.ambientweather.net/v1/";
    public string ApiKey { get; set; } = "";
    public string ApplicationKey { get; set; } = "";
    public string DeviceMacAddress { get; set; } = "";
    public string SocketUrl { get; set; } = "https://rt2.ambientweather.net";
}
