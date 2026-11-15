namespace Weather.Infrastructure.WeatherHistory;

// Storage/read model: date parts are projected into SQL, never stored as columns.
public sealed class WeatherReading
{
    public long DateUtc { get; set; }
    public double TempIn { get; set; }
    public int HumidityIn { get; set; }
    public double BaromRel { get; set; }
    public double BaromAbs { get; set; }
    public double TempOut { get; set; }
    public bool BattOut { get; set; }
    public int HumidityOut { get; set; }
    public int WindDir { get; set; }
    public double WindSpeed { get; set; }
    public double WindGust { get; set; }
    public double MaxDailyGust { get; set; }
    public double HourlyRainRate { get; set; }
    public double EventRain { get; set; }
    public double DailyRain { get; set; }
    public double WeeklyRain { get; set; }
    public double MonthlyRain { get; set; }
    public double TotalRain { get; set; }
    public double SolarRadiation { get; set; }
    public int Uv { get; set; }
    public bool BattCo2 { get; set; }
    public double FeelsLike { get; set; }
    public double DewPoint { get; set; }
    public double FeelsLikeIn { get; set; }
    public double DewPointIn { get; set; }
    public string Loc { get; set; } = "";
    public DateTime Date { get; set; }
    public DateTimeOffset LocalDate { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public int Day { get; set; }
    public int Hour { get; set; }
}
