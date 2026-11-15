using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using System.Text.RegularExpressions;
using Weather.Infrastructure.WeatherHistory;
using Xunit;

namespace Weather.Tests;

public sealed class MigrationTests(HistoryDatabase database) : IClassFixture<HistoryDatabase>
{
    private const string Initial = "20260213032620_InitialCreate";

    [Fact]
    public async Task CreatesCurrentSchemaFromOriginalMigrationAndCanBeAppliedAgain()
    {
        await using var db = await CreateContextAsync();
        await ResetAsync(db);
        await ApplyIdempotentScriptAsync(db);
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Contains(Initial, await db.Database.GetAppliedMigrationsAsync());
        Assert.Equal(2, (await db.Database.GetAppliedMigrationsAsync()).Count());
        await AssertColumnTypesAsync(db, "varchar", "datetime");
        db.Readings.Add(new WeatherReading { DateUtc = 1, Date = new DateTime(2026, 1, 1), Loc = "North", TempOut = 68 });
        await db.SaveChangesAsync();
        await ApplyIdempotentScriptAsync(db);
        await db.Database.MigrateAsync();
        Assert.Equal(68, (await db.Readings.AsNoTracking().SingleAsync()).TempOut);
    }

    [Fact]
    public async Task UpgradesAnAlreadyAlignedDatabaseWithOriginalMigrationHistoryWithoutLosingReadings()
    {
        await using var db = await CreateContextAsync();
        await ResetAsync(db);
        await db.GetService<IMigrator>().MigrateAsync(Initial);
        // Reproduce the live database: original migration ID, already-aligned column types.
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE dbo.DeviceData ALTER COLUMN Loc varchar(100) NOT NULL; ALTER TABLE dbo.DeviceData ALTER COLUMN [Date] datetime NOT NULL;");
        db.Readings.Add(new WeatherReading { DateUtc = 1, Date = new DateTime(2026, 1, 1), Loc = "Existing", TempOut = 72 });
        await db.SaveChangesAsync();
        await db.Database.MigrateAsync();
        var reading = await db.Readings.AsNoTracking().SingleAsync();
        Assert.Equal("Existing", reading.Loc);
        Assert.Equal(72, reading.TempOut);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        await AssertColumnTypesAsync(db, "varchar", "datetime");
    }

    [Theory]
    [InlineData(true, 50002)]
    [InlineData(false, 50003)]
    public async Task RefusesLossyUpgradesAndRollsBack(bool unicodeLocation, int expectedError)
    {
        await using var db = await CreateContextAsync();
        await ResetAsync(db);
        await db.GetService<IMigrator>().MigrateAsync(Initial);
        db.Readings.Add(new WeatherReading { DateUtc = 1, Date = new DateTime(2026, 1, 1), Loc = "North" });
        await db.SaveChangesAsync();
        if (unicodeLocation)
            await db.Database.ExecuteSqlRawAsync("UPDATE dbo.DeviceData SET Loc = N'漢字'");
        else
            await db.Database.ExecuteSqlRawAsync("UPDATE dbo.DeviceData SET [Date] = CAST('2026-01-01T00:00:00.1234567' AS datetime2)");
        var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.MigrateAsync());
        Assert.Equal(expectedError, error.Number);
        Assert.Equal(new[] { Initial }, await db.Database.GetAppliedMigrationsAsync());
        await AssertColumnTypesAsync(db, "nvarchar", "datetime2");
        Assert.Equal(1, await db.Readings.CountAsync());
    }

    private Task<WeatherHistoryContext> CreateContextAsync() => database.Services
        .GetRequiredService<IDbContextFactory<WeatherHistoryContext>>().CreateDbContextAsync();

    private static Task ResetAsync(WeatherHistoryContext db) => db.Database.ExecuteSqlRawAsync(
        "DROP TABLE IF EXISTS dbo.DeviceData; DROP TABLE IF EXISTS dbo.__EFMigrationsHistory;");

    private static async Task ApplyIdempotentScriptAsync(WeatherHistoryContext db)
    {
        var script = db.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
        await db.Database.OpenConnectionAsync();
        try
        {
            foreach (var batch in Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
                if (!string.IsNullOrWhiteSpace(batch)) await db.Database.ExecuteSqlRawAsync(batch);
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    private static async Task AssertColumnTypesAsync(WeatherHistoryContext db, string locationType, string dateType)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT name, TYPE_NAME(system_type_id) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.DeviceData')";
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new Dictionary<string, string>();
        while (await reader.ReadAsync()) columns.Add(reader.GetString(0), reader.GetString(1));
        Assert.Equal(27, columns.Count);
        Assert.Equal(locationType, columns["Loc"]);
        Assert.Equal(dateType, columns["Date"]);
    }
}
