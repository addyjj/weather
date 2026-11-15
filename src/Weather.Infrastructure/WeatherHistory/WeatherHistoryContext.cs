using Microsoft.EntityFrameworkCore;

namespace Weather.Infrastructure.WeatherHistory;

public sealed class WeatherHistoryContext(DbContextOptions<WeatherHistoryContext> options) : DbContext(options)
{
    public DbSet<WeatherReading> Readings => Set<WeatherReading>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<WeatherReading>();
        entity.ToTable("DeviceData", "dbo");
        entity.HasKey(x => x.DateUtc);
        entity.Property(x => x.DateUtc).ValueGeneratedNever();
        entity.Property(x => x.Loc).HasColumnType("varchar(100)").IsRequired();
        entity.Property(x => x.Date).HasColumnType("datetime");
        entity.Ignore(x => x.Year);
        entity.Ignore(x => x.Month);
        entity.Ignore(x => x.Day);
        entity.Ignore(x => x.Hour);
        entity.Ignore(x => x.LocalDate);
    }

    public IQueryable<WeatherReading> QueryReadings(string sqlTimeZone = "UTC") => Readings.AsNoTracking().Select(x => new
    {
        Reading = x,
        LocalDate = EF.Functions.AtTimeZone(EF.Functions.AtTimeZone(x.Date, "UTC"), sqlTimeZone)
    }).Select(projected => new WeatherReading
    {
        DateUtc = projected.Reading.DateUtc,
        TempIn = projected.Reading.TempIn,
        HumidityIn = projected.Reading.HumidityIn,
        BaromRel = projected.Reading.BaromRel,
        BaromAbs = projected.Reading.BaromAbs,
        TempOut = projected.Reading.TempOut,
        BattOut = projected.Reading.BattOut,
        HumidityOut = projected.Reading.HumidityOut,
        WindDir = projected.Reading.WindDir,
        WindSpeed = projected.Reading.WindSpeed,
        WindGust = projected.Reading.WindGust,
        MaxDailyGust = projected.Reading.MaxDailyGust,
        HourlyRainRate = projected.Reading.HourlyRainRate,
        EventRain = projected.Reading.EventRain,
        DailyRain = projected.Reading.DailyRain,
        WeeklyRain = projected.Reading.WeeklyRain,
        MonthlyRain = projected.Reading.MonthlyRain,
        TotalRain = projected.Reading.TotalRain,
        SolarRadiation = projected.Reading.SolarRadiation,
        Uv = projected.Reading.Uv,
        BattCo2 = projected.Reading.BattCo2,
        FeelsLike = projected.Reading.FeelsLike,
        DewPoint = projected.Reading.DewPoint,
        FeelsLikeIn = projected.Reading.FeelsLikeIn,
        DewPointIn = projected.Reading.DewPointIn,
        Loc = projected.Reading.Loc,
        Date = projected.Reading.Date,
        LocalDate = projected.LocalDate,
        Year = projected.LocalDate.Year, Month = projected.LocalDate.Month, Day = projected.LocalDate.Day, Hour = projected.LocalDate.Hour
    });
}
