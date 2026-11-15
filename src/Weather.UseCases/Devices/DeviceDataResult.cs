namespace Weather.UseCases.Devices;

public sealed record DeviceDataResult
{
    public DateTime Date { get; init; }
    public double BaromRel { get; init; }
    public double TempOut { get; init; }
    public int HumidityOut { get; init; }
    public int WindDir { get; init; }
    public double WindSpeed { get; init; }
    public double WindGust { get; init; }
    public double EventRain { get; init; }
    public double DailyRain { get; init; }
    public int UvIndex { get; init; }
    public double FeelsLike { get; init; }
    public double DewPoint { get; init; }
    public double SolarRadiation { get; init; }
}
