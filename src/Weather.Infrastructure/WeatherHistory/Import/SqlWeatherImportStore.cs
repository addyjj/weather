using System.Data;
using Microsoft.EntityFrameworkCore;
using Weather.UseCases.WeatherHistory.ImportWeatherHistory;

namespace Weather.Infrastructure.WeatherHistory.Import;

public sealed class SqlWeatherImportStore(IDbContextFactory<WeatherHistoryContext> contexts) : IWeatherImportStore
{
    public async Task<long?> GetLatestTimestampAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.Readings.MaxAsync(x => (long?)x.DateUtc, cancellationToken);
    }

    public async Task<int> AddMissingReadingsAsync(IReadOnlyList<WeatherImportReading> readings, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        // Acquire an import lock before range locks to serialize concurrent import writers.
        await db.Database.ExecuteSqlRawAsync("""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource = N'Weather.Import.DeviceData',
                @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000;
            IF @result < 0 THROW 50001, 'Could not acquire weather import lock.', 1;
            """, cancellationToken);
        var added = 0;
        foreach (var batch in readings.DistinctBy(x => x.DateUtc).Chunk(500))
        {
            var ids = batch.Select(x => x.DateUtc).ToArray();
            var existing = (await db.Readings.Where(x => ids.Contains(x.DateUtc))
                .Select(x => x.DateUtc).ToArrayAsync(cancellationToken)).ToHashSet();
            var missing = batch.Where(x => !existing.Contains(x.DateUtc)).Select(reading => new WeatherReading
            {
                DateUtc = reading.DateUtc,
                TempIn = reading.TempIn,
                HumidityIn = reading.HumidityIn,
                BaromRel = reading.BaromRel,
                BaromAbs = reading.BaromAbs,
                TempOut = reading.TempOut,
                BattOut = reading.BattOut,
                HumidityOut = reading.HumidityOut,
                WindDir = reading.WindDir,
                WindSpeed = reading.WindSpeed,
                WindGust = reading.WindGust,
                MaxDailyGust = reading.MaxDailyGust,
                HourlyRainRate = reading.HourlyRainRate,
                EventRain = reading.EventRain,
                DailyRain = reading.DailyRain,
                WeeklyRain = reading.WeeklyRain,
                MonthlyRain = reading.MonthlyRain,
                TotalRain = reading.TotalRain,
                SolarRadiation = reading.SolarRadiation,
                Uv = reading.Uv,
                BattCo2 = reading.BattCo2,
                FeelsLike = reading.FeelsLike,
                DewPoint = reading.DewPoint,
                FeelsLikeIn = reading.FeelsLikeIn,
                DewPointIn = reading.DewPointIn,
                Loc = reading.Loc,
                Date = reading.Date,
            }).ToArray();
            db.Readings.AddRange(missing);
            await db.SaveChangesAsync(cancellationToken);
            added += missing.Length;
            db.ChangeTracker.Clear();
        }
        await transaction.CommitAsync(cancellationToken);
        return added;
    }
}
