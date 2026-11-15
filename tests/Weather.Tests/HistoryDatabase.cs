using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Weather.Infrastructure.WeatherHistory;
using Weather.UseCases.WeatherHistory.QueryWeatherHistory;
using Xunit;

namespace Weather.Tests;

public sealed class HistoryDatabase : IAsyncLifetime
{
    private readonly string databaseName = "WeatherMcpTests_" + Guid.NewGuid().ToString("N");
    private const string Master = @"Server=(localdb)\MSSQLLocalDB;Database=master;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=15";
    public string ConnectionString => new SqlConnectionStringBuilder(Master) { InitialCatalog = databaseName }.ConnectionString;
    public ServiceProvider Services { get; private set; } = null!;
    public SqlCapture Capture { get; } = new();
    private bool created;

    public async Task InitializeAsync()
    {
        try
        {
            await using var master = new SqlConnection(Master);
            await master.OpenAsync();
            await using var create = master.CreateCommand();
            create.CommandText = $"CREATE DATABASE [{databaseName}]";
            await create.ExecuteNonQueryAsync();
            created = true;
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var schema = connection.CreateCommand();
            schema.CommandText = """
                CREATE TABLE dbo.DeviceData (
                            [DateUtc] bigint NOT NULL PRIMARY KEY,
                    [TempIn] float NOT NULL,
                    [HumidityIn] int NOT NULL,
                    [BaromRel] float NOT NULL,
                    [BaromAbs] float NOT NULL,
                    [TempOut] float NOT NULL,
                    [BattOut] bit NOT NULL,
                    [HumidityOut] int NOT NULL,
                    [WindDir] int NOT NULL,
                    [WindSpeed] float NOT NULL,
                    [WindGust] float NOT NULL,
                    [MaxDailyGust] float NOT NULL,
                    [HourlyRainRate] float NOT NULL,
                    [EventRain] float NOT NULL,
                    [DailyRain] float NOT NULL,
                    [WeeklyRain] float NOT NULL,
                    [MonthlyRain] float NOT NULL,
                    [TotalRain] float NOT NULL,
                    [SolarRadiation] float NOT NULL,
                    [Uv] int NOT NULL,
                    [BattCo2] bit NOT NULL,
                    [FeelsLike] float NOT NULL,
                    [DewPoint] float NOT NULL,
                    [FeelsLikeIn] float NOT NULL,
                    [DewPointIn] float NOT NULL,
                    [Loc] varchar(100) NOT NULL,
                    [Date] datetime NOT NULL
                )
                """;
            await schema.ExecuteNonQueryAsync();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddWeatherHistory(ConnectionString);
            services.AddDbContextFactory<WeatherHistoryContext>(options =>
                options.UseSqlServer(ConnectionString, sql => sql.CommandTimeout(30))
                    .ConfigureWarnings(warnings => warnings.Throw(CoreEventId.RowLimitingOperationWithoutOrderByWarning))
                    .AddInterceptors(Capture));
            Services = services.BuildServiceProvider();
            await using var db = await Services.GetRequiredService<IDbContextFactory<WeatherHistoryContext>>().CreateDbContextAsync();
            var dates = new[] { "2025-12-31T23:00:00Z", "2026-01-01T00:00:00Z", "2026-01-01T01:00:00Z",
                "2026-01-01T01:30:00Z", "2026-01-02T00:00:00Z", "2026-02-01T00:00:00Z",
                "2026-12-31T23:00:00Z", "2027-01-01T00:00:00Z" };
            var temperatures = new double[] { -4, 32, 50, 50, 68, 86, 32, 14 };
            db.Readings.AddRange(dates.Select((date, i) => new WeatherReading
            {
                Date = DateTime.Parse(date, null, System.Globalization.DateTimeStyles.AdjustToUniversal),
                DateUtc = DateTimeOffset.Parse(date).ToUnixTimeMilliseconds(),
                TempOut = temperatures[i], TempIn = 72, HumidityOut = 40 + i, HumidityIn = 45,
                WindSpeed = i, WindGust = i + 2, Loc = i % 2 == 0 ? "North" : "South",
                DailyRain = i / 10.0, BattOut = true, BattCo2 = true
            }));
            await db.SaveChangesAsync();
            Capture.Commands.Clear();
        }
        catch (Exception ex)
        {
            await DisposeAsync();
            throw new InvalidOperationException("LocalDB E2E setup failed. Install/start MSSQLLocalDB and run tests with access to your Windows user profile.", ex);
        }
    }

    public Task<WeatherHistoryResult> Query(params (string Key, string Value)[] options) =>
        Services.GetRequiredService<IWeatherHistoryQuery>().ExecuteAsync(
            new QueryWeatherHistoryQuery(options.ToDictionary(x => x.Key, x => x.Value)));

    public async Task DisposeAsync()
    {
        if (Services is not null) await Services.DisposeAsync();
        if (!created) return;
        // Only this fixture's generated GUID database may be removed.
        if (!System.Text.RegularExpressions.Regex.IsMatch(databaseName, "^WeatherMcpTests_[a-f0-9]{32}$"))
            throw new InvalidOperationException("Unsafe test database name.");
        SqlConnection.ClearAllPools();
        await using var connection = new SqlConnection(Master);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]";
        await command.ExecuteNonQueryAsync();
        created = false;
    }
}

public sealed class SqlCapture : DbCommandInterceptor
{
    public ConcurrentQueue<string> Commands { get; } = new();
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Commands.Enqueue(command.CommandText);
        return ValueTask.FromResult(result);
    }
}
