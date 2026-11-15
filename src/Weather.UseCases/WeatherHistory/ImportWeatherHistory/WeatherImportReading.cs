namespace Weather.UseCases.WeatherHistory.ImportWeatherHistory;

public sealed record WeatherImportReading
{
    public long DateUtc { get; init; }
    public double TempIn { get; init; }
    public int HumidityIn { get; init; }
    public double BaromRel { get; init; }
    public double BaromAbs { get; init; }
    public double TempOut { get; init; }
    public bool BattOut { get; init; }
    public int HumidityOut { get; init; }
    public int WindDir { get; init; }
    public double WindSpeed { get; init; }
    public double WindGust { get; init; }
    public double MaxDailyGust { get; init; }
    public double HourlyRainRate { get; init; }
    public double EventRain { get; init; }
    public double DailyRain { get; init; }
    public double WeeklyRain { get; init; }
    public double MonthlyRain { get; init; }
    public double TotalRain { get; init; }
    public double SolarRadiation { get; init; }
    public int Uv { get; init; }
    public bool BattCo2 { get; init; }
    public double FeelsLike { get; init; }
    public double DewPoint { get; init; }
    public double FeelsLikeIn { get; init; }
    public double DewPointIn { get; init; }
    public string Loc { get; init; } = "";
    public DateTime Date { get; init; }
}
